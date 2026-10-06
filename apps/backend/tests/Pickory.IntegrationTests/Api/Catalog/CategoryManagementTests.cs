using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.Identity.Entities;
using Xunit;

namespace Pickory.IntegrationTests.Api.Catalog;

// Kiểm tra toàn bộ luồng quản lý danh mục sản phẩm từ route công khai tới quản trị.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CategoryManagementTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;
    private string _adminCookie = null!;
    private string _salesCookie = null!;

    public CategoryManagementTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        // Tắt tự động nạp cookie để bài test kiểm soát phiên người dùng chính xác.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        // Khởi tạo role hệ thống để cấp quyền cho tài khoản kiểm thử.
        await IdentityTestData.SeedRolesAsync(_database);

        var (_, adminToken) = await IdentityTestData.CreateStaffSessionAsync(_database, DefaultRoles.AdminCode, "cat-admin");
        _adminCookie = $"pk_session={adminToken}";

        var (_, salesToken) = await IdentityTestData.CreateStaffSessionAsync(_database, DefaultRoles.SalesCode, "cat-sales");
        _salesCookie = $"pk_session={salesToken}";
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    // Khách vãng lai xem được cây danh mục tối đa 2 cấp kèm thuộc tính filterable được kế thừa.
    public async Task GetCategories_ReturnsTwoLevelTreeWithInheritedFilterableAttributes()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var root = await CreateCategoryInDbAsync($"Gốc {suffix}", null,
        [
            new AttributeDefinition { Key = "brand_origin", Label = "Xuất xứ", Group = "Chung", Type = "text", Required = false, Filterable = true },
            new AttributeDefinition { Key = "internal_code", Label = "Mã nội bộ", Group = "Chung", Type = "text", Required = false, Filterable = false }
        ]);

        var child = await CreateCategoryInDbAsync($"Con {suffix}", root.Id,
        [
            new AttributeDefinition { Key = "ram_gb", Label = "RAM", Group = "Cấu hình", Type = "number", Unit = "GB", Required = false, Filterable = true }
        ]);

        var request = ActorHttp.CreateRequest(HttpMethod.Get, "/api/categories");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var items = json.RootElement.GetProperty("data").EnumerateArray().ToList();
        var rootItem = items.First(item => item.GetProperty("id").GetString() == root.Id);

        // Danh mục cha chỉ giữ thuộc tính filterable của chính nó.
        var rootAttrs = rootItem.GetProperty("attributes").EnumerateArray().ToList();
        Assert.Single(rootAttrs);
        Assert.Equal("brand_origin", rootAttrs[0].GetProperty("key").GetString());

        // Danh mục con có thuộc tính kế thừa từ cha và thuộc tính riêng của con.
        var childItem = rootItem.GetProperty("children").EnumerateArray().First(item => item.GetProperty("id").GetString() == child.Id);
        var childAttrs = childItem.GetProperty("attributes").EnumerateArray().ToList();
        Assert.Equal(2, childAttrs.Count);
        Assert.Equal("brand_origin", childAttrs[0].GetProperty("key").GetString());
        Assert.Equal("ram_gb", childAttrs[1].GetProperty("key").GetString());
    }

    [Fact]
    // Tạo danh mục cấp 3 bị từ chối vì quy tắc nghiệp vụ chỉ cho phép cây 2 cấp.
    public async Task CreateCategory_LevelThree_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var root = await CreateCategoryInDbAsync($"Gốc {suffix}", null, []);
        var child = await CreateCategoryInDbAsync($"Con {suffix}", root.Id, []);

        var payload = new
        {
            name = $"Cháu {suffix}",
            parentId = child.Id
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/categories", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("CATEGORY_LEVEL_EXCEEDED", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Danh mục đã có sản phẩm thì được phép thêm thuộc tính tùy chọn và sửa nhãn thuộc tính.
    public async Task UpdateCategory_AddOptionalAttributeAndEditLabel_Succeeds()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Laptop {suffix}", null,
        [
            new AttributeDefinition { Key = "ram_gb", Label = "RAM Cũ", Group = "Cấu hình", Type = "number", Required = false, Filterable = true }
        ]);

        await CreateProductInDbAsync(category.Id, new { ram_gb = 16 });

        var payload = new
        {
            attributes = new[]
            {
                new { key = "ram_gb", label = "RAM Mới", group = "Cấu hình", type = "number", required = false, filterable = true },
                new { key = "color", label = "Màu sắc", group = "Thiết kế", type = "text", required = false, filterable = true }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{category.Id}", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var attrs = json.RootElement.GetProperty("data").GetProperty("attributes").EnumerateArray().ToList();
        Assert.Equal(2, attrs.Count);
        Assert.Equal("RAM Mới", attrs[0].GetProperty("label").GetString());
        Assert.Equal("color", attrs[1].GetProperty("key").GetString());
    }

    [Fact]
    // Thêm thuộc tính bắt buộc khi danh mục đã có sản phẩm bị từ chối để tránh dữ liệu hỏng.
    public async Task UpdateCategory_AddRequiredAttribute_WhenProductsExist_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Máy tính {suffix}", null,
        [
            new AttributeDefinition { Key = "ram_gb", Label = "RAM", Group = "Cấu hình", Type = "number", Required = false, Filterable = true }
        ]);

        await CreateProductInDbAsync(category.Id, new { ram_gb = 16 });

        var payload = new
        {
            attributes = new[]
            {
                new { key = "ram_gb", label = "RAM", group = "Cấu hình", type = "number", required = false, filterable = true },
                new { key = "storage_gb", label = "Ổ cứng", group = "Cấu hình", type = "number", required = true, filterable = true }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{category.Id}", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("INVALID_CATEGORY_ATTRIBUTES", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Xóa thuộc tính đang có sản phẩm sử dụng thì trả lỗi xung đột dữ liệu 409.
    public async Task UpdateCategory_DeleteInUseAttribute_Returns409()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Tai nghe {suffix}", null,
        [
            new AttributeDefinition { Key = "bluetooth", Label = "Bluetooth", Group = "Kết nối", Type = "boolean", Required = false, Filterable = true }
        ]);

        await CreateProductInDbAsync(category.Id, new { bluetooth = true });

        // Gửi danh sách rỗng để yêu cầu xóa thuộc tính bluetooth đang được dùng.
        var payload = new { attributes = Array.Empty<object>() };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{category.Id}", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("ATTRIBUTE_IN_USE", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Đổi kiểu dữ liệu của thuộc tính đang có sản phẩm dùng bị từ chối với mã lỗi 409.
    public async Task UpdateCategory_ChangeAttributeType_WhenInUse_Returns409()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Màn hình {suffix}", null,
        [
            new AttributeDefinition { Key = "refresh_rate", Label = "Tần số quét", Group = "Hiển thị", Type = "number", Required = false, Filterable = true }
        ]);

        await CreateProductInDbAsync(category.Id, new { refresh_rate = 144 });

        var payload = new
        {
            attributes = new[]
            {
                new { key = "refresh_rate", label = "Tần số quét", group = "Hiển thị", type = "text", required = false, filterable = true }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{category.Id}", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("ATTRIBUTE_IN_USE", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Xóa thuộc tính không dùng thành công khi danh mục đã có sản phẩm.
    public async Task UpdateCategory_DeleteUnusedAttribute_WhenProductsExist_Succeeds()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Bàn phím {suffix}", null,
        [
            new AttributeDefinition { Key = "switch_type", Label = "Loại switch", Group = "Phím", Type = "text", Required = false, Filterable = true },
            new AttributeDefinition { Key = "unused_attr", Label = "Chưa dùng", Group = "Phụ", Type = "text", Required = false, Filterable = false }
        ]);

        // Sản phẩm chỉ dùng switch_type, không dùng unused_attr.
        await CreateProductInDbAsync(category.Id, new { switch_type = "Linear" });

        var payload = new
        {
            attributes = new[]
            {
                new { key = "switch_type", label = "Loại switch", group = "Phím", type = "text", required = false, filterable = true }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{category.Id}", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var attrs = json.RootElement.GetProperty("data").GetProperty("attributes").EnumerateArray().ToList();
        Assert.Single(attrs);
        Assert.Equal("switch_type", attrs[0].GetProperty("key").GetString());
    }

    [Fact]
    // Đổi khóa của thuộc tính đang có sản phẩm dùng bị coi là xóa thuộc tính cũ và trả 409.
    public async Task UpdateCategory_ChangeKey_WhenInUse_Returns409()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Chuột {suffix}", null,
        [
            new AttributeDefinition { Key = "dpi", Label = "DPI", Group = "Cảm biến", Type = "number", Required = false, Filterable = true }
        ]);

        await CreateProductInDbAsync(category.Id, new { dpi = 16000 });

        var payload = new
        {
            attributes = new[]
            {
                new { key = "max_dpi", label = "DPI", group = "Cảm biến", type = "number", required = false, filterable = true }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{category.Id}", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("ATTRIBUTE_IN_USE", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Cập nhật thuộc tính của danh mục con trùng khóa với danh mục cha phải bị từ chối.
    public async Task UpdateCategory_ChildDuplicateParentKey_WithoutParentIdInPayload_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var parent = await CreateCategoryInDbAsync($"Cha {suffix}", null,
        [
            new AttributeDefinition { Key = "origin", Label = "Xuất xứ", Group = "Chung", Type = "text", Required = false, Filterable = true }
        ]);
        var child = await CreateCategoryInDbAsync($"Con {suffix}", parent.Id,
        [
            new AttributeDefinition { Key = "local_spec", Label = "Riêng", Group = "Chung", Type = "text", Required = false, Filterable = true }
        ]);

        var payload = new
        {
            attributes = new[]
            {
                new { key = "origin", label = "Trùng cha", group = "Chung", type = "text", required = false, filterable = true }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{child.Id}", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("INVALID_CATEGORY_ATTRIBUTES", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Đổi thuộc tính sang bắt buộc khi có sản phẩm bị từ chối để bảo vệ dữ liệu.
    public async Task UpdateCategory_ChangeRequiredFromFalseToTrue_WhenProductsExist_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Loa {suffix}", null,
        [
            new AttributeDefinition { Key = "power_w", Label = "Công suất", Group = "Âm thanh", Type = "number", Required = false, Filterable = true }
        ]);

        await CreateProductInDbAsync(category.Id, new { power_w = 40 });

        var payload = new
        {
            attributes = new[]
            {
                new { key = "power_w", label = "Công suất", group = "Âm thanh", type = "number", required = true, filterable = true }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{category.Id}", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("INVALID_CATEGORY_ATTRIBUTES", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Bỏ giá trị enum đang có sản phẩm dùng trả mã lỗi 409.
    public async Task UpdateCategory_RemoveEnumOption_WhenInUse_Returns409()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Đồng hồ {suffix}", null,
        [
            new AttributeDefinition { Key = "color", Label = "Màu", Group = "Thiết kế", Type = "enum", Options = ["black", "white"], Required = false, Filterable = true }
        ]);

        await CreateProductInDbAsync(category.Id, new { color = "white" });

        var payload = new
        {
            attributes = new[]
            {
                new { key = "color", label = "Màu", group = "Thiết kế", type = "enum", options = new[] { "black" }, required = false, filterable = true }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{category.Id}", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("ATTRIBUTE_IN_USE", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Bỏ giá trị enum không có sản phẩm nào dùng thì thực hiện thành công.
    public async Task UpdateCategory_RemoveEnumOption_WhenNotInUse_Succeeds()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Đồng hồ {suffix}", null,
        [
            new AttributeDefinition { Key = "color", Label = "Màu", Group = "Thiết kế", Type = "enum", Options = ["black", "white"], Required = false, Filterable = true }
        ]);

        // Sản phẩm dùng black, không dùng white.
        await CreateProductInDbAsync(category.Id, new { color = "black" });

        var payload = new
        {
            attributes = new[]
            {
                new { key = "color", label = "Màu", group = "Thiết kế", type = "enum", options = new[] { "black" }, required = false, filterable = true }
            }
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{category.Id}", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    // Tạo danh mục con dưới danh mục có sản phẩm bị từ chối vì quy tắc lá.
    public async Task CreateCategory_UnderParentWithProducts_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var root = await CreateCategoryInDbAsync($"Gốc {suffix}", null, []);
        await CreateProductInDbAsync(root.Id, new { });

        var payload = new
        {
            name = $"Con {suffix}",
            parentId = root.Id
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/categories", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("CATEGORY_HAS_PRODUCTS", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Chuyển danh mục làm con của danh mục đang có sản phẩm bị từ chối.
    public async Task UpdateCategory_MoveUnderParentWithProducts_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var rootA = await CreateCategoryInDbAsync($"Gốc A {suffix}", null, []);
        var rootB = await CreateCategoryInDbAsync($"Gốc B {suffix}", null, []);
        await CreateProductInDbAsync(rootA.Id, new { });

        var payload = new
        {
            parentId = rootA.Id
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{rootB.Id}", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("CATEGORY_HAS_PRODUCTS", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Chuyển danh mục đang có sản phẩm sang danh mục cha khác bị từ chối.
    public async Task UpdateCategory_MoveCategoryWithProducts_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var root = await CreateCategoryInDbAsync($"Gốc {suffix}", null, []);
        var child = await CreateCategoryInDbAsync($"Con {suffix}", root.Id, []);
        await CreateProductInDbAsync(child.Id, new { });

        var payload = new
        {
            parentId = (string?)null
        };

        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{child.Id}", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("CATEGORY_HAS_PRODUCTS", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Đổi tên danh mục không làm thay đổi slug ban đầu để giữ tính liên tục của URL chia sẻ.
    public async Task UpdateCategory_ChangeName_PreservesOriginalSlug()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await CreateCategoryInDbAsync($"Tên Gốc {suffix}", null, []);
        var originalSlug = category.Slug;

        var payload = new { name = $"Tên Mới {suffix}" };
        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{category.Id}", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal($"Tên Mới {suffix}", data.GetProperty("name").GetString());
        Assert.Equal(originalSlug, data.GetProperty("slug").GetString());
    }

    [Fact]
    // Chuyển danh mục sang cha mới có khóa thuộc tính trùng nhau phải bị từ chối dù không gửi attributes.
    public async Task UpdateCategory_MoveToParentWithDuplicateKey_WithoutAttributesInPayload_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var parent = await CreateCategoryInDbAsync($"Cha {suffix}", null,
        [
            new AttributeDefinition { Key = "shared_key", Label = "Khóa chung", Group = "Chung", Type = "text", Required = false, Filterable = true }
        ]);

        var child = await CreateCategoryInDbAsync($"Độc lập {suffix}", null,
        [
            new AttributeDefinition { Key = "shared_key", Label = "Khóa chung", Group = "Chung", Type = "text", Required = false, Filterable = true }
        ]);

        var payload = new { parentId = parent.Id };
        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{child.Id}", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("INVALID_CATEGORY_ATTRIBUTES", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Chuyển danh mục con không có sản phẩm thành danh mục gốc bằng parentId null thành công.
    public async Task UpdateCategory_MoveChildToRoot_WhenNoProducts_Succeeds()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var root = await CreateCategoryInDbAsync($"Gốc Ban Đầu {suffix}", null, []);
        var child = await CreateCategoryInDbAsync($"Con Ban Đầu {suffix}", root.Id, []);

        var payload = new { parentId = (string?)null };
        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{child.Id}", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Null(json.RootElement.GetProperty("data").GetProperty("parentId").GetString());

        // Kiểm tra qua route GET công khai thấy cả hai đều ở cấp gốc của cây.
        var getRes = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, "/api/categories"));
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        using var getJson = await ActorHttp.ReadJsonAsync(getRes);
        var items = getJson.RootElement.GetProperty("data").EnumerateArray().ToList();
        Assert.Contains(items, item => item.GetProperty("id").GetString() == child.Id && item.GetProperty("parentId").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    // Cập nhật chỉ gửi tên danh mục thì liên kết danh mục cha hiện tại vẫn được giữ nguyên.
    public async Task UpdateCategory_PatchOnlyName_PreservesParent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var root = await CreateCategoryInDbAsync($"Gốc Giữ {suffix}", null, []);
        var child = await CreateCategoryInDbAsync($"Con Giữ {suffix}", root.Id, []);

        var payload = new { name = $"Tên Con Mới {suffix}" };
        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{child.Id}", payload, _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal(root.Id, json.RootElement.GetProperty("data").GetProperty("parentId").GetString());
    }

    [Fact]
    // Xóa danh mục còn danh mục con hoặc còn sản phẩm phải bị từ chối với thông báo rõ ràng.
    public async Task DeleteCategory_WithChildrenOrProducts_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var parent = await CreateCategoryInDbAsync($"Cha {suffix}", null, []);
        var child = await CreateCategoryInDbAsync($"Con {suffix}", parent.Id, []);

        // Xóa danh mục cha khi còn con thì nhận lỗi 400 CATEGORY_HAS_CHILDREN.
        var deleteParentRequest = ActorHttp.CreateJsonRequest(HttpMethod.Delete, $"/api/admin/categories/{parent.Id}", null, _adminCookie);
        var deleteParentResponse = await _client.SendAsync(deleteParentRequest);
        Assert.Equal(HttpStatusCode.BadRequest, deleteParentResponse.StatusCode);
        using var parentJson = await ActorHttp.ReadJsonAsync(deleteParentResponse);
        Assert.Equal("CATEGORY_HAS_CHILDREN", parentJson.RootElement.GetProperty("error").GetProperty("code").GetString());

        // Gắn sản phẩm vào danh mục con rồi thử xóa danh mục con.
        await CreateProductInDbAsync(child.Id, new { });
        var deleteChildRequest = ActorHttp.CreateJsonRequest(HttpMethod.Delete, $"/api/admin/categories/{child.Id}", null, _adminCookie);
        var deleteChildResponse = await _client.SendAsync(deleteChildRequest);
        Assert.Equal(HttpStatusCode.BadRequest, deleteChildResponse.StatusCode);
        using var childJson = await ActorHttp.ReadJsonAsync(deleteChildResponse);
        Assert.Equal("CATEGORY_HAS_PRODUCTS", childJson.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Nhân viên sales chỉ có quyền đọc nên được gọi GET và bị cấm khi gọi POST.
    public async Task Sales_CanGetCategories_CannotCreateCategory()
    {
        // Gọi GET thành công với mã 200.
        var getRequest = ActorHttp.CreateJsonRequest(HttpMethod.Get, "/api/categories", null, _salesCookie);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // Gọi POST tạo mới nhận mã 403 do thiếu quyền products:write.
        var postRequest = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/categories", new { name = "Thử nghiệm" }, _salesCookie);
        var postResponse = await _client.SendAsync(postRequest);
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);
    }

    [Fact]
    // Cập nhật hoặc xóa danh mục không tồn tại thì API trả mã lỗi 404.
    public async Task UpdateOrDelete_NonExistentCategory_Returns404()
    {
        var nonExistentId = Guid.NewGuid().ToString("N");

        var patchRequest = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{nonExistentId}", new { name = "Tên mới" }, _adminCookie);
        var patchResponse = await _client.SendAsync(patchRequest);
        Assert.Equal(HttpStatusCode.NotFound, patchResponse.StatusCode);

        var deleteRequest = ActorHttp.CreateJsonRequest(HttpMethod.Delete, $"/api/admin/categories/{nonExistentId}", null, _adminCookie);
        var deleteResponse = await _client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
    }

    [Fact]
    // Trùng tên danh mục thì slug tự động thêm hậu tố số để đảm bảo tính duy nhất.
    public async Task CreateCategory_DuplicateName_GeneratesNumberedSlug()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"Chuột quang {suffix}";

        var firstReq = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/categories", new { name }, _adminCookie);
        var firstRes = await _client.SendAsync(firstReq);
        Assert.Equal(HttpStatusCode.Created, firstRes.StatusCode);
        using var firstJson = await ActorHttp.ReadJsonAsync(firstRes);
        var firstSlug = firstJson.RootElement.GetProperty("data").GetProperty("slug").GetString()!;

        var secondReq = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/categories", new { name }, _adminCookie);
        var secondRes = await _client.SendAsync(secondReq);
        Assert.Equal(HttpStatusCode.Created, secondRes.StatusCode);
        using var secondJson = await ActorHttp.ReadJsonAsync(secondRes);
        var secondSlug = secondJson.RootElement.GetProperty("data").GetProperty("slug").GetString()!;

        Assert.Equal($"{firstSlug}-2", secondSlug);
    }

    [Fact]
    // Mọi thao tác ghi danh mục đều tạo bản ghi nhật ký kiểm toán trong cơ sở dữ liệu.
    public async Task CategoryOperations_RecordAuditLogs()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var createReq = ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/categories", new { name = "Nhật ký " + suffix }, _adminCookie);
        var createRes = await _client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        using var createJson = await ActorHttp.ReadJsonAsync(createRes);
        var categoryId = createJson.RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var patchReq = ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/categories/{categoryId}", new { name = "Sửa nhật ký " + suffix }, _adminCookie);
        var patchRes = await _client.SendAsync(patchReq);
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        var deleteReq = ActorHttp.CreateJsonRequest(HttpMethod.Delete, $"/api/admin/categories/{categoryId}", null, _adminCookie);
        var deleteRes = await _client.SendAsync(deleteReq);
        Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);

        // Kiểm tra ba dòng nhật ký tương ứng trong cơ sở dữ liệu identity.
        await using var identity = _database.CreateIdentityContext();
        var logs = await identity.AuditLogs.Where(log => log.TargetId == categoryId).ToListAsync();
        Assert.Equal(3, logs.Count);
        Assert.Contains(logs, log => log.Action == "catalog.category.create");
        Assert.Contains(logs, log => log.Action == "catalog.category.update");
        Assert.Contains(logs, log => log.Action == "catalog.category.delete");
    }

    [Fact]
    // Quản trị viên và nhân viên bán hàng xem được danh sách phẳng mọi danh mục kèm thuộc tính riêng.
    public async Task GetAdminCategories_Sales_ReturnsFlatListWithOwnNonFilterableAttributes()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var rootB = await CreateCategoryInDbAsync($"B Gốc Admin {suffix}", null,
        [
            new AttributeDefinition { Key = "origin", Label = "Xuất xứ", Group = "Chung", Type = "text", Required = false, Filterable = true },
            new AttributeDefinition { Key = "internal_sku_note", Label = "Ghi chú nội bộ", Group = "Chung", Type = "text", Required = false, Filterable = false }
        ]);

        var rootA = await CreateCategoryInDbAsync($"A Gốc Admin {suffix}", null, []);

        var child = await CreateCategoryInDbAsync($"Con Admin {suffix}", rootB.Id,
        [
            new AttributeDefinition { Key = "warranty_type", Label = "Loại bảo hành", Group = "Bảo hành", Type = "text", Required = false, Filterable = false }
        ]);
        var salesReq = ActorHttp.CreateRequest(HttpMethod.Get, "/api/admin/categories", cookieHeader: _salesCookie);
        var salesRes = await _client.SendAsync(salesReq);

        Assert.Equal(HttpStatusCode.OK, salesRes.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(salesRes);
        var items = json.RootElement.GetProperty("data").EnumerateArray().ToList();

        var rootItem = items.First(item => item.GetProperty("id").GetString() == rootB.Id);
        var rootAttrs = rootItem.GetProperty("attributes").EnumerateArray().ToList();
        // Thuộc tính không lọc (filterable = false) vẫn được giữ lại đầy đủ.
        Assert.Contains(rootAttrs, attr => attr.GetProperty("key").GetString() == "internal_sku_note" && !attr.GetProperty("filterable").GetBoolean());

        // Kiểm tra thứ tự sắp xếp theo Position rồi đến Name tăng dần.
        var indexA = items.FindIndex(item => item.GetProperty("id").GetString() == rootA.Id);
        var indexB = items.FindIndex(item => item.GetProperty("id").GetString() == rootB.Id);
        Assert.True(indexA < indexB);
        var childItem = items.First(item => item.GetProperty("id").GetString() == child.Id);
        var childAttrs = childItem.GetProperty("attributes").EnumerateArray().ToList();
        // Danh mục con chỉ giữ thuộc tính riêng, không gộp thuộc tính của danh mục cha.
        Assert.Single(childAttrs);
        Assert.Equal("warranty_type", childAttrs[0].GetProperty("key").GetString());
        Assert.False(childAttrs[0].GetProperty("filterable").GetBoolean());
    }

    [Fact]
    // Khách vãng lai nhận 401 và khách hàng nhận 403 khi gọi route quản trị danh mục.
    public async Task GetAdminCategories_UnauthorizedAndForbidden_ReturnsExpectedStatus()
    {
        var guestReq = ActorHttp.CreateRequest(HttpMethod.Get, "/api/admin/categories", cookieHeader: null);
        var guestRes = await _client.SendAsync(guestReq);
        Assert.Equal(HttpStatusCode.Unauthorized, guestRes.StatusCode);

        var customer = await IdentityTestData.CreateUserAsync(_database, $"cust-{Guid.NewGuid():N}@pickory.local", UserKind.Customer, DefaultRoles.CustomerCode, emailVerified: true);
        var customerToken = await IdentityTestData.CreateSessionAsync(_database, customer.Id);
        var customerReq = ActorHttp.CreateRequest(HttpMethod.Get, "/api/admin/categories", cookieHeader: $"pk_session={customerToken}");
        var customerRes = await _client.SendAsync(customerReq);
        Assert.Equal(HttpStatusCode.Forbidden, customerRes.StatusCode);
    }

    private async Task<Category> CreateCategoryInDbAsync(string name, string? parentId, IReadOnlyList<AttributeDefinition> attributes)
    {
        await using var db = _database.CreateCatalogContext();
        var category = new Category
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Slug = $"test-{Guid.NewGuid():N}",
            ParentId = parentId,
            Position = 0,
            Attributes = JsonDocument.Parse(JsonSerializer.Serialize(attributes, AttributeDefinition.JsonOptions)),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private async Task<Product> CreateProductInDbAsync(string categoryId, object specs)
    {
        await using var db = _database.CreateCatalogContext();
        var brand = new Brand
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Thương hiệu thử",
            Slug = $"brand-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Brands.Add(brand);

        var product = new Product
        {
            Id = Guid.NewGuid().ToString("N"),
            CategoryId = categoryId,
            BrandId = brand.Id,
            Name = "Sản phẩm thử",
            Slug = $"product-{Guid.NewGuid():N}",
            ShortDesc = "Mô tả ngắn",
            Description = JsonDocument.Parse("{\"type\":\"doc\",\"content\":[]}"),
            Specs = JsonDocument.Parse(JsonSerializer.Serialize(specs)),
            Options = JsonDocument.Parse("[]"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }
}
