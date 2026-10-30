using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Data.Common;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Descriptions;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Catalog.Images;
using Pickory.Modules.Catalog.Products;
using Pickory.Modules.Catalog.Seeding;
using Xunit;

namespace Pickory.IntegrationTests.Api.Catalog;

// CAT-12: `catalog:seed` ghi dữ liệu mẫu qua đường ghi của trang quản trị, và chạy lại không tạo bản ghi trùng.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CatalogSeedTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly SeedCatalog _catalog = CatalogSeedData.Create();

    public CatalogSeedTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
    }

    public async Task InitializeAsync()
    {
        await CleanSeedDataAsync();
        await ResetSeedImagesAsync();
        _factory.ImageUploader.ClearFailures();
        _factory.ImageStorage.ClearFindFailures();
    }

    public async Task DisposeAsync()
    {
        // Kiểm bất biến trước khi dọn, vì dọn dữ liệu xóa luôn movement.
        await StockInvariant.AssertHoldsAsync(_database);
        await CleanSeedDataAsync();
    }

    // Xóa đúng dữ liệu mẫu theo slug, để test khác trong cùng DB không thấy thêm sản phẩm.
    private async Task CleanSeedDataAsync()
    {
        await using var context = _database.CreateCatalogContext();
        var productSlugs = _catalog.Products.Select(product => product.Slug).ToList();
        var categorySlugs = _catalog.Categories.Select(category => SlugGenerator.ToSlug(category.Name)).ToList();
        var brandSlugs = _catalog.BrandNames.Select(SlugGenerator.ToSlug).ToList();
        var skuIds = context.Skus.IgnoreQueryFilters().Where(sku => productSlugs.Contains(sku.Product!.Slug)).Select(sku => sku.Id);
        // Movement giữ khóa ngoại Restrict tới SKU, nên phải xóa trước sản phẩm.
        await context.InventoryMovements.Where(movement => skuIds.Contains(movement.SkuId)).ExecuteDeleteAsync();
        // Bỏ qua bộ lọc soft delete để dọn cả dòng đã xóa mềm, tránh khóa ngoại chặn xóa danh mục và hãng.
        await context.Products.IgnoreQueryFilters().Where(product => productSlugs.Contains(product.Slug)).ExecuteDeleteAsync();
        await context.Categories.IgnoreQueryFilters().Where(category => categorySlugs.Contains(category.Slug)).ExecuteDeleteAsync();
        await context.Brands.IgnoreQueryFilters().Where(brand => brandSlugs.Contains(brand.Slug)).ExecuteDeleteAsync();
    }

    // Kho ảnh giả giữ ảnh giữa các test của lớp này. Xóa ảnh mẫu để mỗi test bắt đầu với kho chưa có ảnh nào.
    private async Task ResetSeedImagesAsync()
    {
        foreach (var product in _catalog.Products)
        {
            for (var number = 1; number <= product.Images.Count; number++)
            {
                await _factory.ImageStorage.DeleteAsync(CatalogSeeder.ImagePublicId(product, number), CancellationToken.None);
            }
        }
    }

    private async Task<SeedReport> SeedAsync(SeedCatalog? catalog = null)
    {
        return await CatalogSeeder.SeedAsync(_factory.Services, catalog ?? _catalog, CancellationToken.None);
    }

    // Chính sách thử lại không chờ thật. Test ghi lại các khoảng chờ mà seeder yêu cầu.
    private sealed class RecordedWaits
    {
        public List<TimeSpan> Delays { get; } = [];

        public SeedRetryPolicy Policy => new(
            [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)],
            (delay, _) =>
            {
                Delays.Add(delay);
                return Task.CompletedTask;
            });
    }

    private async Task<SeedReport> SeedOneProductAsync(RecordedWaits waits, StringWriter progress)
    {
        var catalog = new SeedCatalog(_catalog.Categories, [_catalog.Products[0]]);
        return await CatalogSeeder.SeedAsync(_factory.Services, catalog, CancellationToken.None, progress, waits.Policy);
    }

    // Giả lỗi mạng DB tạm thời ở các truy vấn tra sản phẩm theo slug, bắt đầu từ truy vấn thứ `firstFailingQuery`.
    private sealed class ProductSlugQueryFault(int firstFailingQuery, int failureCount) : DbCommandInterceptor
    {
        private int _queries;
        private int _failures;

        public int Failures => Volatile.Read(ref _failures);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            var isProductSlugQuery = command.CommandText.Contains("slug", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("products", StringComparison.OrdinalIgnoreCase)
                && !command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase);
            if (isProductSlugQuery && Interlocked.Increment(ref _queries) >= firstFailingQuery
                && Volatile.Read(ref _failures) < failureCount)
            {
                Interlocked.Increment(ref _failures);
                // `IsTransient` của Npgsql đúng khi lỗi bọc một lỗi `IOException`, như khi pooler ngắt kết nối.
                throw new NpgsqlException("connection reset", new IOException("pooler closed the connection"));
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private WebApplicationFactory<Program> FactoryWith(ProductSlugQueryFault fault) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.ConfigureDbContext<CatalogDbContext>(options => options.AddInterceptors(fault))));

    private static TaskCanceledException HttpClientTimeout() => new("timeout", new TimeoutException());

    private static HttpRequestException RateLimited() => new("limit", null, (HttpStatusCode)420);

    private async Task<Dictionary<string, int>> CountRowsAsync()
    {
        await using var context = _database.CreateCatalogContext();
        var productSlugs = _catalog.Products.Select(product => product.Slug).ToList();
        var skuIds = context.Skus.Where(sku => productSlugs.Contains(sku.Product!.Slug)).Select(sku => sku.Id);
        return new Dictionary<string, int>
        {
            ["categories"] = await context.Categories.CountAsync(category =>
                _catalog.Categories.Select(item => SlugGenerator.ToSlug(item.Name)).Contains(category.Slug)),
            ["brands"] = await context.Brands.CountAsync(brand =>
                _catalog.BrandNames.Select(SlugGenerator.ToSlug).Contains(brand.Slug)),
            ["products"] = await context.Products.CountAsync(product => productSlugs.Contains(product.Slug)),
            ["skus"] = await context.Skus.CountAsync(sku => skuIds.Contains(sku.Id)),
            ["images"] = await context.ProductImages.CountAsync(image => productSlugs.Contains(image.Product!.Slug)),
            ["movements"] = await context.InventoryMovements.CountAsync(movement => skuIds.Contains(movement.SkuId)),
        };
    }

    [Fact]
    public async Task Seed_RunTwice_DoesNotCreateDuplicates()
    {
        var first = await SeedAsync();
        var afterFirst = await CountRowsAsync();
        var second = await SeedAsync();
        var afterSecond = await CountRowsAsync();

        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(5, afterFirst["categories"]);
        Assert.Equal(_catalog.Products.Count, afterFirst["products"]);
        Assert.Equal(_catalog.Products.Sum(product => product.Variants.Count), afterFirst["skus"]);
        Assert.Equal(_catalog.Products.Sum(product => product.Images.Count), afterFirst["images"]);
        Assert.Equal(_catalog.Products.Sum(product => product.Variants.Count(variant => variant.Stock > 0)), afterFirst["movements"]);
        // Lần chạy thứ hai không tạo gì mới ở bất kỳ loại bản ghi nào.
        Assert.Equal(_catalog.Products.Count, first.CreatedCount("Sản phẩm"));
        Assert.Equal(0, second.CreatedCount("Sản phẩm"));
        Assert.Equal(0, second.CreatedCount("Danh mục"));
        Assert.Equal(0, second.CreatedCount("Thương hiệu"));
        Assert.Equal(0, second.CreatedCount("Movement tồn kho"));
        Assert.Equal(0, second.CreatedCount("Chuyển trạng thái"));
        Assert.Equal(_catalog.Products.Count, second.SkippedCount("Sản phẩm"));
    }

    [Fact]
    public async Task Seed_EveryActiveProduct_PassesCheckPublishable()
    {
        await SeedAsync();

        await using var context = _database.CreateCatalogContext();
        var productSlugs = _catalog.Products.Select(product => product.Slug).ToList();
        var products = await context.Products.AsNoTracking()
            .Include(product => product.Skus).Include(product => product.Images).Include(product => product.Category)
            .Where(product => productSlugs.Contains(product.Slug)).ToListAsync();

        var active = products.Where(product => product.Status == ProductStatus.Active).ToList();
        Assert.NotEmpty(active);
        foreach (var product in active)
        {
            var attributes = product.Category!.Attributes.Deserialize<List<AttributeDefinition>>(AttributeDefinition.JsonOptions)!;
            var input = new ProductPublishableInput(
                JsonNode.Parse(product.Specs.RootElement.GetRawText()), attributes, product.Images.Count,
                product.Skus.Select(sku => new SkuPublishableInput(sku.Code, sku.Enabled, sku.Price, sku.CompareAtPrice,
                    sku.WeightGrams, sku.LengthCm, sku.WidthCm, sku.HeightCm)).ToList());
            var check = ProductPublishing.CheckPublishable(input);
            Assert.True(check.Publishable, $"{product.Slug}: {string.Join(" | ", check.Reasons)}");
        }
    }

    [Fact]
    public async Task Seed_StatusesFollowTheSeedData()
    {
        await SeedAsync();

        await using var context = _database.CreateCatalogContext();
        var statusBySlug = await context.Products.AsNoTracking()
            .Where(product => _catalog.Products.Select(item => item.Slug).Contains(product.Slug))
            .ToDictionaryAsync(product => product.Slug, product => product.Status);
        foreach (var product in _catalog.Products)
        {
            Assert.Equal(product.Status, statusBySlug[product.Slug]);
        }

        Assert.True(_catalog.Products.Count(product => product.Status == ProductStatus.Discontinued) >= 2);
    }

    [Fact]
    public async Task Seed_StockOnlyChangesThroughOneManualMovementPerSku()
    {
        await SeedAsync();
        await SeedAsync();

        await using var context = _database.CreateCatalogContext();
        var productSlugs = _catalog.Products.Select(product => product.Slug).ToList();
        var skus = await context.Skus.AsNoTracking().Include(sku => sku.Movements)
            .Where(sku => productSlugs.Contains(sku.Product!.Slug)).ToListAsync();

        var withStock = skus.Where(sku => sku.Stock > 0).ToList();
        Assert.NotEmpty(withStock);
        foreach (var sku in withStock)
        {
            var movement = Assert.Single(sku.Movements);
            Assert.Equal(InventoryMovementType.Manual, movement.Type);
            Assert.Equal("Dữ liệu mẫu", movement.Reason);
            Assert.Equal(sku.Stock, movement.Delta);
        }

        // SKU hết hàng không có movement nào, và tồn kho luôn bằng tổng movement.
        foreach (var sku in skus.Where(sku => sku.Stock == 0))
        {
            Assert.Empty(sku.Movements);
        }

        Assert.All(skus, sku => Assert.Equal(sku.Movements.Sum(movement => movement.Delta), sku.Stock));
        Assert.Contains(_catalog.Products, product => product.Variants.Any(variant => variant.Stock == 0));
    }

    [Fact]
    public async Task Seed_EveryDescription_PassesTheDescriptionSchema()
    {
        await SeedAsync();

        await using var context = _database.CreateCatalogContext();
        var productSlugs = _catalog.Products.Select(product => product.Slug).ToList();
        var descriptions = await context.Products.AsNoTracking()
            .Where(product => productSlugs.Contains(product.Slug)).Select(product => product.Description).ToListAsync();

        Assert.Equal(_catalog.Products.Count, descriptions.Count);
        foreach (var description in descriptions)
        {
            var validation = DescriptionSchema.Validate(JsonNode.Parse(description.RootElement.GetRawText()));
            Assert.True(validation.Valid, string.Join(" ", validation.Errors));
        }
    }

    [Fact]
    public async Task Seed_ProductsAppearInThePublicList_WithFacetCounts()
    {
        await SeedAsync();
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/products?category=laptop&pageSize=48");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Laptop mẫu có 10 sản phẩm khách thấy được, tất cả đang bán.
        Assert.Equal(10, body.GetProperty("total").GetInt32());
        var ramFacet = body.GetProperty("facets").GetProperty("attributes").EnumerateArray()
            .Single(attribute => attribute.GetProperty("key").GetString() == "ram_gb");
        Assert.True(ramFacet.GetProperty("values").EnumerateArray().Sum(value => value.GetProperty("count").GetInt32()) > 0);

        var detail = await client.GetAsync($"/api/products/{_catalog.Products[0].Slug}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Fact]
    public async Task Seed_DescriptionThatBreaksTheSchema_FailsWithTheProductName()
    {
        // Mô tả dài quá giới hạn ký tự của schema, nên lệnh dừng và nêu tên sản phẩm thay vì lưu tài liệu sai.
        var broken = _catalog.Products[0] with { Description = new string('a', DescriptionSchema.MaxTextLength + 1) };
        var catalog = new SeedCatalog(_catalog.Categories, [broken]);

        var error = await Assert.ThrowsAsync<SeedException>(() => SeedAsync(catalog));

        Assert.Contains(broken.Name, error.Message);
        Assert.Contains("không hợp lệ", error.Message);
    }

    [Fact]
    public async Task Seed_ProductThatCannotBePublished_FailsWithTheReasons()
    {
        // Sản phẩm không có ảnh nào thì không đủ điều kiện đăng bán, nên lệnh dừng và nêu lý do.
        var broken = _catalog.Products[0] with { Images = [] };
        var catalog = new SeedCatalog(_catalog.Categories, [broken]);

        var error = await Assert.ThrowsAsync<SeedException>(() => SeedAsync(catalog));

        Assert.Contains("ít nhất 1 ảnh", error.Message);
        Assert.Contains(broken.Name, error.Message);
    }

    [Fact]
    public async Task Seed_FirstRun_UploadsEveryImageThatIsNotOnTheStorage()
    {
        // Bộ tải giả giữ lịch sử của mọi test trong lớp, nên chỉ đếm các lần tải sau mốc này.
        var uploadsBefore = _factory.ImageUploader.Uploads.Count;

        var report = await SeedAsync();

        var expectedUploads = _catalog.Products
            .SelectMany(product => product.Images.Select((image, index) =>
                (PublicId: CatalogSeeder.ImagePublicId(product, index + 1), image.SourceUrl)))
            .ToList();
        var uploads = _factory.ImageUploader.Uploads.Skip(uploadsBefore).ToList();
        Assert.Equal(expectedUploads.Count, uploads.Count);
        Assert.Equal(expectedUploads.OrderBy(upload => upload.PublicId), uploads.OrderBy(upload => upload.PublicId));
        Assert.Equal(expectedUploads.Count, report.CreatedCount("Ảnh đã tải lên"));
    }

    [Fact]
    public async Task Seed_ImageAlreadyOnTheStorage_IsNotUploadedAgain()
    {
        var product = _catalog.Products[0];
        var knownPublicId = CatalogSeeder.ImagePublicId(product, 1);
        _factory.ImageStorage.Add(knownPublicId, 200_000, "webp");
        var uploadsBefore = _factory.ImageUploader.Uploads.Count;

        await SeedAsync(new SeedCatalog(_catalog.Categories, [product]));

        var newUploads = _factory.ImageUploader.Uploads.Skip(uploadsBefore).ToList();
        Assert.DoesNotContain(newUploads, upload => upload.PublicId == knownPublicId);
        Assert.Equal(product.Images.Count - 1, newUploads.Count);
    }

    [Fact]
    public async Task Seed_SecondRun_UploadsNothing()
    {
        await SeedAsync();
        var uploadsAfterFirst = _factory.ImageUploader.Uploads.Count;

        var second = await SeedAsync();

        Assert.Equal(uploadsAfterFirst, _factory.ImageUploader.Uploads.Count);
        Assert.Equal(0, second.CreatedCount("Ảnh đã tải lên"));
    }

    [Fact]
    public async Task Seed_ImageThatCannotBeUploaded_FailsWithTheProductNameAndTheSourceUrl()
    {
        var product = _catalog.Products[0];
        var brokenUrl = product.Images[1].SourceUrl;
        _factory.ImageUploader.FailFor(brokenUrl, "Invalid image file");

        var error = await Assert.ThrowsAsync<SeedException>(
            () => SeedAsync(new SeedCatalog(_catalog.Categories, [product])));

        Assert.Contains(product.Name, error.Message);
        Assert.Contains(CatalogSeeder.ImagePublicId(product, 2), error.Message);
        Assert.Contains(brokenUrl, error.Message);
        Assert.Contains("Invalid image file", error.Message);
    }

    [Fact]
    public async Task Seed_DoesNotAskTheStorageAboutEachImageBeforeUploading()
    {
        var product = _catalog.Products[0];
        var findsBefore = _factory.ImageStorage.FindCount;

        await SeedAsync(new SeedCatalog(_catalog.Categories, [product]));

        // Chỉ bước kiểm ảnh của handler hỏi kho ảnh, mỗi ảnh một lần. Seeder không hỏi thêm lần nào.
        Assert.Equal(product.Images.Count, _factory.ImageStorage.FindCount - findsBefore);
    }

    [Fact]
    public async Task Seed_ImageAlreadyOnTheStorage_IsCountedAsSkippedAndNotOverwritten()
    {
        var product = _catalog.Products[0];
        _factory.ImageStorage.Add(CatalogSeeder.ImagePublicId(product, 1), 200_000, "webp");

        var report = await SeedAsync(new SeedCatalog(_catalog.Categories, [product]));

        Assert.Equal(1, report.SkippedCount("Ảnh đã tải lên"));
        Assert.Equal(product.Images.Count - 1, report.CreatedCount("Ảnh đã tải lên"));
    }

    [Fact]
    public async Task Seed_StorageTimesOutOnce_RetriesAndCreatesTheProduct()
    {
        var product = _catalog.Products[0];
        _factory.ImageStorage.QueueFindFailures(CatalogSeeder.ImagePublicId(product, 2), HttpClientTimeout());
        var waits = new RecordedWaits();
        var progress = new StringWriter();

        var report = await SeedOneProductAsync(waits, progress);

        Assert.Equal(1, report.CreatedCount("Sản phẩm"));
        Assert.Equal([TimeSpan.FromSeconds(5)], waits.Delays);
        Assert.Contains($"Thử lại lần 1/3: {product.Name}", progress.ToString());
    }

    [Fact]
    public async Task Seed_UploadTimesOutTwice_WaitsLongerEachTimeAndCreatesTheProduct()
    {
        var product = _catalog.Products[0];
        _factory.ImageUploader.QueueFailures(
            product.Images[0].SourceUrl,
            new ImageUploadException("Cloudinary không phản hồi kịp", isTransient: true),
            new ImageUploadException("Cloudinary không phản hồi kịp", isTransient: true));
        var waits = new RecordedWaits();

        var report = await SeedOneProductAsync(waits, new StringWriter());

        Assert.Equal(1, report.CreatedCount("Sản phẩm"));
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)], waits.Delays);
    }

    [Fact]
    public async Task Seed_StorageKeepsTimingOut_StopsAfterThreeRetriesAndNamesTheProduct()
    {
        var product = _catalog.Products[0];
        var publicId = CatalogSeeder.ImagePublicId(product, 1);
        _factory.ImageStorage.QueueFindFailures(
            publicId, HttpClientTimeout(), HttpClientTimeout(), HttpClientTimeout(), HttpClientTimeout());
        var waits = new RecordedWaits();

        var error = await Assert.ThrowsAsync<SeedException>(() => SeedOneProductAsync(waits, new StringWriter()));

        Assert.Equal(3, waits.Delays.Count);
        Assert.Contains(product.Name, error.Message);
    }

    [Fact]
    public async Task Seed_UploadKeepsFailingOnTheNetwork_FailsWithProductPublicIdAndSourceUrl()
    {
        var product = _catalog.Products[0];
        var transient = new ImageUploadException("Không gọi được Cloudinary: connection reset", isTransient: true);
        _factory.ImageUploader.QueueFailures(
            product.Images[0].SourceUrl, transient, transient, transient, transient);

        var error = await Assert.ThrowsAsync<SeedException>(
            () => SeedOneProductAsync(new RecordedWaits(), new StringWriter()));

        Assert.Contains(product.Name, error.Message);
        Assert.Contains(CatalogSeeder.ImagePublicId(product, 1), error.Message);
        Assert.Contains(product.Images[0].SourceUrl, error.Message);
    }

    [Fact]
    public async Task Seed_DataErrorFromTheStorage_IsNotRetried()
    {
        var product = _catalog.Products[0];
        _factory.ImageUploader.FailFor(product.Images[0].SourceUrl, "Invalid image file");
        var waits = new RecordedWaits();

        await Assert.ThrowsAsync<SeedException>(() => SeedOneProductAsync(waits, new StringWriter()));

        Assert.Empty(waits.Delays);
    }

    [Fact]
    public async Task Seed_StorageRejectsTheKey_FailsWithTheProductNameAndIsNotRetried()
    {
        var product = _catalog.Products[0];
        _factory.ImageStorage.QueueFindFailures(
            CatalogSeeder.ImagePublicId(product, 1),
            new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized));
        var waits = new RecordedWaits();

        var error = await Assert.ThrowsAsync<SeedException>(() => SeedOneProductAsync(waits, new StringWriter()));

        Assert.Empty(waits.Delays);
        Assert.Contains(product.Name, error.Message);
        Assert.Contains("Unauthorized", error.Message);
    }

    [Fact]
    public async Task Seed_AdminApiRateLimitOnFind_StopsWithoutRetryAndExplainsTheLimit()
    {
        var product = _catalog.Products[0];
        _factory.ImageStorage.QueueFindFailures(CatalogSeeder.ImagePublicId(product, 1), RateLimited());
        var waits = new RecordedWaits();

        var error = await Assert.ThrowsAsync<SeedRateLimitedException>(
            () => SeedOneProductAsync(waits, new StringWriter()));

        Assert.Empty(waits.Delays);
        Assert.Contains("giới hạn Admin API của Cloudinary", error.Message);
        Assert.Contains("chạy lại", error.Message);
        Assert.Contains(product.Name, error.Message);
    }

    [Fact]
    public async Task Seed_UploadRateLimited_StopsWithoutRetry()
    {
        var product = _catalog.Products[0];
        _factory.ImageUploader.QueueFailures(product.Images[0].SourceUrl, new ImageRateLimitedException("HTTP 420"));
        var waits = new RecordedWaits();

        await Assert.ThrowsAsync<SeedRateLimitedException>(() => SeedOneProductAsync(waits, new StringWriter()));

        Assert.Empty(waits.Delays);
    }

    [Fact]
    public async Task Seed_PrintsOneProgressLinePerProduct()
    {
        var products = _catalog.Products.Take(2).ToList();
        var catalog = new SeedCatalog(_catalog.Categories, products);
        var waits = new RecordedWaits();
        var first = new StringWriter();
        var second = new StringWriter();

        await CatalogSeeder.SeedAsync(_factory.Services, catalog, CancellationToken.None, first, waits.Policy);
        await CatalogSeeder.SeedAsync(_factory.Services, catalog, CancellationToken.None, second, waits.Policy);

        Assert.Contains($"[1/2] {products[0].Name}: tạo mới", first.ToString());
        Assert.Contains($"[2/2] {products[1].Name}: tạo mới", first.ToString());
        Assert.Contains($"[1/2] {products[0].Name}: bỏ qua", second.ToString());
    }

    [Fact]
    public async Task Seed_TransientDatabaseErrorWhileRecheckingTheSlug_IsRetried()
    {
        var product = _catalog.Products[0];
        var fault = new ProductSlugQueryFault(firstFailingQuery: 2, failureCount: 1);
        using var factory = FactoryWith(fault);
        // Lần thử đầu hỏng ở kho ảnh. Truy vấn slug thứ hai (bước tra lại sau khi chờ) hỏng vì lỗi DB tạm thời.
        factory.Services.GetRequiredService<FakeImageStorage>()
            .QueueFindFailures(CatalogSeeder.ImagePublicId(product, 1), HttpClientTimeout());
        var waits = new RecordedWaits();
        var progress = new StringWriter();

        var report = await CatalogSeeder.SeedAsync(
            factory.Services, new SeedCatalog(_catalog.Categories, [product]), CancellationToken.None, progress, waits.Policy);

        Assert.Equal(1, fault.Failures);
        Assert.Equal(1, report.CreatedCount("Sản phẩm"));
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)], waits.Delays);
    }

    [Fact]
    public async Task Seed_DatabaseKeepsFailingWhileRecheckingTheSlug_FailsWithTheProductName()
    {
        var product = _catalog.Products[0];
        var fault = new ProductSlugQueryFault(firstFailingQuery: 2, failureCount: int.MaxValue);
        using var factory = FactoryWith(fault);
        factory.Services.GetRequiredService<FakeImageStorage>()
            .QueueFindFailures(CatalogSeeder.ImagePublicId(product, 1), HttpClientTimeout());
        var waits = new RecordedWaits();

        var error = await Assert.ThrowsAsync<SeedException>(() => CatalogSeeder.SeedAsync(
            factory.Services, new SeedCatalog(_catalog.Categories, [product]), CancellationToken.None,
            new StringWriter(), waits.Policy));

        Assert.Equal(3, waits.Delays.Count);
        Assert.Contains(product.Name, error.Message);
    }
}
