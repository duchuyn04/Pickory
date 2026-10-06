using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.IntegrationTests.Api.Catalog;

// Kiểm tra toàn bộ luồng quản lý thương hiệu từ route công khai tới quản trị.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class BrandManagementTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;
    private string _adminCookie = null!;
    private string _salesCookie = null!;

    public BrandManagementTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        // Tắt tự động nạp cookie để bài test kiểm soát phiên người dùng chính xác.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        // Khởi tạo role hệ thống để cấp quyền cho tài khoản kiểm thử.
        await IdentityTestData.SeedRolesAsync(_database);

        var (_, adminToken) = await IdentityTestData.CreateStaffSessionAsync(_database, DefaultRoles.AdminCode, "brand-admin");
        _adminCookie = $"pk_session={adminToken}";

        var (_, salesToken) = await IdentityTestData.CreateStaffSessionAsync(_database, DefaultRoles.SalesCode, "brand-sales");
        _salesCookie = $"pk_session={salesToken}";
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    // Khách vãng lai xem được danh sách thương hiệu sắp xếp theo tên.
    public async Task GetBrands_ReturnsListSortedByName()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var brandA = await CreateBrandInDbAsync($"A Brand {suffix}");
        var brandB = await CreateBrandInDbAsync($"B Brand {suffix}");

        var request = ActorHttp.CreateRequest(HttpMethod.Get, "/api/brands");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var items = json.RootElement.GetProperty("data").EnumerateArray().ToList();
        Assert.Contains(items, item => item.GetProperty("id").GetString() == brandA.Id);
        Assert.Contains(items, item => item.GetProperty("id").GetString() == brandB.Id);
    }

    [Fact]
    // Tạo thương hiệu thành công và sinh slug tương ứng.
    public async Task CreateBrand_SucceedsWithSlug()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var payload = new
        {
            name = $"Thương Hiệu {suffix}",
            logoPublicId = "logo_123"
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/brands", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal($"Thương Hiệu {suffix}", data.GetProperty("name").GetString());
        Assert.StartsWith("thuong-hieu-", data.GetProperty("slug").GetString());
    }

    [Fact]
    // Xóa thương hiệu đang có sản phẩm thì bị từ chối với mã 400 BRAND_HAS_PRODUCTS.
    public async Task DeleteBrand_WithProducts_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var brand = await CreateBrandInDbAsync($"Asus {suffix}");
        await CreateProductInDbAsync(brand.Id);

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Delete, $"/api/admin/brands/{brand.Id}", null, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("BRAND_HAS_PRODUCTS", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Nhân viên sales chỉ có quyền đọc nên gọi GET được và gọi POST tạo thương hiệu nhận 403.
    public async Task Sales_CanGetBrands_CannotCreateBrand()
    {
        // Gọi GET thành công với mã 200.
        var getRequest = ActorHttp.CreateJsonRequest(HttpMethod.Get, "/api/brands", null, _salesCookie);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // Gọi POST tạo mới nhận mã 403 do thiếu quyền products:write.
        var postRequest = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/brands", new { name = "Nhãn mới" }, _salesCookie);
        var postResponse = await _client.SendAsync(postRequest);
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);
    }

    [Fact]
    // Cập nhật hoặc xóa thương hiệu không tồn tại thì API trả mã lỗi 404.
    public async Task UpdateOrDelete_NonExistentBrand_Returns404()
    {
        var nonExistentId = Guid.NewGuid().ToString("N");

        var patchRequest = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/brands/{nonExistentId}", new { name = "Tên mới" }, _adminCookie);
        var patchResponse = await _client.SendAsync(patchRequest);
        Assert.Equal(HttpStatusCode.NotFound, patchResponse.StatusCode);

        var deleteRequest = ActorHttp.CreateJsonRequest(HttpMethod.Delete, $"/api/admin/brands/{nonExistentId}", null, _adminCookie);
        var deleteResponse = await _client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
    }

    [Fact]
    // Trùng tên thương hiệu thì slug tự động thêm hậu tố số để đảm bảo tính duy nhất.
    public async Task CreateBrand_DuplicateName_GeneratesNumberedSlug()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"Apple {suffix}";

        var firstReq = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/brands", new { name }, _adminCookie);
        var firstRes = await _client.SendAsync(firstReq);
        Assert.Equal(HttpStatusCode.Created, firstRes.StatusCode);
        using var firstJson = await ActorHttp.ReadJsonAsync(firstRes);
        var firstSlug = firstJson.RootElement.GetProperty("data").GetProperty("slug").GetString()!;

        var secondReq = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/brands", new { name }, _adminCookie);
        var secondRes = await _client.SendAsync(secondReq);
        Assert.Equal(HttpStatusCode.Created, secondRes.StatusCode);
        using var secondJson = await ActorHttp.ReadJsonAsync(secondRes);
        var secondSlug = secondJson.RootElement.GetProperty("data").GetProperty("slug").GetString()!;

        Assert.Equal($"{firstSlug}-2", secondSlug);
    }

    [Fact]
    // Đổi tên thương hiệu không làm thay đổi slug ban đầu để giữ tính liên tục của URL chia sẻ.
    public async Task UpdateBrand_ChangeName_PreservesOriginalSlug()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var brand = await CreateBrandInDbAsync($"Brand Gốc {suffix}");
        var originalSlug = brand.Slug;

        var payload = new { name = $"Brand Mới {suffix}" };
        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/brands/{brand.Id}", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal($"Brand Mới {suffix}", data.GetProperty("name").GetString());
        Assert.Equal(originalSlug, data.GetProperty("slug").GetString());
    }

    [Fact]
    // Logo vượt quá độ dài tối đa 255 ký tự thì bị từ chối bằng lỗi 400.
    public async Task CreateBrand_LogoPublicIdTooLong_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var payload = new
        {
            name = $"Brand {suffix}",
            logoPublicId = new string('a', 256)
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/brands", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("VALIDATION_ERROR", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Mọi thao tác ghi thương hiệu đều tạo bản ghi nhật ký kiểm toán trong cơ sở dữ liệu.
    public async Task BrandOperations_RecordAuditLogs()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var createReq = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/brands", new { name = "Nhật ký " + suffix }, _adminCookie);
        var createRes = await _client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        using var createJson = await ActorHttp.ReadJsonAsync(createRes);
        var brandId = createJson.RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var patchReq = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/brands/{brandId}", new { name = "Sửa nhật ký " + suffix }, _adminCookie);
        var patchRes = await _client.SendAsync(patchReq);
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        var deleteReq = ActorHttp.CreateJsonRequest(HttpMethod.Delete, $"/api/admin/brands/{brandId}", null, _adminCookie);
        var deleteRes = await _client.SendAsync(deleteReq);
        Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);

        // Kiểm tra ba dòng nhật ký tương ứng trong cơ sở dữ liệu identity.
        await using var identity = _database.CreateIdentityContext();
        var logs = await identity.AuditLogs.Where(log => log.TargetId == brandId).ToListAsync();
        Assert.Equal(3, logs.Count);
        Assert.Contains(logs, log => log.Action == "catalog.brand.create");
        Assert.Contains(logs, log => log.Action == "catalog.brand.update");
        Assert.Contains(logs, log => log.Action == "catalog.brand.delete");
    }
    private async Task<Brand> CreateBrandInDbAsync(string name)
    {
        await using var db = _database.CreateCatalogContext();
        var brand = new Brand
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Slug = $"brand-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Brands.Add(brand);
        await db.SaveChangesAsync();
        return brand;
    }

    private async Task<Product> CreateProductInDbAsync(string brandId)
    {
        await using var db = _database.CreateCatalogContext();
        var category = new Category
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Danh mục thử",
            Slug = $"cat-{Guid.NewGuid():N}",
            Attributes = JsonDocument.Parse("[]"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Categories.Add(category);

        var product = new Product
        {
            Id = Guid.NewGuid().ToString("N"),
            CategoryId = category.Id,
            BrandId = brandId,
            Name = "Sản phẩm thử",
            Slug = $"product-{Guid.NewGuid():N}",
            ShortDesc = "Mô tả ngắn",
            Description = JsonDocument.Parse("{\"type\":\"doc\",\"content\":[]}"),
            Specs = JsonDocument.Parse("{}"),
            Options = JsonDocument.Parse("[]"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }
}
