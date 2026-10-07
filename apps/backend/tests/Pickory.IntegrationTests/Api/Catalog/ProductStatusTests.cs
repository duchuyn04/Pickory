using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Contracts.Events;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Features.Products;
using Pickory.Modules.Catalog.Images;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.IntegrationTests.Api.Catalog;

// CAT-07: đổi trạng thái sản phẩm và xóa cứng sản phẩm.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ProductStatusTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string StatusAuditAction = "catalog.product.status_change";

    private readonly IntegrationDatabaseFixture _database;
    private readonly PickoryWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];
    private string _adminCookie = null!;
    private string _salesCookie = null!;
    private string _plainCategoryId = null!;
    private string _requiredSpecCategoryId = null!;
    private string _brandId = null!;
    private readonly List<string> _productIds = new();

    public ProductStatusTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        await IdentityTestData.SeedRolesAsync(_database);
        var (_, adminToken) = await IdentityTestData.CreateStaffSessionAsync(_database, DefaultRoles.AdminCode, $"st-admin-{_suffix}");
        _adminCookie = $"pk_session={adminToken}";
        var (_, salesToken) = await IdentityTestData.CreateStaffSessionAsync(_database, DefaultRoles.SalesCode, $"st-sales-{_suffix}");
        _salesCookie = $"pk_session={salesToken}";

        await using var context = _database.CreateCatalogContext();
        // Danh mục không có thuộc tính bắt buộc, để sản phẩm mẫu chỉ có thể trượt ở ảnh hoặc SKU.
        var plainCategory = NewCategory("trang-thai", []);
        // Danh mục có thuộc tính bắt buộc, để kiểm tra lý do thiếu thông số.
        var requiredSpecCategory = NewCategory("thong-so", [
            new AttributeDefinition { Key = "ram_gb", Label = "RAM", Group = "Cấu hình", Type = "number", Required = true, Filterable = false },
        ]);
        var brand = new Brand
        {
            Id = Guid.NewGuid().ToString("N"), Name = $"Hãng trạng thái {_suffix}", Slug = $"hang-trang-thai-{_suffix}",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        context.Categories.AddRange(plainCategory, requiredSpecCategory);
        context.Brands.Add(brand);
        await context.SaveChangesAsync();
        _plainCategoryId = plainCategory.Id;
        _requiredSpecCategoryId = requiredSpecCategory.Id;
        _brandId = brand.Id;
    }

    public async Task DisposeAsync()
    {
        // Kiểm bất biến trước khi dọn, vì dọn dữ liệu xóa luôn movement.
        await StockInvariant.AssertHoldsAsync(_database);
        await using var context = _database.CreateCatalogContext();
        // Movement giữ khóa ngoại Restrict tới SKU, nên phải xóa trước sản phẩm.
        await context.InventoryMovements
            .Where(movement => context.Skus.Any(sku => sku.Id == movement.SkuId && _productIds.Contains(sku.ProductId)))
            .ExecuteDeleteAsync();
        await context.ImageDeletionJobs.Where(job => job.PublicId.Contains(_suffix)).ExecuteDeleteAsync();
        await context.Products.Where(product => _productIds.Contains(product.Id)).ExecuteDeleteAsync();
        await context.Categories.Where(category => category.Id == _plainCategoryId || category.Id == _requiredSpecCategoryId).ExecuteDeleteAsync();
        await context.Brands.Where(brand => brand.Id == _brandId).ExecuteDeleteAsync();
    }

    private Category NewCategory(string slugPrefix, IReadOnlyList<AttributeDefinition> attributes) => new()
    {
        Id = Guid.NewGuid().ToString("N"), Name = $"Danh mục {slugPrefix} {_suffix}", Slug = $"{slugPrefix}-{_suffix}",
        Position = 0, Attributes = JsonDocument.Parse(JsonSerializer.Serialize(attributes, AttributeDefinition.JsonOptions)),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private sealed record SkuSeed(string Code, bool Enabled = true, int Price = 1_000_000, int? WeightGrams = 200);

    // Seed thẳng vào DB để kiểm soát chính xác trạng thái, ảnh và SKU của từng tình huống.
    private async Task<string> SeedProductAsync(
        ProductStatus status = ProductStatus.Draft,
        int imageCount = 1,
        bool requiredSpecMissing = false,
        IReadOnlyList<SkuSeed>? skus = null)
    {
        var productId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var product = new Product
        {
            Id = productId,
            CategoryId = requiredSpecMissing ? _requiredSpecCategoryId : _plainCategoryId,
            BrandId = _brandId,
            Name = $"Sản phẩm trạng thái {productId}",
            Slug = $"trang-thai-{productId}",
            ShortDesc = "Mô tả ngắn",
            Description = JsonDocument.Parse("""{"type":"doc","content":[{"type":"paragraph"}]}"""),
            Specs = JsonDocument.Parse("{}"),
            Options = JsonDocument.Parse("[]"),
            WarrantyMonths = 12,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
        };
        foreach (var seed in skus ?? [new SkuSeed($"ST-{_suffix}-{productId[..6]}")])
        {
            product.Skus.Add(new Sku
            {
                Id = Guid.NewGuid().ToString("N"), ProductId = productId, Code = seed.Code,
                OptionValues = JsonDocument.Parse("{}"), Price = seed.Price, Enabled = seed.Enabled,
                WeightGrams = seed.WeightGrams, LengthCm = 15, WidthCm = 10, HeightCm = 5,
                CreatedAt = now, UpdatedAt = now,
            });
        }

        for (var position = 0; position < imageCount; position++)
        {
            product.Images.Add(new ProductImage
            {
                Id = Guid.NewGuid().ToString("N"), ProductId = productId, PublicId = ImagePublicId(productId, position),
                Alt = "Ảnh mẫu", Position = position, CreatedAt = now,
            });
        }

        await using var context = _database.CreateCatalogContext();
        context.Products.Add(product);
        await context.SaveChangesAsync();
        _productIds.Add(productId);
        return productId;
    }

    private string ImagePublicId(string productId, int position) => $"pickory/products/{_suffix}-{productId[..8]}-{position}";

    private async Task AddMovementAsync(string productId, InventoryMovementType type, string? orderId)
    {
        await using var context = _database.CreateCatalogContext();
        var skuId = await context.Skus.Where(sku => sku.ProductId == productId).Select(sku => sku.Id).FirstAsync();
        context.InventoryMovements.Add(new InventoryMovement
        {
            Id = Guid.NewGuid().ToString("N"), SkuId = skuId, Delta = 0, Type = type,
            OrderId = orderId, Reason = "Movement kiểm thử", CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> ChangeStatusAsync(string productId, object body, string? cookie = null) =>
        await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, $"/api/admin/products/{productId}/status", body, cookie ?? _adminCookie));

    private async Task<HttpResponseMessage> DeleteAsync(string productId, string? cookie = null) =>
        await _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Delete, $"/api/admin/products/{productId}", null, cookie ?? _adminCookie));

    private static async Task<JsonDocument> AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Mong đợi {status}, nhận {response.StatusCode}: {body}");
        var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
        return json;
    }

    private async Task<int> StatusAuditCountAsync(string productId)
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.AuditLogs.CountAsync(log => log.Action == StatusAuditAction && log.TargetId == productId);
    }

    private IReadOnlyList<ProductChangedEvent> EventsOf(string productId) =>
        _factory.DomainEvents.OfType<ProductChangedEvent>().Where(item => item.ProductId == productId).ToList();

    private async Task<ProductStatus> StoredStatusAsync(string productId)
    {
        await using var context = _database.CreateCatalogContext();
        return await context.Products.Where(product => product.Id == productId).Select(product => product.Status).SingleAsync();
    }

    // Lỗi đổi trạng thái không được để lại dấu vết: trạng thái cũ, không audit, không sự kiện.
    private async Task AssertNothingChangedAsync(string productId, ProductStatus expectedStatus)
    {
        Assert.Equal(expectedStatus, await StoredStatusAsync(productId));
        Assert.Equal(0, await StatusAuditCountAsync(productId));
        Assert.Empty(EventsOf(productId));
    }

    [Theory]
    [InlineData("published")]
    [InlineData("")]
    [InlineData("ACTIVE")]
    // Chỉ nhận đúng 4 tên trạng thái viết thường. Giá trị lạ làm yêu cầu bị từ chối trước khi đọc DB.
    public async Task Status_UnknownValue_Returns400(string status)
    {
        var productId = await SeedProductAsync();

        var response = await ChangeStatusAsync(productId, new { status });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await AssertNothingChangedAsync(productId, ProductStatus.Draft);
    }

    [Fact]
    public async Task Status_MissingStatusField_Returns400()
    {
        var productId = await SeedProductAsync();

        var response = await ChangeStatusAsync(productId, new { });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Status_Sales_Returns403AndKeepsStatus()
    {
        var productId = await SeedProductAsync();

        var response = await ChangeStatusAsync(productId, new { status = "active" }, _salesCookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNothingChangedAsync(productId, ProductStatus.Draft);
    }

    [Fact]
    public async Task Status_UnknownProduct_Returns404()
    {
        var response = await ChangeStatusAsync(Guid.NewGuid().ToString("N"), new { status = "active" });

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "PRODUCT_NOT_FOUND");
    }

    private static List<string> ReadReasons(JsonDocument error) =>
        error.RootElement.GetProperty("error").GetProperty("details").EnumerateArray()
            .Select(item => item.GetString()!).ToList();

    [Fact]
    // Số ảnh lấy từ bảng `product_images`, nên sản phẩm không có ảnh không đăng bán được.
    public async Task Activate_WithoutImage_Returns422WithReason()
    {
        var productId = await SeedProductAsync(imageCount: 0);

        var response = await ChangeStatusAsync(productId, new { status = "active" });

        using var error = await AssertErrorAsync(response, HttpStatusCode.UnprocessableEntity, "PRODUCT_NOT_PUBLISHABLE");
        Assert.Contains(ReadReasons(error), reason => reason.Contains("ảnh"));
        await AssertNothingChangedAsync(productId, ProductStatus.Draft);
    }

    [Fact]
    public async Task Activate_MissingRequiredSpec_Returns422WithReason()
    {
        var productId = await SeedProductAsync(requiredSpecMissing: true);

        var response = await ChangeStatusAsync(productId, new { status = "active" });

        using var error = await AssertErrorAsync(response, HttpStatusCode.UnprocessableEntity, "PRODUCT_NOT_PUBLISHABLE");
        Assert.Contains(ReadReasons(error), reason => reason.Contains("thông số"));
        await AssertNothingChangedAsync(productId, ProductStatus.Draft);
    }

    [Fact]
    // Lý do ghi mã SKU bị thiếu cân nặng. SKU đủ thông tin không xuất hiện trong lý do.
    public async Task Activate_EnabledSkuWithoutWeight_Returns422WithSkuCode()
    {
        var productId = await SeedProductAsync(skus:
        [
            new SkuSeed($"OK-{_suffix}"),
            new SkuSeed($"NOWEIGHT-{_suffix}", WeightGrams: null),
        ]);

        var response = await ChangeStatusAsync(productId, new { status = "active" });

        using var error = await AssertErrorAsync(response, HttpStatusCode.UnprocessableEntity, "PRODUCT_NOT_PUBLISHABLE");
        var reason = Assert.Single(ReadReasons(error));
        Assert.Equal($"SKU NOWEIGHT-{_suffix}: thiếu cân nặng hoặc kích thước đóng gói.", reason);
        await AssertNothingChangedAsync(productId, ProductStatus.Draft);
    }

    [Fact]
    // SKU đang tắt không bán cho khách, nên thiếu cân nặng ở SKU tắt không chặn đăng bán.
    public async Task Activate_DisabledSkuWithoutWeight_Succeeds()
    {
        var productId = await SeedProductAsync(skus:
        [
            new SkuSeed($"ON-{_suffix}"),
            new SkuSeed($"OFF-{_suffix}", Enabled: false, WeightGrams: null),
        ]);

        var response = await ChangeStatusAsync(productId, new { status = "active" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    // Nhiều điều kiện cùng thiếu thì danh sách lý do báo đủ, để admin sửa một lần.
    public async Task Activate_WithSeveralProblems_ListsEveryReason()
    {
        var productId = await SeedProductAsync(imageCount: 0, requiredSpecMissing: true,
            skus: [new SkuSeed($"NOWEIGHT-{_suffix}", WeightGrams: null)]);

        var response = await ChangeStatusAsync(productId, new { status = "active" });

        using var error = await AssertErrorAsync(response, HttpStatusCode.UnprocessableEntity, "PRODUCT_NOT_PUBLISHABLE");
        Assert.Equal(3, ReadReasons(error).Count);
    }

    [Fact]
    // Đổi thành công ghi đúng 1 dòng audit và phát đúng 1 sự kiện. Phản hồi là sản phẩm sau khi đổi.
    public async Task Activate_CompleteProduct_Returns200WithAuditAndEvent()
    {
        var productId = await SeedProductAsync();

        var response = await ChangeStatusAsync(productId, new { status = "active" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(productId, data.GetProperty("id").GetString());
        Assert.Equal("active", data.GetProperty("status").GetString());
        Assert.Equal(ProductStatus.Active, await StoredStatusAsync(productId));

        await using var identity = _database.CreateIdentityContext();
        var audit = await identity.AuditLogs.AsNoTracking()
            .Where(log => log.Action == StatusAuditAction && log.TargetId == productId).SingleAsync();
        Assert.Equal("product", audit.TargetType);
        Assert.Equal("draft", audit.Before!.RootElement.GetProperty("status").GetString());
        Assert.Equal("active", audit.After!.RootElement.GetProperty("status").GetString());

        var emitted = Assert.Single(EventsOf(productId));
        Assert.Equal(ProductChange.Status, emitted.Change);
    }

    [Fact]
    // Ẩn sản phẩm không cần điều kiện đăng bán, nên sản phẩm đang bán thiếu ảnh vẫn ẩn được.
    public async Task Hide_ActiveProductWithoutImage_Succeeds()
    {
        var productId = await SeedProductAsync(ProductStatus.Active, imageCount: 0);

        var response = await ChangeStatusAsync(productId, new { status = "hidden" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ProductStatus.Hidden, await StoredStatusAsync(productId));
        Assert.Equal(1, await StatusAuditCountAsync(productId));
    }

    [Fact]
    // Sản phẩm đã phát hành không quay về bản nháp. Bảng chuyển trạng thái từ chối bằng 409.
    public async Task Status_RejectedTransition_Returns409WithoutSideEffects()
    {
        var productId = await SeedProductAsync(ProductStatus.Active);

        var response = await ChangeStatusAsync(productId, new { status = "draft" });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "INVALID_STATUS_TRANSITION");
        await AssertNothingChangedAsync(productId, ProductStatus.Active);
    }

    [Theory]
    [InlineData(ProductStatus.Draft, "draft")]
    [InlineData(ProductStatus.Active, "active")]
    // Đổi sang đúng trạng thái hiện tại không có thay đổi. API trả 200, không audit, không phát sự kiện.
    public async Task Status_SameStatus_Returns200WithoutAuditOrEvent(ProductStatus current, string target)
    {
        var productId = await SeedProductAsync(current);

        var response = await ChangeStatusAsync(productId, new { status = target });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal(target, json.RootElement.GetProperty("data").GetProperty("status").GetString());
        await AssertNothingChangedAsync(productId, current);
    }

    [Fact]
    // Sản phẩm đang bán không đạt điều kiện đăng bán vẫn giữ trạng thái khi gửi lại `active`, vì không có thay đổi.
    public async Task Status_SameActiveStatusWithoutImage_Returns200()
    {
        var productId = await SeedProductAsync(ProductStatus.Active, imageCount: 0);

        var response = await ChangeStatusAsync(productId, new { status = "active" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertNothingChangedAsync(productId, ProductStatus.Active);
    }

    [Fact]
    public async Task Delete_Sales_Returns403AndKeepsProduct()
    {
        var productId = await SeedProductAsync();

        var response = await DeleteAsync(productId, _salesCookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var context = _database.CreateCatalogContext();
        Assert.True(await context.Products.AnyAsync(product => product.Id == productId));
    }

    [Fact]
    public async Task Delete_UnknownProduct_Returns404()
    {
        var response = await DeleteAsync(Guid.NewGuid().ToString("N"));

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "PRODUCT_NOT_FOUND");
    }

    [Fact]
    // Movement `order_reserve` nghĩa là SKU từng có đơn. Sản phẩm, movement và ảnh phải còn nguyên.
    public async Task Delete_WithOrderReserveMovement_Returns409AndKeepsEverything()
    {
        var productId = await SeedProductAsync(imageCount: 2);
        await AddMovementAsync(productId, InventoryMovementType.Manual, null);
        await AddMovementAsync(productId, InventoryMovementType.OrderReserve, $"order-{_suffix}");

        var response = await DeleteAsync(productId);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "PRODUCT_HAS_ORDERS");
        await using var context = _database.CreateCatalogContext();
        Assert.True(await context.Products.AnyAsync(product => product.Id == productId));
        Assert.Equal(2, await context.InventoryMovements.CountAsync(movement => context.Skus.Any(sku => sku.Id == movement.SkuId && sku.ProductId == productId)));
        Assert.Equal(2, await context.ProductImages.CountAsync(image => image.ProductId == productId));
        Assert.Empty(await context.ImageDeletionJobs.Where(job => job.PublicId.Contains(_suffix)).ToListAsync());
        Assert.Empty(EventsOf(productId));
    }

    [Fact]
    // Đơn đã giữ hàng rồi thả hàng vẫn là sản phẩm từng có đơn, nên không xóa cứng được.
    public async Task Delete_WithReserveAndReleaseMovements_Returns409()
    {
        var productId = await SeedProductAsync();
        await AddMovementAsync(productId, InventoryMovementType.OrderReserve, $"order-a-{_suffix}");
        await AddMovementAsync(productId, InventoryMovementType.OrderRelease, $"order-a-{_suffix}");

        var response = await DeleteAsync(productId);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "PRODUCT_HAS_ORDERS");
    }

    [Fact]
    // Movement `manual` và `return` bị xóa cùng sản phẩm (Decision 18). Mọi bản ghi con biến mất khỏi DB.
    public async Task Delete_WithManualAndReturnMovements_DeletesEverythingAndQueuesImageJobs()
    {
        var productId = await SeedProductAsync(imageCount: 2,
            skus: [new SkuSeed($"DEL-A-{_suffix}"), new SkuSeed($"DEL-B-{_suffix}")]);
        await AddMovementAsync(productId, InventoryMovementType.Manual, null);
        await AddMovementAsync(productId, InventoryMovementType.Return, $"order-return-{_suffix}");
        await using var before = _database.CreateCatalogContext();
        var skuIds = await before.Skus.Where(sku => sku.ProductId == productId).Select(sku => sku.Id).ToListAsync();

        var response = await DeleteAsync(productId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var context = _database.CreateCatalogContext();
        Assert.False(await context.Products.AnyAsync(product => product.Id == productId));
        Assert.False(await context.Skus.AnyAsync(sku => sku.ProductId == productId));
        Assert.False(await context.ProductImages.AnyAsync(image => image.ProductId == productId));
        Assert.False(await context.InventoryMovements.AnyAsync(movement => skuIds.Contains(movement.SkuId)));

        // Mỗi ảnh có đúng 1 job xóa, để Cloudinary không giữ ảnh mồ côi.
        var jobs = await context.ImageDeletionJobs.AsNoTracking().Where(job => job.PublicId.Contains(_suffix)).ToListAsync();
        var expectedPublicIds = new[] { ImagePublicId(productId, 0), ImagePublicId(productId, 1) }.Order();
        Assert.Equal(expectedPublicIds, jobs.Select(job => job.PublicId).Order());
        Assert.All(jobs, job => Assert.Equal(ImageDeletionProcessor.PendingStatus, job.Status));

        var emitted = Assert.Single(EventsOf(productId));
        Assert.Equal(ProductChange.Deleted, emitted.Change);
    }

    [Fact]
    public async Task Delete_ProductWithoutImages_CreatesNoJobs()
    {
        var productId = await SeedProductAsync(imageCount: 0);

        var response = await DeleteAsync(productId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var context = _database.CreateCatalogContext();
        Assert.Empty(await context.ImageDeletionJobs.Where(job => job.PublicId.Contains(_suffix)).ToListAsync());
    }

    [Fact]
    // Xóa phải chờ khóa ghi sản phẩm. Test giữ khóa ở kết nối riêng, nên nếu handler bỏ khóa thì yêu cầu xong ngay và test đỏ.
    public async Task Delete_WaitsForTheProductWriteLock()
    {
        var productId = await SeedProductAsync();
        await using var lockConnection = new NpgsqlConnection(_database.ConnectionString);
        await lockConnection.OpenAsync();
        await using var lockTransaction = await lockConnection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand($"SELECT pg_advisory_xact_lock({ProductManagementHandler.ProductWriteLockKey})", lockConnection, lockTransaction))
        {
            await command.ExecuteNonQueryAsync();
        }

        var pendingDelete = DeleteAsync(productId);
        // Chờ đủ lâu để yêu cầu chạy tới câu lệnh khóa. Mốc 1,5 giây chỉ ảnh hưởng độ nhạy, không ảnh hưởng độ đúng.
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        Assert.False(pendingDelete.IsCompleted, "Yêu cầu xóa phải chờ khóa ghi sản phẩm.");

        await lockTransaction.RollbackAsync();
        var response = await pendingDelete.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
