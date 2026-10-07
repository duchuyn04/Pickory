using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Contracts.Events;
using Pickory.Modules.Catalog.Descriptions;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Images;
using Pickory.Modules.Catalog.Events;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Catalog.Products;
using Pickory.Modules.Catalog.Specs;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;
using Pickory.Shared.Json;

namespace Pickory.Modules.Catalog.Features.Products;

public sealed class ProductManagementHandler(
    CatalogDbContext database,
    IAuditLogWriter auditLogWriter,
    IEventDispatcher dispatcher,
    TimeProvider timeProvider,
    ProductImageSync imageSync,
    CloudinaryCredentials cloudinary)
{
    // Một khóa transaction chung ngăn hai request cấp cùng slug hoặc tạo trùng tổ hợp biến thể (SKU).
    public const long ProductWriteLockKey = 578524097137070906;

    public async Task<PagedResponse<ProductResponse>> GetListAsync(
        ProductListQuery query, CancellationToken cancellationToken)
    {
        // Nhân viên cần xem mọi trạng thái, kể cả sản phẩm chưa đăng bán.
        var products = database.Products.AsNoTracking();
        var term = query.Search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            // So sánh không phân biệt hoa thường. Ký tự `%` và `_` giữ nghĩa ký tự thường.
            var lowerTerm = term.ToLowerInvariant();
            products = products.Where(product => product.Name.ToLower().Contains(lowerTerm)
                || product.Skus.Any(sku => sku.Code.ToLower().Contains(lowerTerm)));
        }

        // Lọc danh mục bao gồm cả các sản phẩm thuộc danh mục con trực thuộc.
        var categoryId = query.CategoryId?.Trim();
        if (!string.IsNullOrEmpty(categoryId))
        {
            products = products.Where(product => product.CategoryId == categoryId
                || (product.Category != null && product.Category.ParentId == categoryId));
        }

        // Lọc trạng thái chính xác theo giá trị hợp lệ được truyền từ tham số.
        if (query.Status.HasValue)
        {
            products = products.Where(product => product.Status == query.Status.Value);
        }

        // Lọc sản phẩm có ít nhất một biến thể đang bật và tồn kho dưới hoặc bằng ngưỡng.
        if (query.LowStock)
        {
            products = products.Where(product => product.Skus.Any(sku =>
                sku.Enabled && sku.Stock <= CatalogInventoryDefaults.LowStockThreshold));
        }

        // Đếm cùng bộ lọc để giao diện không hiển thị trang ngoài tập kết quả.
        var total = await products.CountAsync(cancellationToken);
        // Thứ tự phụ theo định danh giữ phân trang ổn định khi thời điểm tạo trùng nhau.
        var items = await products.Include(product => product.Skus).Include(product => product.Images)
            .OrderByDescending(product => product.CreatedAt).ThenBy(product => product.Id)
            .Skip(query.Page.Skip).Take(query.Page.Limit).ToListAsync(cancellationToken);
        return new PagedResponse<ProductResponse>(items.Select(ToResponse).ToList(), total, query.Page.Page, query.Page.Limit);
    }

    public async Task<ProductResponse> GetAsync(string id, CancellationToken cancellationToken)
    {
        // Không lọc SKU tắt vì form cần giữ định danh của các tổ hợp từng tồn tại.
        var product = await database.Products.AsNoTracking().Include(item => item.Skus).Include(item => item.Images)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (product is null)
        {
            // Định danh thiếu nhận mã riêng để giao diện phân biệt lỗi dữ liệu nhập.
            throw ProductNotFound();
        }

        return ToResponse(product);
    }

    public async Task<ProductResponse> CreateAsync(Actor actor, CreateProductRequest request, CancellationToken cancellationToken)
    {
        // Kiểm ảnh mới với kho ảnh trước khi khóa ghi, để lời gọi mạng không giữ khóa sản phẩm.
        await imageSync.VerifyNewImagesAsync(productId: null, request.Images, cancellationToken);
        // Transaction bao gồm sản phẩm, SKU và ảnh. Không phát sự kiện nếu bất kỳ bước ghi nào thất bại.
        return await RunWriteAsync(async events =>
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var product = new Product { Id = Guid.NewGuid().ToString("N"), CreatedAt = now };
            await SaveAsync(actor, product, request, creating: true, events, cancellationToken);
            return ToResponse(product);
        }, cancellationToken);
    }

    public async Task<ProductResponse> UpdateAsync(Actor actor, string id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        // Không gửi `images` thì giữ ảnh cũ, nên chỉ kiểm ảnh khi danh sách ảnh được gửi.
        var targetImages = request.ImagesSpecified ? request.Images : null;
        await imageSync.VerifyNewImagesAsync(id, targetImages, cancellationToken);
        // Đọc và ghi trong cùng transaction để lịch sử slug, giá, ảnh và nhật ký cùng được hoàn nguyên.
        return await RunWriteAsync(async events =>
        {
            // Khóa dòng sản phẩm trước khi đọc, cùng thứ tự khóa với sổ tồn kho. Nhờ vậy `stock` đọc bên dưới là bản sau khi giữ hàng đã commit.
            await LockProductRowAsync(id, cancellationToken);
            var product = await database.Products.Include(item => item.Skus).Include(item => item.Images)
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (product is null)
            {
                // Không tạo mới khi PATCH nhầm định danh sản phẩm.
                throw ProductNotFound();
            }

            // Chỉ lấy SKU thuộc tùy chọn hiện tại. SKU đã bỏ vẫn nằm trong DB để có thể bật lại.
            var oldOptions = ReadOptions(product.Options);
            var oldCombinationKeys = VariantCombinations.Build(oldOptions).Select(CombinationKey).ToHashSet(StringComparer.Ordinal);
            var oldVariants = product.Skus
                .Where(sku => oldCombinationKeys.Contains(CombinationKey(ReadOptionValues(sku.OptionValues))))
                .Select(ToVariantRequest).ToList();
            // Trường không gửi giữ nguyên. Các cờ phân biệt thiếu trường với giá trị null do client gửi.
            var target = new CreateProductRequest(
                CategoryId: request.CategoryIdSpecified ? request.CategoryId! : product.CategoryId,
                BrandId: request.BrandIdSpecified ? request.BrandId! : product.BrandId,
                Name: request.NameSpecified ? request.Name! : product.Name,
                ShortDesc: request.ShortDescSpecified ? request.ShortDesc! : product.ShortDesc,
                Description: request.DescriptionSpecified ? request.Description : product.Description.RootElement,
                Specs: request.SpecsSpecified ? request.Specs : product.Specs.RootElement,
                Options: request.OptionsSpecified ? request.Options : oldOptions,
                WarrantyMonths: request.WarrantyMonthsSpecified ? request.WarrantyMonths : product.WarrantyMonths,
                Variants: request.VariantsSpecified ? request.Variants : oldVariants,
                Images: targetImages);
            await SaveAsync(actor, product, target, creating: false, events, cancellationToken);
            return ToResponse(product);
        }, cancellationToken);
    }

    public async Task<ProductResponse> ChangeStatusAsync(
        Actor actor, string id, ProductStatus target, CancellationToken cancellationToken)
    {
        // Đọc, kiểm tra điều kiện và ghi trong cùng transaction, để audit và trạng thái cùng thành công hoặc cùng hoàn nguyên.
        return await RunWriteAsync(async events =>
        {
            var product = await database.Products.Include(item => item.Skus).Include(item => item.Images)
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (product is null)
            {
                throw ProductNotFound();
            }

            // Trạng thái không đổi thì không có gì để ghi, nên không audit và không phát sự kiện.
            if (product.Status == target)
            {
                return ToResponse(product);
            }

            // Bảng chuyển trạng thái chặn sản phẩm đã phát hành quay về bản nháp.
            if (!ProductPublishing.CanTransition(product.Status, target))
            {
                throw new AppException("INVALID_STATUS_TRANSITION", 409,
                    $"Không thể chuyển sản phẩm từ {ToStatusName(product.Status)} sang {ToStatusName(target)}.");
            }

            // Chỉ đăng bán mới cần đủ điều kiện. Ẩn hoặc ngừng bán luôn được phép.
            if (target == ProductStatus.Active)
            {
                await EnsurePublishableAsync(product, cancellationToken);
            }

            var previousStatus = product.Status;
            product.Status = target;
            product.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
            await database.SaveChangesAsync(cancellationToken);

            // Audit dùng đúng kết nối transaction để lỗi ghi nhật ký hoàn nguyên cả trạng thái.
            await auditLogWriter.WriteAsync(
                new AuditLogEntry(actor.UserId!, "catalog.product.status_change", "product", product.Id,
                    new { status = ToStatusName(previousStatus) }, new { status = ToStatusName(target) }),
                database.Database.CurrentTransaction!.GetDbTransaction(), cancellationToken);
            events.Emit(new ProductChangedEvent(product.Id, ProductChange.Status));
            return ToResponse(product);
        }, cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            // Helper chỉ phát sự kiện sau commit, nên xóa thất bại không phát sự kiện `Deleted`.
            await CatalogTransaction.RunAsync(database, dispatcher, async events =>
            {
                // Dùng chung khóa với tạo và sửa, để không xóa trong lúc request khác đang sửa sản phẩm.
                await database.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock({ProductWriteLockKey})", cancellationToken);
                // Khóa dòng sản phẩm trước khi kiểm movement. Đơn đang giữ hàng chưa commit phải commit xong thì mới thấy movement của nó.
                await LockProductRowAsync(id, cancellationToken);
                var product = await database.Products.Include(item => item.Skus).Include(item => item.Images)
                    .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
                if (product is null)
                {
                    throw ProductNotFound();
                }

                // Movement `order_reserve` nghĩa là SKU từng có đơn. Xóa sản phẩm sẽ làm mất lịch sử đơn hàng.
                var skuIds = product.Skus.Select(sku => sku.Id).ToList();
                var hasOrders = await database.InventoryMovements.AnyAsync(movement =>
                    skuIds.Contains(movement.SkuId) && movement.Type == InventoryMovementType.OrderReserve, cancellationToken);
                if (hasOrders)
                {
                    throw ProductHasOrders();
                }

                // Movement `manual` và `return` không thuộc đơn hàng. Khóa ngoại Restrict buộc xóa chúng trước SKU (Decision 18).
                await database.InventoryMovements
                    .Where(movement => skuIds.Contains(movement.SkuId))
                    .ExecuteDeleteAsync(cancellationToken);

                // Job xóa ảnh nằm trong cùng transaction, để ảnh chỉ bị xóa khỏi Cloudinary khi sản phẩm đã bị xóa.
                imageSync.ScheduleDeletionOfAllImages(product, timeProvider.GetUtcNow().UtcDateTime);
                database.Products.Remove(product);
                await database.SaveChangesAsync(cancellationToken);
                events.Emit(new ProductChangedEvent(product.Id, ProductChange.Deleted));
                return true;
            }, cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.ForeignKeyViolation, ConstraintName: "inventory_movements_skuId_fkey" })
        {
            // Movement đơn hàng xuất hiện sau bước kiểm tra. Khóa ngoại là lớp bảo vệ thứ hai và trả cùng lỗi.
            throw ProductHasOrders();
        }
    }

    // Khóa dòng sản phẩm đến hết transaction. Sổ tồn kho cũng khóa `products` trước `skus`, nên hai bên không chờ nhau theo vòng.
    private async Task LockProductRowAsync(string productId, CancellationToken cancellationToken)
    {
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM products WHERE id = {productId} FOR UPDATE", cancellationToken);
    }

    private async Task EnsurePublishableAsync(Product product, CancellationToken cancellationToken)
    {
        var category = await database.Categories.AsNoTracking()
            .SingleAsync(item => item.Id == product.CategoryId, cancellationToken);
        // Số ảnh lấy từ `product_images`, không tin số ảnh do client gửi.
        var input = new ProductPublishableInput(
            JsonNode.Parse(product.Specs.RootElement.GetRawText()),
            await ResolveAttributesAsync(category, cancellationToken),
            product.Images.Count,
            product.Skus.Select(sku => new SkuPublishableInput(sku.Code, sku.Enabled, sku.Price, sku.CompareAtPrice,
                sku.WeightGrams, sku.LengthCm, sku.WidthCm, sku.HeightCm)).ToList());
        var check = ProductPublishing.CheckPublishable(input);
        if (!check.Publishable)
        {
            throw new ProductNotPublishableException(check.Reasons);
        }
    }

    private static string ToStatusName(ProductStatus status) => status.ToString().ToLowerInvariant();

    private async Task<ProductResponse> RunWriteAsync(
        Func<DomainEventCollector, Task<ProductResponse>> action, CancellationToken cancellationToken)
    {
        try
        {
            // Helper chỉ phát hàng đợi sau commit, nên lỗi audit không phát dữ liệu đã bị hoàn nguyên.
            return await CatalogTransaction.RunAsync(database, dispatcher, async events =>
            {
                // Khóa trước khi đọc để request chờ thấy SKU mới và slug đã cấp của request trước.
                await database.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock({ProductWriteLockKey})", cancellationToken);
                return await action(events);
            }, cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "skus_code_key" })
        {
            // Ràng buộc DB chặn mã trùng do hai request đồng thời, kể cả sau kiểm tra nghiệp vụ.
            throw DuplicateSkuCode();
        }
    }

    private async Task SaveAsync(Actor actor, Product product, CreateProductRequest target, bool creating,
        DomainEventCollector events, CancellationToken cancellationToken)
    {
        // Kiểm tra toàn bộ dữ liệu đích trước khi thay đổi entity để lỗi không lưu một phần.
        var options = target.Options ?? [];
        var combinations = VariantCombinations.Build(options);
        await ValidateContentAsync(target, cancellationToken);
        var variants = target.Variants!;
        ValidateVariants(combinations, variants);

        // Tên giữ nguyên thì slug giữ nguyên, kể cả slug trước đây có hậu tố do trùng tên.
        var name = target.Name.Trim();
        var slug = !creating && name == product.Name
            ? product.Slug
            : await GenerateSlugAsync(name, creating ? null : product.Id, cancellationToken);
        // Chuẩn bị mã trước khi ghi để mã admin nhập trùng không tạo sản phẩm hoặc SKU dở dang.
        var plans = await PlanVariantsAsync(product, variants, slug, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (!creating && slug != product.Slug)
        {
            // Xóa lịch sử trùng slug vừa nhận lại để link cũ không chuyển hướng tới chính nó.
            var reclaimed = await database.ProductSlugHistories.SingleOrDefaultAsync(
                history => history.ProductId == product.Id && history.OldSlug == slug, cancellationToken);
            if (reclaimed is not null)
            {
                database.ProductSlugHistories.Remove(reclaimed);
            }

            // Giữ đường dẫn trước đó trong cùng transaction để các link đã chia sẻ vẫn có đích.
            database.ProductSlugHistories.Add(new ProductSlugHistory
            {
                Id = Guid.NewGuid().ToString("N"), ProductId = product.Id,
                OldSlug = product.Slug, CreatedAt = now,
            });
        }

        // Chỉ CAT-07 đổi trạng thái. CAT-05 không gán lại trạng thái khi sửa sản phẩm.
        if (creating)
        {
            product.Status = ProductStatus.Draft;
            database.Products.Add(product);
        }

        // Lưu JSON theo cấu trúc đã kiểm tra, không nhận HTML hay thông số ngoài bộ thuộc tính.
        product.CategoryId = target.CategoryId;
        product.BrandId = target.BrandId;
        product.Name = name;
        product.Slug = slug;
        product.ShortDesc = target.ShortDesc.Trim();
        product.Description = JsonDocument.Parse(target.Description!.Value.GetRawText());
        product.Specs = JsonDocument.Parse(target.Specs!.Value.GetRawText());
        product.Options = JsonSerializer.SerializeToDocument(options.Select(option =>
            new ProductOption(option.Name.Trim(), option.Values.Select(value => value.Trim()).ToList())), PickoryJsonDefaults.Options);
        product.WarrantyMonths = target.WarrantyMonths ?? 12;
        product.UpdatedAt = now;

        // Tổ hợp bị bỏ chỉ tắt SKU để giữ tồn kho, ảnh và lịch sử biến động.
        var retainedIds = plans.Select(plan => plan.Sku.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var sku in product.Skus)
        {
            if (!retainedIds.Contains(sku.Id))
            {
                sku.Enabled = false;
                sku.UpdatedAt = now;
            }
        }

        var priceChanges = new List<AuditLogEntry>();
        foreach (var plan in plans)
        {
            var sku = plan.Sku;
            var variant = plan.Variant;
            // Chỉ giá bán đổi mới ghi nhật ký. Tạo SKU và đổi riêng giá niêm yết không ghi dòng này.
            if (!plan.IsNew && sku.Price != variant.Price)
            {
                priceChanges.Add(new AuditLogEntry(actor.UserId!, "catalog.sku.price_change", "sku", sku.Id,
                    new { price = sku.Price }, new { price = (int)variant.Price }));
            }

            // SKU mới dùng tồn kho mặc định của DB. CAT-05 không ghi cột `stock`.
            if (plan.IsNew)
            {
                product.Skus.Add(sku);
                // Đánh dấu thêm rõ ràng vì SKU mới đã có định danh, tránh Entity Framework coi là bản ghi cần cập nhật.
                database.Skus.Add(sku);
            }

            // Không thay định danh hoặc tồn kho của SKU cũ khi cập nhật giá và kích thước.
            sku.Code = plan.Code;
            sku.OptionValues = JsonSerializer.SerializeToDocument(variant.OptionValues, PickoryJsonDefaults.Options);
            sku.Price = (int)variant.Price;
            sku.CompareAtPrice = (int?)variant.CompareAtPrice;
            sku.Enabled = variant.Enabled;
            sku.WeightGrams = variant.WeightGrams;
            sku.LengthCm = variant.LengthCm;
            sku.WidthCm = variant.WidthCm;
            sku.HeightCm = variant.HeightCm;
            sku.UpdatedAt = now;
        }

        // Đồng bộ ảnh sau khi SKU đã có định danh, để ảnh gắn biến thể kiểm được SKU thuộc sản phẩm này.
        if (target.Images is not null)
        {
            imageSync.Apply(product, target.Images, now);
        }

        // Tính giá và khả năng còn hàng từ toàn bộ SKU sau khi đã tắt các tổ hợp bị bỏ.
        var display = ProductPricing.ComputeDisplayPrice(product.Skus.Select(sku =>
            new SkuPricingInput(sku.Price, sku.Stock, sku.Enabled)).ToList());
        product.MinPrice = display.MinPrice;
        product.InStock = display.InStock;
        // Lưu sản phẩm, lịch sử slug và SKU cùng transaction trước khi ghi audit.
        await database.SaveChangesAsync(cancellationToken);
        foreach (var entry in priceChanges)
        {
            // Audit dùng đúng kết nối transaction để lỗi ghi nhật ký hoàn nguyên cả thay đổi giá.
            await auditLogWriter.WriteAsync(entry, database.Database.CurrentTransaction!.GetDbTransaction(), cancellationToken);
        }

        // Gom đúng một sự kiện cho thao tác, không phát riêng sự kiện cho mỗi SKU.
        events.Emit(new ProductChangedEvent(product.Id, creating ? ProductChange.Created : ProductChange.Updated));
    }

    private async Task ValidateContentAsync(CreateProductRequest target, CancellationToken cancellationToken)
    {
        // Danh mục thiếu nhận lỗi đầu vào thay vì để khóa ngoại DB trả lỗi nội bộ.
        var category = await database.Categories.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == target.CategoryId, cancellationToken);
        if (category is null)
        {
            throw new AppException("INVALID_CATEGORY", 400, "Danh mục không tồn tại.");
        }

        // Chỉ danh mục không có con mới chứa sản phẩm trực tiếp.
        if (await database.Categories.AnyAsync(item => item.ParentId == category.Id, cancellationToken))
        {
            throw new AppException("CATEGORY_NOT_LEAF", 400, "Sản phẩm phải thuộc danh mục lá.");
        }

        // Thương hiệu phải tồn tại trước khi ghi sản phẩm để không có tham chiếu thiếu.
        if (!await database.Brands.AnyAsync(item => item.Id == target.BrandId, cancellationToken))
        {
            throw new AppException("INVALID_BRAND", 400, "Thương hiệu không tồn tại.");
        }

        var attributes = await ResolveAttributesAsync(category, cancellationToken);

        // Trả từng khóa sai để giao diện gắn lỗi vào đúng ô thông số.
        var specs = SpecValidator.Validate(ToNode(target.Specs), attributes);
        if (!specs.Valid)
        {
            throw new ProductSpecsException(specs.Errors);
        }

        // Schema whitelist chặn nút lạ và giới hạn tài liệu trước mọi thay đổi dữ liệu.
        var description = DescriptionSchema.Validate(ToNode(target.Description));
        if (!description.Valid)
        {
            throw new AppException("DESCRIPTION_INVALID", 400, string.Join(" ", description.Errors));
        }
    }

    private async Task<IReadOnlyList<AttributeDefinition>> ResolveAttributesAsync(
        Category category, CancellationToken cancellationToken)
    {
        // Danh mục con kế thừa bộ thuộc tính cha, nên kiểm tra cả hai bộ.
        var attributes = ReadAttributes(category.Attributes);
        if (category.ParentId is null)
        {
            return attributes;
        }

        var parent = await database.Categories.AsNoTracking()
            .SingleAsync(item => item.Id == category.ParentId, cancellationToken);
        return CategoryAttributes.Resolve(ReadAttributes(parent.Attributes), attributes);
    }

    private static void ValidateVariants(IReadOnlyList<IReadOnlyDictionary<string, string>> combinations,
        IReadOnlyList<ProductVariantRequest> variants)
    {
        // Tập tổ hợp phải khớp tuyệt đối, không cho phép thiếu, thừa hoặc lặp SKU trong request.
        var remaining = combinations.Select(CombinationKey).ToHashSet(StringComparer.Ordinal);
        if (variants.Count != combinations.Count)
        {
            throw InvalidVariants();
        }

        foreach (var variant in variants)
        {
            if (variant.OptionValues is null || !remaining.Remove(CombinationKey(variant.OptionValues)))
            {
                throw InvalidVariants();
            }

            // Helper chung kiểm tra giá nguyên dương và giá niêm yết cao hơn giá bán.
            var price = ProductPricing.ValidatePrice(variant.Price, variant.CompareAtPrice);
            if (!price.Valid || variant.Price > int.MaxValue || variant.CompareAtPrice > int.MaxValue)
            {
                throw new AppException("INVALID_PRODUCT_PRICE", 400, price.Error ?? "Giá vượt giới hạn lưu trữ.");
            }
        }
    }

    private async Task<List<VariantPlan>> PlanVariantsAsync(Product product,
        IReadOnlyList<ProductVariantRequest> variants, string slug, CancellationToken cancellationToken)
    {
        // Ghép cả SKU đã tắt theo tổ hợp để thêm lại giá trị nhận đúng định danh cũ.
        var existing = product.Skus.ToDictionary(sku => CombinationKey(ReadOptionValues(sku.OptionValues)), StringComparer.Ordinal);
        var plans = new List<VariantPlan>(variants.Count);
        var reservedCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in variants)
        {
            var isNew = !existing.TryGetValue(CombinationKey(variant.OptionValues!), out var sku);
            sku ??= new Sku
            {
                Id = Guid.NewGuid().ToString("N"), ProductId = product.Id,
                CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            };
            // Mã trống trên SKU cũ giữ mã trước đó, kể cả khi tên sản phẩm đổi.
            var code = string.IsNullOrWhiteSpace(variant.Code) ? (isNew ? null : sku.Code) : variant.Code.Trim();
            if (code is not null)
            {
                // Giữ chỗ mã nhập trước mã tự sinh để kết quả không phụ thuộc thứ tự biến thể.
                if (!reservedCodes.Add(code)
                    || await database.Skus.AnyAsync(item => item.Code == code && item.Id != sku.Id, cancellationToken))
                {
                    throw DuplicateSkuCode();
                }
            }

            plans.Add(new VariantPlan(sku, variant, code!, isNew));
        }

        for (var index = 0; index < plans.Count; index++)
        {
            var plan = plans[index];
            if (plan.Code is not null)
            {
                continue;
            }

            // Slug và giá trị tùy chọn cho mã đọc được, viết hoa và không có dấu tiếng Việt.
            var values = plan.Variant.OptionValues!.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value);
            var baseCode = SlugGenerator.ToSlug(string.Join("-", new[] { slug }.Concat(values))).ToUpperInvariant();
            var code = baseCode;
            var suffix = 2;
            // Mã tự sinh có thể trùng sau khi bỏ dấu, nên dùng hậu tố thay vì bỏ biến thể.
            while (reservedCodes.Contains(code) || await database.Skus.AnyAsync(item => item.Code == code, cancellationToken))
            {
                code = $"{baseCode}-{suffix++}";
            }

            reservedCodes.Add(code);
            plans[index] = plan with { Code = code };
        }

        return plans;
    }

    private async Task<string> GenerateSlugAsync(string name, string? productId, CancellationToken cancellationToken)
    {
        // Tên chỉ có ký hiệu vẫn cần URL ổn định để quản trị và chia sẻ sản phẩm.
        var baseSlug = SlugGenerator.ToSlug(name);
        if (baseSlug.Length == 0)
        {
            baseSlug = "san-pham";
        }

        var slug = baseSlug;
        var suffix = 2;
        // Không chiếm đường dẫn hiện tại hoặc lịch sử của sản phẩm khác.
        while (await database.Products.AnyAsync(item => item.Slug == slug && item.Id != productId, cancellationToken)
            || await database.ProductSlugHistories.AnyAsync(item => item.OldSlug == slug && item.ProductId != productId, cancellationToken))
        {
            slug = $"{baseSlug}-{suffix++}";
        }

        return slug;
    }

    private static string CombinationKey(IReadOnlyDictionary<string, string> values)
    {
        // JSON theo thứ tự khóa tránh ghép nhầm khi tên hoặc giá trị chứa ký tự phân cách.
        return JsonSerializer.Serialize(values.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    private static JsonNode? ToNode(JsonElement? value)
    {
        // Schema chung nhận cây JSON. Null giữ nguyên để schema trả lỗi có ý nghĩa nghiệp vụ.
        return value is null ? null : JsonNode.Parse(value.Value.GetRawText());
    }

    private static IReadOnlyList<AttributeDefinition> ReadAttributes(JsonDocument document)
    {
        // Dùng cùng cấu hình thuộc tính của CAT-04 để giữ kiểu enum và tên trường nhất quán.
        return document.Deserialize<List<AttributeDefinition>>(AttributeDefinition.JsonOptions)!;
    }

    private static IReadOnlyList<ProductOption> ReadOptions(JsonDocument document)
    {
        // JSON trong DB dùng camelCase như request, nên đọc bằng cùng cấu hình API.
        return document.Deserialize<List<ProductOption>>(PickoryJsonDefaults.Options)!;
    }

    private static IReadOnlyDictionary<string, string> ReadOptionValues(JsonDocument document)
    {
        // Đọc lại khóa tổ hợp đã lưu để ghép SKU, không dùng mã SKU có thể do admin đổi.
        return document.Deserialize<Dictionary<string, string>>()!;
    }

    private static ProductVariantRequest ToVariantRequest(Sku sku)
    {
        // Khi PATCH không gửi biến thể, giữ nguyên cả giá niêm yết và kích thước đóng gói.
        return new ProductVariantRequest
        {
            OptionValues = ReadOptionValues(sku.OptionValues), Code = sku.Code,
            Price = sku.Price, CompareAtPrice = sku.CompareAtPrice, Enabled = sku.Enabled,
            WeightGrams = sku.WeightGrams, LengthCm = sku.LengthCm, WidthCm = sku.WidthCm, HeightCm = sku.HeightCm,
        };
    }

    private ProductResponse ToResponse(Product product)
    {
        // Sao chép phần tử JSON để phản hồi không phụ thuộc vòng đời tài liệu của entity.
        return new ProductResponse(product.Id, product.CategoryId, product.BrandId, product.Name, product.Slug,
            product.ShortDesc, product.Description.RootElement.Clone(), product.Specs.RootElement.Clone(), ReadOptions(product.Options),
            product.WarrantyMonths, ToStatusName(product.Status), product.MinPrice, product.InStock,
            product.Skus.OrderBy(sku => sku.CreatedAt).ThenBy(sku => sku.Id).Select(sku =>
                new ProductVariantResponse(sku.Id, sku.Code, ReadOptionValues(sku.OptionValues), sku.Price,
                    sku.CompareAtPrice, sku.Stock, sku.Enabled, sku.WeightGrams, sku.LengthCm, sku.WidthCm, sku.HeightCm,
                    sku.CreatedAt, sku.UpdatedAt)).ToList(),
            product.Images.OrderBy(image => image.Position).ThenBy(image => image.Id).Select(image =>
                new ProductImageResponse(image.Id, image.PublicId, image.Alt, image.SkuId, image.Position,
                    $"https://res.cloudinary.com/{cloudinary.CloudName}/image/upload/{image.PublicId}")).ToList(),
            product.CreatedAt, product.UpdatedAt);
    }

    private static AppException ProductNotFound() => new("PRODUCT_NOT_FOUND", 404, "Không tìm thấy sản phẩm.");
    private static AppException ProductHasOrders() => new("PRODUCT_HAS_ORDERS", 409, "Sản phẩm đã có đơn hàng nên không xóa được.");
    private static AppException DuplicateSkuCode() => new("SKU_CODE_ALREADY_EXISTS", 409, "Mã SKU đã tồn tại.");
    private static AppException InvalidVariants() => new("INVALID_PRODUCT_VARIANTS", 400, "Danh sách biến thể không khớp tổ hợp tùy chọn.");
    private sealed record VariantPlan(Sku Sku, ProductVariantRequest Variant, string Code, bool IsNew);
}
