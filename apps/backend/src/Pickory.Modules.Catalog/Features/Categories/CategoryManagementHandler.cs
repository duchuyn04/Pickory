using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Catalog.Products;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Catalog.Features.Categories;

// Quản lý thông tin và cây phân cấp danh mục sản phẩm của hệ thống.
public sealed class CategoryManagementHandler(
    CatalogDbContext database,
    IAuditLogWriter auditLogWriter,
    TimeProvider timeProvider)
{
    // Đọc toàn bộ danh mục và dựng cây phân cấp hai cấp kèm thuộc tính lọc.
    public async Task<IReadOnlyList<CategoryTreeResponse>> GetTreeAsync(CancellationToken cancellationToken)
    {
        // Tải danh mục sắp xếp theo vị trí hiển thị để trả về cho người dùng.
        var allCategories = await database.Categories
            .AsNoTracking()
            .OrderBy(category => category.Position)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken);

        var roots = allCategories.Where(category => category.ParentId == null).ToList();
        var childrenByParent = allCategories
            .Where(category => category.ParentId != null)
            .GroupBy(category => category.ParentId!)
            .ToDictionary(group => group.Key, group => group.ToList());

        var result = new List<CategoryTreeResponse>(roots.Count);
        foreach (var root in roots)
        {
            var rootAttributes = DeserializeAttributes(root.Attributes);
            var filterableRootAttributes = rootAttributes.Where(attribute => attribute.Filterable).ToList();

            var childNodes = new List<CategoryTreeResponse>();
            if (childrenByParent.TryGetValue(root.Id, out var children))
            {
                foreach (var child in children)
                {
                    var childAttributes = DeserializeAttributes(child.Attributes);
                    // Kế thừa thuộc tính lọc từ danh mục cha sang danh mục con.
                    var resolvedAttributes = CategoryAttributes.Resolve(rootAttributes, childAttributes);
                    var filterableChildAttributes = resolvedAttributes.Where(attribute => attribute.Filterable).ToList();

                    childNodes.Add(new CategoryTreeResponse(
                        child.Id,
                        child.ParentId,
                        child.Name,
                        child.Slug,
                        child.Position,
                        filterableChildAttributes,
                        []));
                }
            }

            result.Add(new CategoryTreeResponse(
                root.Id,
                root.ParentId,
                root.Name,
                root.Slug,
                root.Position,
                filterableRootAttributes,
                childNodes));
        }

        return result;
    }

    // Trả danh sách phẳng toàn bộ danh mục kèm thuộc tính riêng để trang quản trị biên tập.
    public async Task<IReadOnlyList<CategoryResponse>> GetListAsync(CancellationToken cancellationToken)
    {
        // Danh mục sắp theo vị trí hiển thị và tên, không lọc hay gộp thuộc tính với cha.
        var categories = await database.Categories
            .AsNoTracking()
            .OrderBy(category => category.Position)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken);

        return categories
            .Select(category => ToResponse(category, DeserializeAttributes(category.Attributes)))
            .ToList();
    }

    // Tạo mới một danh mục sản phẩm và lưu vết nhật ký quản trị.
    public async Task<CategoryResponse> CreateAsync(
        Actor actor,
        CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var attributes = request.Attributes ?? [];
        // Kiểm tra tính hợp lệ của từng định nghĩa thuộc tính theo luật của hệ thống.
        var validation = AttributeDefinitionRules.ValidateAll(attributes);
        if (!validation.Valid)
        {
            var details = string.Join(' ', validation.Errors.Select(error => error.Message));
            throw new AppException("INVALID_CATEGORY_ATTRIBUTES", 400, $"Định nghĩa thuộc tính không hợp lệ: {details}");
        }

        Category? parent = null;
        if (!string.IsNullOrWhiteSpace(request.ParentId))
        {
            parent = await database.Categories.SingleOrDefaultAsync(
                category => category.Id == request.ParentId,
                cancellationToken);
            if (parent is null)
            {
                // Báo lỗi không tìm thấy khi mã danh mục cha không tồn tại trong cơ sở dữ liệu.
                throw new AppException("CATEGORY_NOT_FOUND", 404, "Không tìm thấy danh mục cha.");
            }

            // Chặn tạo danh mục cấp ba vì hệ thống chỉ hỗ trợ tối đa hai cấp.
            if (parent.ParentId != null)
            {
                throw new AppException("CATEGORY_LEVEL_EXCEEDED", 400, "Cây danh mục chỉ hỗ trợ tối đa 2 cấp.");
            }

            // Chặn tạo danh mục con dưới danh mục đang có sản phẩm vì sản phẩm chỉ ở danh mục lá.
            var parentHasProducts = await database.Products.AnyAsync(
                product => product.CategoryId == parent.Id,
                cancellationToken);
            if (parentHasProducts)
            {
                throw new AppException("CATEGORY_HAS_PRODUCTS", 400, "Không thể tạo danh mục con dưới danh mục đang có sản phẩm.");
            }

            var parentAttributes = DeserializeAttributes(parent.Attributes);
            // Kiểm tra không trùng khóa thuộc tính giữa danh mục cha và con.
            CategoryAttributes.Resolve(parentAttributes, attributes);
        }

        var slug = await GenerateUniqueSlugAsync(request.Name, excludeId: null, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var newCategory = new Category
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = request.Name.Trim(),
            Slug = slug,
            ParentId = parent?.Id,
            Position = request.Position ?? 0,
            Attributes = SerializeAttributes(attributes),
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        database.Categories.Add(newCategory);
        await database.SaveChangesAsync(cancellationToken);

        // Ghi nhật ký quản trị trong cùng giao dịch với lệnh tạo danh mục.
        var auditEntry = new AuditLogEntry(
            actor.UserId!,
            "catalog.category.create",
            "category",
            newCategory.Id,
            null,
            ToAuditSnapshot(newCategory));
        await auditLogWriter.WriteAsync(auditEntry, transaction.GetDbTransaction(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(newCategory, attributes);
    }

    // Cập nhật thông tin danh mục, kiểm tra an toàn dữ liệu sản phẩm hiện có.
    public async Task<CategoryResponse> UpdateAsync(
        Actor actor,
        string id,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await database.Categories
            .Include(item => item.Children)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (category is null)
        {
            // Báo lỗi không tìm thấy khi mã danh mục không tồn tại trong cơ sở dữ liệu.
            throw new AppException("CATEGORY_NOT_FOUND", 404, "Không tìm thấy danh mục.");
        }

        var beforeSnapshot = ToAuditSnapshot(category);
        var oldAttributes = DeserializeAttributes(category.Attributes);
        var targetAttributes = request.Attributes ?? oldAttributes;

        if (request.Attributes is not null)
        {
            // Kiểm tra định nghĩa các thuộc tính mới gửi lên từ yêu cầu.
            var validation = AttributeDefinitionRules.ValidateAll(targetAttributes);
            if (!validation.Valid)
            {
                var details = string.Join(' ', validation.Errors.Select(error => error.Message));
                throw new AppException("INVALID_CATEGORY_ATTRIBUTES", 400, $"Định nghĩa thuộc tính không hợp lệ: {details}");
            }
        }

        if (request.ParentIdSpecified)
        {
            // Chặn gán danh mục cha là chính nó để tránh tạo vòng lặp vô tận.
            if (request.ParentId == category.Id)
            {
                throw new AppException("INVALID_PARENT_CATEGORY", 400, "Danh mục không thể làm cha của chính mình.");
            }

            // Chặn chuyển danh mục đang có con thành con của danh mục khác vì sẽ thành cấp ba.
            if (category.Children.Count > 0 && request.ParentId != null)
            {
                throw new AppException("CATEGORY_LEVEL_EXCEEDED", 400, "Không thể chuyển danh mục có danh mục con thành danh mục cấp 2.");
            }

            // Chặn chuyển danh mục đang có sản phẩm sang danh mục cha khác để bảo vệ thông số cũ.
            var currentHasProducts = await database.Products.AnyAsync(
                product => product.CategoryId == category.Id,
                cancellationToken);
            if (currentHasProducts && request.ParentId != category.ParentId)
            {
                throw new AppException("CATEGORY_HAS_PRODUCTS", 400, "Không thể chuyển danh mục đang có sản phẩm sang danh mục cha khác.");
            }

            if (request.ParentId != null)
            {
                var parent = await database.Categories.SingleOrDefaultAsync(
                    item => item.Id == request.ParentId,
                    cancellationToken);
                if (parent is null)
                {
                    // Báo lỗi không tìm thấy khi mã danh mục cha không tồn tại trong cơ sở dữ liệu.
                    throw new AppException("CATEGORY_NOT_FOUND", 404, "Không tìm thấy danh mục cha.");
                }

                // Chặn chọn cha là danh mục cấp hai vì sẽ vi phạm giới hạn phân cấp.
                if (parent.ParentId != null)
                {
                    throw new AppException("CATEGORY_LEVEL_EXCEEDED", 400, "Cây danh mục chỉ hỗ trợ tối đa 2 cấp.");
                }

                // Chặn gán cha có sản phẩm vì sản phẩm chỉ ở danh mục lá.
                var targetParentHasProducts = await database.Products.AnyAsync(
                    product => product.CategoryId == parent.Id,
                    cancellationToken);
                if (targetParentHasProducts)
                {
                    throw new AppException("CATEGORY_HAS_PRODUCTS", 400, "Không thể chuyển danh mục làm con của danh mục đang có sản phẩm.");
                }

                category.ParentId = parent.Id;
            }
            else
            {
                category.ParentId = null;
            }
        }

        // Kiểm tra trùng khóa với cha khi đổi cha hoặc đổi thuộc tính.
        var effectiveParentId = category.ParentId;
        if (effectiveParentId != null && (request.ParentIdSpecified || request.Attributes is not null))
        {
            var parent = await database.Categories
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == effectiveParentId, cancellationToken);
            if (parent is not null)
            {
                var parentAttributes = DeserializeAttributes(parent.Attributes);
                // Kiểm tra xung đột khóa thuộc tính giữa danh mục cha và thuộc tính của con.
                CategoryAttributes.Resolve(parentAttributes, targetAttributes);
            }
        }

        // Kiểm tra khóa thuộc tính mới không xung đột với các danh mục con hiện có.
        if (category.Children.Count > 0 && request.Attributes is not null)
        {
            foreach (var child in category.Children)
            {
                var childAttributes = DeserializeAttributes(child.Attributes);
                CategoryAttributes.Resolve(targetAttributes, childAttributes);
            }
        }

        // Bảo vệ toàn vẹn dữ liệu thông số của sản phẩm khi sửa thuộc tính danh mục.
        if (request.Attributes is not null)
        {
            await ValidateAttributeModificationsAsync(category, oldAttributes, targetAttributes, cancellationToken);
            category.Attributes = SerializeAttributes(targetAttributes);
        }

        if (!string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim() != category.Name)
        {
            category.Name = request.Name.Trim();
            // Giữ nguyên slug ban đầu để tránh làm hỏng các liên kết chia sẻ đã có.
        }

        if (request.Position.HasValue)
        {
            category.Position = request.Position.Value;
        }

        category.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.SaveChangesAsync(cancellationToken);

        // Ghi nhật ký cập nhật danh mục trong cùng giao dịch cơ sở dữ liệu.
        var auditEntry = new AuditLogEntry(
            actor.UserId!,
            "catalog.category.update",
            "category",
            category.Id,
            beforeSnapshot,
            ToAuditSnapshot(category));
        await auditLogWriter.WriteAsync(auditEntry, transaction.GetDbTransaction(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(category, targetAttributes);
    }

    // Xóa một danh mục khi danh mục đó không còn con và không còn sản phẩm.
    public async Task DeleteAsync(Actor actor, string id, CancellationToken cancellationToken)
    {
        var category = await database.Categories.SingleOrDefaultAsync(
            item => item.Id == id,
            cancellationToken);
        if (category is null)
        {
            // Báo lỗi không tìm thấy khi mã danh mục cần xóa không tồn tại trong hệ thống.
            throw new AppException("CATEGORY_NOT_FOUND", 404, "Không tìm thấy danh mục.");
        }

        // Chặn xóa nếu còn danh mục con để tránh mồ côi liên kết cây.
        var hasChildren = await database.Categories.AnyAsync(
            item => item.ParentId == id,
            cancellationToken);
        if (hasChildren)
        {
            throw new AppException("CATEGORY_HAS_CHILDREN", 400, "Không thể xóa danh mục đang có danh mục con.");
        }

        // Chặn xóa nếu còn sản phẩm gắn với danh mục này để bảo vệ dữ liệu.
        var hasProducts = await database.Products.AnyAsync(
            item => item.CategoryId == id,
            cancellationToken);
        if (hasProducts)
        {
            throw new AppException("CATEGORY_HAS_PRODUCTS", 400, "Không thể xóa danh mục đang có sản phẩm.");
        }

        var beforeSnapshot = ToAuditSnapshot(category);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        database.Categories.Remove(category);
        await database.SaveChangesAsync(cancellationToken);

        // Ghi nhật ký thao tác xóa danh mục trong cùng giao dịch cơ sở dữ liệu.
        var auditEntry = new AuditLogEntry(
            actor.UserId!,
            "catalog.category.delete",
            "category",
            category.Id,
            beforeSnapshot,
            null);
        await auditLogWriter.WriteAsync(auditEntry, transaction.GetDbTransaction(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ValidateAttributeModificationsAsync(
        Category category,
        IReadOnlyList<AttributeDefinition> oldAttributes,
        IReadOnlyList<AttributeDefinition> newAttributes,
        CancellationToken cancellationToken)
    {
        var categoryIds = new List<string> { category.Id };
        var childIds = await database.Categories
            .Where(item => item.ParentId == category.Id)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        categoryIds.AddRange(childIds);

        // Lấy danh sách thông số của sản phẩm để kiểm tra việc sử dụng thuộc tính.
        var products = await database.Products
            .Where(item => categoryIds.Contains(item.CategoryId))
            .Select(item => new { item.Id, item.Specs })
            .ToListAsync(cancellationToken);

        // Bỏ qua kiểm tra ràng buộc thuộc tính khi danh mục chưa có sản phẩm nào.
        if (products.Count == 0)
        {
            return;
        }

        var oldMap = oldAttributes.ToDictionary(attribute => attribute.Key, attribute => attribute, StringComparer.Ordinal);
        var newMap = newAttributes.ToDictionary(attribute => attribute.Key, attribute => attribute, StringComparer.Ordinal);

        // Chặn thêm thuộc tính bắt buộc mới khi đã có sản phẩm trong danh mục.
        foreach (var newAttribute in newAttributes)
        {
            if (!oldMap.ContainsKey(newAttribute.Key) && newAttribute.Required)
            {
                throw new AppException(
                    "INVALID_CATEGORY_ATTRIBUTES",
                    400,
                    $"Không thể thêm thuộc tính bắt buộc \"{newAttribute.Key}\" khi danh mục đã có sản phẩm.");
            }
        }

        // Kiểm tra các thuộc tính cũ bị xóa hoặc đổi ràng buộc.
        foreach (var oldAttribute in oldAttributes)
        {
            if (!newMap.TryGetValue(oldAttribute.Key, out var newAttribute))
            {
                var isInUse = products.Any(product => HasSpecKey(product.Specs, oldAttribute.Key));
                if (isInUse)
                {
                    // Thuộc tính đang có sản phẩm sử dụng thì không được phép xóa.
                    throw new AppException(
                        "ATTRIBUTE_IN_USE",
                        409,
                        $"Thuộc tính \"{oldAttribute.Key}\" đang được sản phẩm sử dụng, không thể xóa.");
                }
            }
            else
            {
                // Chặn đổi thuộc tính từ tùy chọn sang bắt buộc khi danh mục đã có sản phẩm.
                if (!oldAttribute.Required && newAttribute.Required)
                {
                    throw new AppException(
                        "INVALID_CATEGORY_ATTRIBUTES",
                        400,
                        $"Không thể đổi thuộc tính \"{oldAttribute.Key}\" thành bắt buộc khi danh mục đã có sản phẩm.");
                }

                // Thuộc tính đang có sản phẩm sử dụng thì không được phép đổi kiểu dữ liệu.
                if (!string.Equals(oldAttribute.Type, newAttribute.Type, StringComparison.OrdinalIgnoreCase))
                {
                    var isInUse = products.Any(product => HasSpecKey(product.Specs, oldAttribute.Key));
                    if (isInUse)
                    {
                        throw new AppException(
                            "ATTRIBUTE_IN_USE",
                            409,
                            $"Thuộc tính \"{oldAttribute.Key}\" đang được sản phẩm sử dụng, không thể đổi kiểu.");
                    }
                }

                // Giá trị tùy chọn đang có sản phẩm dùng thì không được phép xóa khỏi danh mục.
                if (string.Equals(oldAttribute.Type, "enum", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(newAttribute.Type, "enum", StringComparison.OrdinalIgnoreCase)
                    && oldAttribute.Options is not null
                    && newAttribute.Options is not null)
                {
                    var newOptions = newAttribute.Options.ToHashSet(StringComparer.Ordinal);
                    var removedOptions = oldAttribute.Options.Where(option => !newOptions.Contains(option)).ToList();
                    if (removedOptions.Count > 0)
                    {
                        var isOptionInUse = products.Any(product =>
                            HasSpecValueAny(product.Specs, oldAttribute.Key, removedOptions));
                        if (isOptionInUse)
                        {
                            throw new AppException(
                                "ATTRIBUTE_IN_USE",
                                409,
                                $"Giá trị tùy chọn của thuộc tính \"{oldAttribute.Key}\" đang được sản phẩm sử dụng, không thể xóa.");
                        }
                    }
                }
            }
        }
    }

    private static bool HasSpecKey(JsonDocument specs, string key)
    {
        if (specs.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (specs.RootElement.TryGetProperty(key, out var property))
        {
            // Bỏ qua thuộc tính mang giá trị null vì sản phẩm chưa thực sự lưu dữ liệu cho khóa này.
            return property.ValueKind != JsonValueKind.Null && property.ValueKind != JsonValueKind.Undefined;
        }

        return false;
    }

    private static bool HasSpecValueAny(JsonDocument specs, string key, IReadOnlyList<string> values)
    {
        if (specs.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!specs.RootElement.TryGetProperty(key, out var property))
        {
            return false;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString();
        return text is not null && values.Contains(text, StringComparer.Ordinal);
    }

    private async Task<string> GenerateUniqueSlugAsync(string name, string? excludeId, CancellationToken cancellationToken)
    {
        var baseSlug = SlugGenerator.ToSlug(name);
        if (string.IsNullOrWhiteSpace(baseSlug))
        {
            // Dùng tên thay thế khi chuỗi chỉ gồm ký tự đặc biệt tạo ra slug rỗng.
            baseSlug = "danh-muc";
        }

        var slug = baseSlug;
        var counter = 2;
        // Lặp kiểm tra để đảm bảo slug không bị trùng với danh mục khác.
        while (await database.Categories.AnyAsync(
            item => item.Slug == slug && item.Id != excludeId,
            cancellationToken))
        {
            slug = $"{baseSlug}-{counter}";
            counter++;
        }

        return slug;
    }

    private static IReadOnlyList<AttributeDefinition> DeserializeAttributes(JsonDocument document)
    {
        return JsonSerializer.Deserialize<List<AttributeDefinition>>(
            document.RootElement.GetRawText(),
            AttributeDefinition.JsonOptions) ?? [];
    }

    private static JsonDocument SerializeAttributes(IReadOnlyList<AttributeDefinition> attributes)
    {
        return JsonDocument.Parse(JsonSerializer.Serialize(attributes, AttributeDefinition.JsonOptions));
    }

    private static CategoryResponse ToResponse(Category category, IReadOnlyList<AttributeDefinition> attributes)
    {
        return new CategoryResponse(
            category.Id,
            category.ParentId,
            category.Name,
            category.Slug,
            category.Position,
            attributes,
            category.CreatedAt,
            category.UpdatedAt);
    }

    private static object ToAuditSnapshot(Category category)
    {
        return new
        {
            category.Id,
            category.ParentId,
            category.Name,
            category.Slug,
            category.Position,
            category.CreatedAt,
            category.UpdatedAt,
        };
    }
}
