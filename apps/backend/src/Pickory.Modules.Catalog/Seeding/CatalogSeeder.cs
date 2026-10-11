using System.Data.Common;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pickory.Modules.Catalog.Descriptions;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Features.Brands;
using Pickory.Modules.Catalog.Features.Categories;
using Pickory.Modules.Catalog.Features.Products;
using Pickory.Modules.Catalog.Images;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Catalog.Products;
using Pickory.Modules.Catalog.Stock;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Catalog.Seeding;

// Số bản ghi mỗi loại mà một lần chạy tạo mới hoặc bỏ qua. Lần chạy thứ hai phải toàn là bỏ qua.
public sealed class SeedReport
{
    private readonly Dictionary<string, (int Created, int Skipped)> _counts = new(StringComparer.Ordinal);

    public void Created(string kind, int amount = 1) => Add(kind, amount, 0);

    public void Skipped(string kind, int amount = 1) => Add(kind, 0, amount);

    public int CreatedCount(string kind) => _counts.TryGetValue(kind, out var pair) ? pair.Created : 0;

    public int SkippedCount(string kind) => _counts.TryGetValue(kind, out var pair) ? pair.Skipped : 0;

    public IEnumerable<string> Lines() =>
        _counts.Select(pair => $"{pair.Key}: tạo mới {pair.Value.Created}, bỏ qua {pair.Value.Skipped}");

    private void Add(string kind, int created, int skipped)
    {
        _counts.TryGetValue(kind, out var current);
        _counts[kind] = (current.Created + created, current.Skipped + skipped);
    }
}

// Lỗi của dữ liệu mẫu hoặc của kho ảnh khi seed một sản phẩm. Lệnh in lý do và thoát với mã lỗi.
public class SeedException(string message, Exception? innerException = null) : Exception(message, innerException);

// Kho ảnh báo hết lượt gọi API. Lệnh dừng và người chạy lệnh phải chờ giới hạn đặt lại rồi chạy lại.
public sealed class SeedRateLimitedException(string message, Exception? innerException = null)
    : SeedException(message, innerException);

// Số lần thử lại và khoảng chờ cho lỗi tạm thời của kho ảnh. Hàm chờ tách riêng để test không phải chờ thật.
public sealed record SeedRetryPolicy(IReadOnlyList<TimeSpan> Delays, Func<TimeSpan, CancellationToken, Task> Wait)
{
    // Thử lại tối đa 3 lần, chờ tăng dần. Admin API treo thường qua đi sau vài giây.
    public static readonly SeedRetryPolicy Default = new(
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)],
        (delay, cancellationToken) => Task.Delay(delay, cancellationToken));
}

// Ghi dữ liệu mẫu qua cùng handler của trang quản trị. Nhờ vậy luật slug, SKU, giá và đăng bán không bị viết lại.
public static class CatalogSeeder
{
    public const string StockReason = "Dữ liệu mẫu";
    public const string ImageFolder = "pickory/products/seed";

    // Dữ liệu mẫu không do một người cụ thể thao tác, nên không có actor thật để ghi nhật ký quản trị.
    private static readonly Actor SystemActor = new()
    {
        AnonymousId = "catalog-seed",
        UserId = "catalog-seed",
        Kind = ActorKind.Staff,
        Roles = [],
        Permissions = [],
        Consent = ConsentState.AllDisabled,
    };

    public static async Task<SeedReport> SeedAsync(
        IServiceProvider services, SeedCatalog catalog, CancellationToken cancellationToken,
        TextWriter? progress = null, SeedRetryPolicy? retryPolicy = null)
    {
        var output = progress ?? TextWriter.Null;
        var policy = retryPolicy ?? SeedRetryPolicy.Default;
        var report = new SeedReport();
        var categoryIds = await SeedCategoriesAsync(services, catalog, report, cancellationToken);
        var brandIds = await SeedBrandsAsync(services, catalog, report, cancellationToken);
        var packagingByCategory = catalog.Categories.ToDictionary(category => category.Name, category => category.Packaging);

        for (var index = 0; index < catalog.Products.Count; index++)
        {
            var product = catalog.Products[index];
            // Mỗi sản phẩm một scope và một DbContext, để bộ nhớ không phình khi seed hàng trăm sản phẩm.
            await using var scope = services.CreateAsyncScope();
            var createdNow = await SeedProductAsync(
                scope.ServiceProvider, product, categoryIds[product.CategoryName], brandIds[product.BrandName],
                packagingByCategory[product.CategoryName], report, output, policy, cancellationToken);
            // Lệnh chạy nhiều phút. Mỗi sản phẩm một dòng để người chạy lệnh thấy lệnh còn chạy.
            await output.WriteLineAsync($"[{index + 1}/{catalog.Products.Count}] {product.Name}: {(createdNow ? "tạo mới" : "bỏ qua")}");
        }

        return report;
    }

    private static async Task<Dictionary<string, string>> SeedCategoriesAsync(
        IServiceProvider services, SeedCatalog catalog, SeedReport report, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var handler = CreateHandler<CategoryManagementHandler>(scope.ServiceProvider);
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var category in catalog.Categories)
        {
            // Khóa tự nhiên là slug sinh từ tên. Danh mục đã có thì bỏ qua và không ghi đè thuộc tính.
            var slug = SlugGenerator.ToSlug(category.Name);
            var existingId = await database.Categories.AsNoTracking()
                .Where(item => item.Slug == slug).Select(item => item.Id).FirstOrDefaultAsync(cancellationToken);
            if (existingId is not null)
            {
                ids[category.Name] = existingId;
                report.Skipped("Danh mục");
                continue;
            }

            var created = await handler.CreateAsync(
                SystemActor, new CreateCategoryRequest(category.Name, null, 0, category.Attributes), cancellationToken);
            ids[category.Name] = created.Id;
            report.Created("Danh mục");
        }

        return ids;
    }

    private static async Task<Dictionary<string, string>> SeedBrandsAsync(
        IServiceProvider services, SeedCatalog catalog, SeedReport report, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var handler = CreateHandler<BrandManagementHandler>(scope.ServiceProvider);
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var brandName in catalog.BrandNames)
        {
            var slug = SlugGenerator.ToSlug(brandName);
            var existingId = await database.Brands.AsNoTracking()
                .Where(item => item.Slug == slug).Select(item => item.Id).FirstOrDefaultAsync(cancellationToken);
            if (existingId is not null)
            {
                ids[brandName] = existingId;
                report.Skipped("Thương hiệu");
                continue;
            }

            var created = await handler.CreateAsync(SystemActor, new CreateBrandRequest(brandName, null), cancellationToken);
            ids[brandName] = created.Id;
            report.Created("Thương hiệu");
        }

        return ids;
    }

    // Trả true khi sản phẩm vừa được tạo, false khi sản phẩm đã có từ trước.
    private static async Task<bool> SeedProductAsync(
        IServiceProvider provider, SeedProduct seed, string categoryId, string brandId, SeedPackaging packaging,
        SeedReport report, TextWriter progress, SeedRetryPolicy retryPolicy, CancellationToken cancellationToken)
    {
        var database = provider.GetRequiredService<CatalogDbContext>();
        var handler = CreateHandler<ProductManagementHandler>(provider);

        // Khóa tự nhiên là slug. Sản phẩm đã có thì chỉ bỏ bước tạo, còn tồn kho và trạng thái vẫn được kiểm để lệnh chạy dở chạy lại được.
        var productId = await FindProductIdAsync(database, seed, cancellationToken);
        var createdNow = false;
        if (productId is null)
        {
            (productId, createdNow) = await CreateProductWithRetryAsync(
                provider, database, handler, seed, categoryId, brandId, packaging, report, progress, retryPolicy,
                cancellationToken);
        }
        else
        {
            report.Skipped("Sản phẩm");
        }

        await SeedStockAsync(provider, database, seed, productId, report, cancellationToken);
        await SeedStatusAsync(database, handler, seed, productId, report, cancellationToken);
        return createdNow;
    }

    private static Task<string?> FindProductIdAsync(CatalogDbContext database, SeedProduct seed, CancellationToken cancellationToken) =>
        database.Products.AsNoTracking()
            .Where(item => item.Slug == seed.Slug).Select(item => item.Id).FirstOrDefaultAsync(cancellationToken);

    // Tải ảnh rồi tạo sản phẩm. Lỗi tạm thời của kho ảnh được thử lại, mọi lỗi khác dừng lệnh và nêu tên sản phẩm.
    private static async Task<(string ProductId, bool CreatedNow)> CreateProductWithRetryAsync(
        IServiceProvider provider, CatalogDbContext database, ProductManagementHandler handler, SeedProduct seed,
        string categoryId, string brandId, SeedPackaging packaging, SeedReport report, TextWriter progress,
        SeedRetryPolicy retryPolicy, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                // Sau lần thử lỗi, lần thử trước có thể đã kịp tạo sản phẩm. Kiểm lại trong `try` để lỗi DB tạm thời ở đây cũng được thử lại.
                if (attempt > 0)
                {
                    var existingId = await FindProductIdAsync(database, seed, cancellationToken);
                    if (existingId is not null)
                    {
                        report.Skipped("Sản phẩm");
                        return (existingId, false);
                    }
                }

                // Tải ảnh trước khi tạo sản phẩm. Handler từ chối ảnh chưa có trên kho nên thiếu ảnh thì không tạo được sản phẩm.
                await EnsureImagesAsync(provider, seed, report, cancellationToken);
                var created = await handler.CreateAsync(
                    SystemActor, BuildRequest(seed, categoryId, brandId, packaging), cancellationToken);
                report.Created("Sản phẩm");
                report.Created("SKU", created.Variants.Count);
                report.Created("Ảnh", created.Images.Count);
                return (created.Id, true);
            }
            catch (Exception exception) when (ShouldWrapFailure(exception, cancellationToken))
            {
                var failure = ClassifyFailure(exception, cancellationToken);
                // Hết lượt gọi thì thử lại ngay chỉ tốn thêm lượt. Dừng lệnh và nói rõ cách xử lý.
                if (failure == StorageFailure.RateLimited)
                {
                    throw new SeedRateLimitedException(
                        $"Đã chạm giới hạn Admin API của Cloudinary khi seed \"{seed.Name}\" ({exception.Message}). " +
                        "Hãy chờ giới hạn đặt lại (mỗi giờ) rồi chạy lại lệnh. Sản phẩm đã tạo được giữ nguyên.",
                        exception);
                }

                var canRetry = failure == StorageFailure.Transient && attempt < retryPolicy.Delays.Count;
                if (!canRetry)
                {
                    throw new SeedException($"Không seed được \"{seed.Name}\": {exception.Message}", exception);
                }

                var delay = retryPolicy.Delays[attempt];
                await progress.WriteLineAsync(
                    $"  Thử lại lần {attempt + 1}/{retryPolicy.Delays.Count}: {seed.Name} (chờ {delay.TotalSeconds:0} giây): {exception.Message}");
                await retryPolicy.Wait(delay, cancellationToken);
            }
        }
    }

    private enum StorageFailure
    {
        Transient,
        RateLimited,
        Permanent,
    }

    // Người gọi hủy lệnh và lỗi dữ liệu mẫu đã nêu tên sản phẩm thì để nguyên, không bọc lại.
    private static bool ShouldWrapFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is not SeedException && !(exception is OperationCanceledException && cancellationToken.IsCancellationRequested);

    // EF Core bọc lỗi Npgsql tạm thời trong `InvalidOperationException`. Duyệt chuỗi lỗi bên trong để tìm lỗi gốc.
    private static bool HasTransientDatabaseCause(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is NpgsqlException { IsTransient: true })
            {
                return true;
            }
        }

        return false;
    }

    private static StorageFailure ClassifyFailure(Exception exception, CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case ImageRateLimitedException:
                return StorageFailure.RateLimited;
            case ImageUploadException upload:
                return upload.IsTransient ? StorageFailure.Transient : StorageFailure.Permanent;
            case HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests or (HttpStatusCode)420 }:
                return StorageFailure.RateLimited;
            case HttpRequestException { StatusCode: null }:
                // Không có mã trạng thái nghĩa là lỗi mạng trước khi nhận phản hồi.
                return StorageFailure.Transient;
            case HttpRequestException { StatusCode: var status } when (int)status >= 500 || status == HttpStatusCode.RequestTimeout:
                return StorageFailure.Transient;
            case Exception when HasTransientDatabaseCause(exception):
                // Pooler hoặc mạng ngắt kết nối DB giữa chừng. EF Core bọc lỗi này trong `InvalidOperationException`.
                return StorageFailure.Transient;
            case TaskCanceledException when !cancellationToken.IsCancellationRequested:
                // Hủy không do người gọi nghĩa là hết thời gian chờ của `HttpClient`.
                return StorageFailure.Transient;
            default:
                // Lỗi 4xx khác (khóa sai), ảnh sai định dạng hoặc lỗi ghi DB: thử lại vẫn lỗi như cũ.
                return StorageFailure.Permanent;
        }
    }

    private static CreateProductRequest BuildRequest(
        SeedProduct seed, string categoryId, string brandId, SeedPackaging packaging)
    {
        // `FromPlainText` kiểm tài liệu bằng `DescriptionSchema.Validate` trước khi trả về. Lỗi được đổi thành lỗi dữ liệu mẫu để lệnh dừng và nêu tên sản phẩm.
        JsonObject description;
        try
        {
            description = DescriptionText.FromPlainText(seed.Description);
        }
        catch (InvalidOperationException exception)
        {
            throw new SeedException($"Mô tả của \"{seed.Name}\" không hợp lệ: {exception.Message}");
        }

        var images = seed.Images
            .Select((_, index) => new ProductImageRequest(ImagePublicId(seed, index + 1), $"{seed.Name} - ảnh {index + 1}"))
            .ToList();
        var variants = seed.Variants.Select(variant => new ProductVariantRequest(
            variant.OptionValues, Code: null, variant.Price, variant.CompareAtPrice, Enabled: true,
            packaging.WeightGrams, packaging.LengthCm, packaging.WidthCm, packaging.HeightCm)).ToList();

        return new CreateProductRequest(
            categoryId, brandId, seed.Name, seed.ShortDesc,
            JsonSerializer.SerializeToElement(description),
            JsonSerializer.SerializeToElement(seed.Specs),
            seed.Options, seed.WarrantyMonths, variants, images);
    }

    // `publicId` suy ra từ slug và số thứ tự bắt đầu từ 1, nên lần chạy sau tìm lại đúng ảnh đã tải.
    public static string ImagePublicId(SeedProduct seed, int number) => $"{ImageFolder}/{seed.Slug}-{number}";

    // Gọi upload với `overwrite=false` cho từng ảnh. Cloudinary giữ ảnh đã có và upload API không tính vào giới hạn Admin API.
    private static async Task EnsureImagesAsync(
        IServiceProvider provider, SeedProduct seed, SeedReport report, CancellationToken cancellationToken)
    {
        var uploader = provider.GetRequiredService<IImageUploader>();
        for (var index = 0; index < seed.Images.Count; index++)
        {
            var publicId = ImagePublicId(seed, index + 1);
            var sourceUrl = seed.Images[index].SourceUrl;
            ImageUploadOutcome outcome;
            try
            {
                outcome = await uploader.UploadFromUrlAsync(publicId, sourceUrl, cancellationToken);
            }
            catch (ImageUploadException exception)
            {
                // Nêu `publicId` và URL nguồn để người chạy lệnh biết ảnh nào cần sửa. Giữ cờ tạm thời cho bước thử lại.
                throw new ImageUploadException(
                    $"Không tải được ảnh của \"{seed.Name}\" (publicId {publicId}, nguồn {sourceUrl}): {exception.Message}",
                    exception.IsTransient);
            }

            if (outcome == ImageUploadOutcome.AlreadyExisted)
            {
                report.Skipped("Ảnh đã tải lên");
            }
            else
            {
                report.Created("Ảnh đã tải lên");
            }
        }
    }

    // Ghi tồn kho ban đầu bằng movement `manual`. Cột `stock` không bao giờ được gán trực tiếp.
    private static async Task SeedStockAsync(
        IServiceProvider provider, CatalogDbContext database, SeedProduct seed, string productId, SeedReport report,
        CancellationToken cancellationToken)
    {
        var skus = await database.Skus.AsNoTracking().Where(sku => sku.ProductId == productId).ToListAsync(cancellationToken);
        var movements = new List<StockMovementInput>();
        foreach (var variant in seed.Variants.Where(variant => variant.Stock > 0))
        {
            var sku = skus.FirstOrDefault(item => SameOptions(item.OptionValues, variant.OptionValues))
                ?? throw new SeedException($"Không tìm thấy SKU của \"{seed.Name}\" cho tùy chọn {string.Join(", ", variant.OptionValues)}.");
            // Đã có movement dữ liệu mẫu cho SKU này thì bỏ qua, để lần chạy thứ hai không cộng tồn kho thêm.
            var alreadySeeded = await database.InventoryMovements.AnyAsync(movement =>
                movement.SkuId == sku.Id && movement.Type == InventoryMovementType.Manual && movement.Reason == StockReason,
                cancellationToken);
            if (alreadySeeded)
            {
                report.Skipped("Movement tồn kho");
                continue;
            }

            movements.Add(new StockMovementInput(sku.Id, variant.Stock, InventoryMovementType.Manual, null, null, StockReason));
        }

        if (movements.Count == 0)
        {
            return;
        }

        var timeProvider = provider.GetRequiredService<TimeProvider>();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var result = await StockLedger.ApplyAsync(
            transaction.GetDbTransaction(), movements, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        if (!result.Applied)
        {
            throw new SeedException($"Không ghi được tồn kho của \"{seed.Name}\".");
        }

        await transaction.CommitAsync(cancellationToken);
        report.Created("Movement tồn kho", movements.Count);
    }

    // Đăng bán qua `ChangeStatusAsync`, nên `ProductPublishing.CheckPublishable` chạy trước mỗi lần đổi sang `active`.
    private static async Task SeedStatusAsync(
        CatalogDbContext database, ProductManagementHandler handler, SeedProduct seed, string productId,
        SeedReport report, CancellationToken cancellationToken)
    {
        var current = await database.Products.AsNoTracking()
            .Where(item => item.Id == productId).Select(item => item.Status).SingleAsync(cancellationToken);
        if (seed.Status == ProductStatus.Draft || current == seed.Status)
        {
            return;
        }

        try
        {
            // Sản phẩm ngừng kinh doanh phải qua `active` trước, vì bản nháp không chuyển thẳng sang `discontinued`.
            if (current == ProductStatus.Draft)
            {
                await handler.ChangeStatusAsync(SystemActor, productId, ProductStatus.Active, cancellationToken);
                report.Created("Chuyển trạng thái");
            }

            if (seed.Status == ProductStatus.Discontinued)
            {
                await handler.ChangeStatusAsync(SystemActor, productId, ProductStatus.Discontinued, cancellationToken);
                report.Created("Chuyển trạng thái");
            }
        }
        catch (ProductNotPublishableException exception)
        {
            // Sản phẩm mẫu không qua điều kiện đăng bán là lỗi dữ liệu mẫu. In đủ lý do để sửa và dừng lệnh.
            throw new SeedException(
                $"Sản phẩm \"{seed.Name}\" chưa đủ điều kiện đăng bán: {string.Join(" | ", exception.Reasons)}");
        }
    }

    private static bool SameOptions(JsonDocument stored, IReadOnlyDictionary<string, string> expected)
    {
        var values = stored.RootElement.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetString());
        return values.Count == expected.Count && expected.All(pair => values.TryGetValue(pair.Key, out var value) && value == pair.Value);
    }

    // Handler nhận `IAuditLogWriter` rỗng, vì nhật ký quản trị cần người dùng thật còn dữ liệu mẫu thì không có.
    private static THandler CreateHandler<THandler>(IServiceProvider provider) where THandler : class =>
        ActivatorUtilities.CreateInstance<THandler>(provider, NullAuditLogWriter.Instance);
}

// Bỏ qua mọi nhật ký quản trị của lệnh seed.
internal sealed class NullAuditLogWriter : IAuditLogWriter
{
    public static readonly NullAuditLogWriter Instance = new();

    public Task WriteAsync(AuditLogEntry entry, DbTransaction transaction, CancellationToken cancellationToken) => Task.CompletedTask;
}
