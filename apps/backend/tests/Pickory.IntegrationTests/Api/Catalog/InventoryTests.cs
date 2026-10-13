using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Contracts.Events;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.IntegrationTests.Api.Catalog;

// CAT-08: điều chỉnh tồn kho bằng tay, lịch sử movement và danh sách SKU sắp hết hàng.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class InventoryTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string AdjustAuditAction = "catalog.sku.stock_adjust";

    private readonly IntegrationDatabaseFixture _database;
    private readonly PickoryWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly StockTestData _data;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];
    private string _adminCookie = null!;
    private string _salesCookie = null!;
    private string _adminUserId = null!;

    public InventoryTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _data = new StockTestData(database, _suffix);
    }

    public async Task InitializeAsync()
    {
        await IdentityTestData.SeedRolesAsync(_database);
        var (admin, adminToken) = await IdentityTestData.CreateStaffSessionAsync(_database, DefaultRoles.AdminCode, $"inv-admin-{_suffix}");
        _adminCookie = $"pk_session={adminToken}";
        _adminUserId = admin.Id;
        var (_, salesToken) = await IdentityTestData.CreateStaffSessionAsync(_database, DefaultRoles.SalesCode, $"inv-sales-{_suffix}");
        _salesCookie = $"pk_session={salesToken}";
        await _data.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await StockInvariant.AssertHoldsAsync(_database);
        await _data.CleanupAsync();
    }

    private async Task<HttpResponseMessage> AdjustAsync(string skuId, object body, string? cookie = null) =>
        await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, $"/api/admin/skus/{skuId}/movements", body, cookie ?? _adminCookie));

    private async Task<HttpResponseMessage> GetAsync(string path, string? cookie = null) =>
        await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, path, cookie ?? _adminCookie));

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Mong đợi {status}, nhận {response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private async Task<int> AdjustAuditCountAsync(string skuId)
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.AuditLogs.CountAsync(log => log.Action == AdjustAuditAction && log.TargetId == skuId);
    }

    private IReadOnlyList<StockChangedEvent> EventsOf(string skuId) =>
        _factory.DomainEvents.OfType<StockChangedEvent>().Where(item => item.SkuId == skuId).ToList();

    // Lỗi điều chỉnh không được để lại dấu vết: tồn kho cũ, không movement mới, không audit, không sự kiện.
    private async Task AssertNothingChangedAsync(string skuId, int expectedStock, int expectedMovementCount)
    {
        Assert.Equal(expectedStock, await _data.StockOfAsync(skuId));
        Assert.Equal(expectedMovementCount, (await _data.MovementsOfAsync(skuId)).Count);
        Assert.Equal(0, await AdjustAuditCountAsync(skuId));
        Assert.Empty(EventsOf(skuId));
    }

    [Fact]
    public async Task Adjust_Sales_Returns403AndChangesNothing()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(3));

        var response = await AdjustAsync(product.SkuIds[0], new { delta = 1, reason = "Nhập thêm" }, _salesCookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNothingChangedAsync(product.SkuIds[0], 3, 1);
    }

    [Fact]
    public async Task Adjust_UnknownSku_Returns404()
    {
        var response = await AdjustAsync(Guid.NewGuid().ToString("N"), new { delta = 1, reason = "Nhập thêm" });

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "SKU_NOT_FOUND");
    }

    public static IEnumerable<object[]> InvalidBodies() =>
    [
        [new { delta = 0, reason = "Lý do" }],
        [new { delta = 1, reason = "" }],
        [new { delta = 1, reason = "   " }],
        [new { delta = 1 }],
        [new { delta = 1, reason = new string('a', 501) }],
        [new { delta = -1, reason = "Lý do", type = "return" }],
        [new { delta = 0, reason = "Lý do", type = "return" }],
        [new { delta = 1, reason = "Lý do", type = "order_reserve" }],
        [new { reason = "Lý do" }],
    ];

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    // Mọi body sai bị từ chối trước khi đụng tới tồn kho.
    public async Task Adjust_InvalidBody_Returns400AndChangesNothing(object body)
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(3));

        var response = await AdjustAsync(product.SkuIds[0], body);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await AssertNothingChangedAsync(product.SkuIds[0], 3, 1);
    }

    [Fact]
    // Điều chỉnh làm tồn kho âm bị từ chối bằng 409, không ghi movement, audit hay sự kiện.
    public async Task Adjust_BelowZero_Returns409AndChangesNothing()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(3));

        var response = await AdjustAsync(product.SkuIds[0], new { delta = -4, reason = "Kiểm kê lệch" });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "INSUFFICIENT_STOCK");
        await AssertNothingChangedAsync(product.SkuIds[0], 3, 1);
    }

    [Fact]
    // Trừ đúng bằng số tồn là hợp lệ. Tồn kho về 0 và sản phẩm hết hàng.
    public async Task Adjust_ExactlyToZero_Succeeds()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(3));

        var response = await AdjustAsync(product.SkuIds[0], new { delta = -3, reason = "Hàng hỏng" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(0, await _data.StockOfAsync(product.SkuIds[0]));
        Assert.False((await _data.DisplayOfAsync(product.ProductId)).InStock);
    }

    [Fact]
    // Thành công: phản hồi có tồn kho mới và movement vừa ghi. Đúng 1 audit, 1 sự kiện, và `inStock` cập nhật.
    public async Task Adjust_Manual_RecordsMovementAuditAndEvent()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(0));
        var skuId = product.SkuIds[0];
        Assert.False((await _data.DisplayOfAsync(product.ProductId)).InStock);

        var response = await AdjustAsync(skuId, new { delta = 2, reason = "  Nhập lô mới  " });

        // Điều chỉnh tạo một dòng biến động mới nên route trả 201.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(skuId, data.GetProperty("skuId").GetString());
        Assert.Equal(2, data.GetProperty("stock").GetInt32());
        var movementJson = data.GetProperty("movement");
        Assert.Equal(2, movementJson.GetProperty("delta").GetInt32());
        Assert.Equal("manual", movementJson.GetProperty("type").GetString());
        Assert.Equal("Nhập lô mới", movementJson.GetProperty("reason").GetString());
        Assert.Equal(_adminUserId, movementJson.GetProperty("actorId").GetString());

        var movement = Assert.Single(await _data.MovementsOfAsync(skuId));
        Assert.Equal(movementJson.GetProperty("id").GetString(), movement.Id);
        Assert.Equal(InventoryMovementType.Manual, movement.Type);
        Assert.Null(movement.OrderId);
        Assert.True((await _data.DisplayOfAsync(product.ProductId)).InStock);

        await using var identity = _database.CreateIdentityContext();
        var audit = await identity.AuditLogs.AsNoTracking()
            .Where(log => log.Action == AdjustAuditAction && log.TargetId == skuId).SingleAsync();
        Assert.Equal("sku", audit.TargetType);
        Assert.Equal(_adminUserId, audit.ActorId);
        Assert.Equal(0, audit.Before!.RootElement.GetProperty("stock").GetInt32());
        var after = audit.After!.RootElement;
        Assert.Equal(2, after.GetProperty("stock").GetInt32());
        Assert.Equal(2, after.GetProperty("delta").GetInt32());
        Assert.Equal("manual", after.GetProperty("type").GetString());
        Assert.Equal("Nhập lô mới", after.GetProperty("reason").GetString());

        Assert.Equal(new StockChangedEvent(skuId, 2, 0), Assert.Single(EventsOf(skuId)));
    }

    [Fact]
    // Dòng biến động vừa tạo bằng `POST` hiện ngay trong `GET` cùng đường dẫn.
    public async Task Adjust_NewMovementAppearsInMovementList()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(1));
        var skuId = product.SkuIds[0];

        var created = await AdjustAsync(skuId, new { delta = 5, reason = "Nhập lô thử danh sách" });
        var list = await GetAsync($"/api/admin/skus/{skuId}/movements");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var createdJson = await ActorHttp.ReadJsonAsync(created);
        var createdId = createdJson.RootElement.GetProperty("data").GetProperty("movement").GetProperty("id").GetString();
        using var listJson = await ActorHttp.ReadJsonAsync(list);
        var listedIds = listJson.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString());
        Assert.Contains(createdId, listedIds);
    }

    [Fact]
    // Hai method trên cùng đường dẫn có quyền khác nhau. Role chỉ có `products:read` xem được lịch sử nhưng không điều chỉnh được.
    public async Task MovementsRoute_ReadOnlyRoleCanGetButCannotPost()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(2));
        var skuId = product.SkuIds[0];
        var readOnlyRole = await IdentityTestData.CreateRoleAsync(_database, [Permissions.ProductsRead]);
        var (_, readOnlyToken) = await IdentityTestData.CreateStaffSessionAsync(_database, readOnlyRole.Code, $"inv-readonly-{_suffix}");
        var readOnlyCookie = $"pk_session={readOnlyToken}";

        var getResponse = await GetAsync($"/api/admin/skus/{skuId}/movements", readOnlyCookie);
        var postResponse = await AdjustAsync(skuId, new { delta = 1, reason = "Không được phép" }, readOnlyCookie);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        await AssertErrorAsync(postResponse, HttpStatusCode.Forbidden, "FORBIDDEN");
        await AssertNothingChangedAsync(skuId, 2, 1);
    }

    [Fact]
    // Route cũ đặt theo hành động không còn đăng ký. Test tách đường dẫn để lệnh grep route cũ không báo nhầm.
    public async Task OldAdjustRoute_IsNoLongerRegistered()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(2));
        var skuId = product.SkuIds[0];

        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, $"/api/admin/skus/{skuId}/" + "adjust" + "-stock", new { delta = 1, reason = "Route cũ" }, _adminCookie));

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        await AssertNothingChangedAsync(skuId, 2, 1);
    }

    [Fact]
    public async Task Adjust_Return_StoresReturnType()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(1));

        var response = await AdjustAsync(product.SkuIds[0], new { delta = 1, reason = "Khách trả hàng", type = "return" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var movements = await _data.MovementsOfAsync(product.SkuIds[0]);
        Assert.Equal(InventoryMovementType.Return, movements[^1].Type);
        Assert.Equal(2, await _data.StockOfAsync(product.SkuIds[0]));
    }

    private async Task SeedMovementAsync(string skuId, int delta, DateTime createdAt)
    {
        await using var context = _database.CreateCatalogContext();
        context.InventoryMovements.Add(new InventoryMovement
        {
            Id = Guid.NewGuid().ToString("N"), SkuId = skuId, Delta = delta, Type = InventoryMovementType.Manual,
            Reason = $"Movement {delta}", CreatedAt = createdAt,
        });
        // Tồn kho đổi cùng movement để bất biến tồn kho vẫn đúng.
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE skus SET stock = stock + {delta} WHERE id = {skuId}");
        await context.SaveChangesAsync();
    }

    [Fact]
    // Lịch sử trả mới nhất trước, mỗi dòng có đủ trường, và phân trang theo `page` và `limit`.
    public async Task Movements_ReturnNewestFirstWithPaging()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(0));
        var skuId = product.SkuIds[0];
        var start = DateTime.UtcNow.AddHours(-1);
        await SeedMovementAsync(skuId, 1, start);
        await SeedMovementAsync(skuId, 2, start.AddMinutes(1));
        await SeedMovementAsync(skuId, 3, start.AddMinutes(2));

        var firstPage = await GetAsync($"/api/admin/skus/{skuId}/movements?page=1&limit=2");
        var secondPage = await GetAsync($"/api/admin/skus/{skuId}/movements?page=2&limit=2");

        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        using var firstJson = await ActorHttp.ReadJsonAsync(firstPage);
        var data = firstJson.RootElement.GetProperty("data");
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        Assert.Equal(2, data.GetProperty("limit").GetInt32());
        var items = data.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([3, 2], items.Select(item => item.GetProperty("delta").GetInt32()));
        var top = items[0];
        foreach (var field in new[] { "id", "delta", "type", "orderId", "actorId", "reason", "createdAt" })
        {
            Assert.True(top.TryGetProperty(field, out _), $"Thiếu trường {field}");
        }

        Assert.Equal("manual", top.GetProperty("type").GetString());
        using var secondJson = await ActorHttp.ReadJsonAsync(secondPage);
        var secondItems = secondJson.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([1], secondItems.Select(item => item.GetProperty("delta").GetInt32()));
    }

    [Fact]
    public async Task Movements_SalesCanRead()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(1));

        var response = await GetAsync($"/api/admin/skus/{product.SkuIds[0]}/movements", _salesCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Movements_UnknownSku_Returns404()
    {
        var response = await GetAsync($"/api/admin/skus/{Guid.NewGuid():N}/movements");

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "SKU_NOT_FOUND");
    }

    [Fact]
    public async Task Movements_InvalidLimit_Returns400()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(1));

        var response = await GetAsync($"/api/admin/skus/{product.SkuIds[0]}/movements?limit=0");

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    // Ngưỡng là 5: SKU còn 5 cái có trong danh sách, còn 6 cái thì không. SKU tắt bị loại dù hết hàng.
    public async Task LowStock_UsesThresholdOfFiveAndSkipsDisabledSkus()
    {
        var product = await _data.CreateProductAsync(
            new StockTestData.SkuSeed(5), new StockTestData.SkuSeed(6), new StockTestData.SkuSeed(0, Enabled: false),
            new StockTestData.SkuSeed(0), new StockTestData.SkuSeed(2));

        var response = await GetAsync("/api/admin/inventory/low-stock?limit=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var mine = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("productId").GetString() == product.ProductId).ToList();
        // Hết hàng (0), sắp hết (2), đúng ngưỡng (5). Sắp theo tồn tăng dần.
        Assert.Equal([0, 2, 5], mine.Select(item => item.GetProperty("stock").GetInt32()));
        Assert.DoesNotContain(mine, item => item.GetProperty("skuId").GetString() == product.SkuIds[1]);
        Assert.DoesNotContain(mine, item => item.GetProperty("skuId").GetString() == product.SkuIds[2]);
        var first = mine[0];
        Assert.Equal(product.SkuIds[3], first.GetProperty("skuId").GetString());
        Assert.Contains(_suffix, first.GetProperty("code").GetString());
        Assert.StartsWith("Sản phẩm tồn kho", first.GetProperty("productName").GetString());
        Assert.Equal("3", first.GetProperty("optionValues").GetProperty("i").GetString());
    }

    [Fact]
    // Cùng số tồn thì sắp theo mã SKU tăng dần.
    public async Task LowStock_SameStock_OrdersByCode()
    {
        var product = await _data.CreateProductAsync(
            new StockTestData.SkuSeed(1), new StockTestData.SkuSeed(1), new StockTestData.SkuSeed(1));

        var response = await GetAsync("/api/admin/inventory/low-stock?limit=100");

        using var json = await ActorHttp.ReadJsonAsync(response);
        var codes = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("productId").GetString() == product.ProductId)
            .Select(item => item.GetProperty("code").GetString()!).ToList();
        Assert.Equal(3, codes.Count);
        Assert.Equal(codes.Order(StringComparer.Ordinal), codes);
    }

    [Fact]
    public async Task LowStock_PagesTheList_AndSalesCanRead()
    {
        await _data.CreateProductAsync(new StockTestData.SkuSeed(1), new StockTestData.SkuSeed(2));

        var response = await GetAsync("/api/admin/inventory/low-stock?page=1&limit=1", _salesCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.True(data.GetProperty("total").GetInt32() >= 2);
        Assert.Equal(1, data.GetProperty("limit").GetInt32());
    }

    // Đổi tên sản phẩm seed để test tìm theo tên có dấu. Tên mặc định của seed không có dấu đặc biệt.
    private async Task RenameProductAsync(string productId, string name)
    {
        await using var context = _database.CreateCatalogContext();
        await context.Products.Where(product => product.Id == productId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(product => product.Name, name));
    }

    private async Task<List<string>> CodesOfAsync(IReadOnlyList<string> skuIds)
    {
        await using var context = _database.CreateCatalogContext();
        var rows = await context.Skus.AsNoTracking().Where(sku => skuIds.Contains(sku.Id))
            .Select(sku => new { sku.Id, sku.Code }).ToListAsync();
        return skuIds.Select(id => rows.Single(row => row.Id == id).Code).ToList();
    }

    private async Task<(List<JsonElement> Items, int Total)> ReadLowStockAsync(string query)
    {
        var response = await GetAsync($"/api/admin/inventory/low-stock?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        var items = data.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();
        return (items, data.GetProperty("total").GetInt32());
    }

    private static async Task AssertValidationErrorForFieldAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        Assert.Contains($"{field}:", error.GetProperty("message").GetString());
    }

    [Fact]
    // Tìm theo mã SKU. Chỉ SKU có mã khớp hiện ra, dù sản phẩm có nhiều SKU sắp hết hàng.
    public async Task LowStock_SearchBySkuCode_ReturnsOnlyMatchingSku()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(1), new StockTestData.SkuSeed(2));
        var codes = await CodesOfAsync(product.SkuIds);

        var (items, total) = await ReadLowStockAsync($"search={Uri.EscapeDataString(codes[0])}&limit=100");

        Assert.Equal([product.SkuIds[0]], items.Select(item => item.GetProperty("skuId").GetString()));
        Assert.Equal(1, total);
    }

    [Theory]
    [InlineData("dien thoai")]
    [InlineData("DIEN THOAI")]
    [InlineData("Điện thoại")]
    // Tên sản phẩm lưu có dấu. Từ khóa không dấu, viết hoa hoặc có dấu đều tìm được cả hai SKU sắp hết hàng.
    public async Task LowStock_SearchByProductName_IgnoresDiacriticsAndCase(string keyword)
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(1), new StockTestData.SkuSeed(2));
        await RenameProductAsync(product.ProductId, $"Điện thoại {_suffix}");

        var (items, total) = await ReadLowStockAsync($"search={Uri.EscapeDataString($"{keyword} {_suffix}")}&limit=100");

        Assert.Equal(
            product.SkuIds.Order(StringComparer.Ordinal),
            items.Select(item => item.GetProperty("skuId").GetString()!).Order(StringComparer.Ordinal));
        Assert.Equal(2, total);
    }

    [Fact]
    // `total` đếm cùng điều kiện tìm và vẫn chỉ tính SKU sắp hết hàng. SKU đủ hàng cùng sản phẩm không được tính.
    public async Task LowStock_TotalCountsSearchedLowStockRowsOnly()
    {
        var product = await _data.CreateProductAsync(
            new StockTestData.SkuSeed(1), new StockTestData.SkuSeed(2), new StockTestData.SkuSeed(3), new StockTestData.SkuSeed(50));
        await RenameProductAsync(product.ProductId, $"Tổng {_suffix}");

        var (items, total) = await ReadLowStockAsync($"search={Uri.EscapeDataString($"tong {_suffix}")}&limit=1");

        Assert.Single(items);
        Assert.Equal(3, total);
    }

    [Fact]
    // Sáu SKU cùng số tồn nằm ở ba trang liền nhau. Thứ tự phụ theo mã SKU giữ cho không lặp và không mất dòng.
    public async Task LowStock_SameStockAcrossPages_NoDuplicateAndNoMissingRow()
    {
        var product = await _data.CreateProductAsync(
            Enumerable.Range(0, 6).Select(_ => new StockTestData.SkuSeed(1)).ToArray());
        await RenameProductAsync(product.ProductId, $"Phân trang {_suffix}");
        var codes = await CodesOfAsync(product.SkuIds);

        // Mỗi `UPDATE` ghi bản dòng mới ở cuối bảng. Cập nhật theo mã SKU giảm dần đặt thứ tự lưu ngược với mã SKU.
        foreach (var code in codes.OrderByDescending(code => code, StringComparer.Ordinal))
        {
            await using var context = _database.CreateCatalogContext();
            await context.Skus.Where(sku => sku.Code == code)
                .ExecuteUpdateAsync(setters => setters.SetProperty(sku => sku.UpdatedAt, DateTime.UtcNow));
        }

        var collectedCodes = new List<string>();
        foreach (var pageNumber in new[] { 1, 2, 3 })
        {
            var (items, total) = await ReadLowStockAsync(
                $"search={Uri.EscapeDataString($"phan trang {_suffix}")}&limit=2&page={pageNumber}");
            Assert.Equal(6, total);
            collectedCodes.AddRange(items.Select(item => item.GetProperty("code").GetString()!));
        }

        Assert.Equal(codes.Order(StringComparer.Ordinal).ToList(), collectedCodes);
    }

    [Fact]
    // `%` trong từ khóa là ký tự thường. Từ khóa có `%` không khớp SKU nào, dù có SKU sắp hết hàng.
    public async Task LowStock_SearchPercentSign_IsNotWildcard()
    {
        await _data.CreateProductAsync(new StockTestData.SkuSeed(1));

        var (items, total) = await ReadLowStockAsync($"search={Uri.EscapeDataString($"%{_suffix}")}&limit=100");

        Assert.Empty(items);
        Assert.Equal(0, total);
    }

    [Fact]
    // `search` lặp lại và từ khóa 101 ký tự nhận 400 `VALIDATION_ERROR` cho trường `search`.
    public async Task LowStock_InvalidSearch_Returns400()
    {
        var repeated = await GetAsync("/api/admin/inventory/low-stock?search=a&search=b");
        var tooLong = await GetAsync($"/api/admin/inventory/low-stock?search={new string('a', 101)}");

        await AssertValidationErrorForFieldAsync(repeated, "search");
        await AssertValidationErrorForFieldAsync(tooLong, "search");
    }

    private async Task SeedTypedMovementAsync(string skuId, int delta, InventoryMovementType type, DateTime createdAt)
    {
        await using var context = _database.CreateCatalogContext();
        context.InventoryMovements.Add(new InventoryMovement
        {
            Id = Guid.NewGuid().ToString("N"), SkuId = skuId, Delta = delta, Type = type,
            Reason = $"Movement {type}", CreatedAt = createdAt,
        });
        // Tồn kho đổi cùng movement để bất biến tồn kho vẫn đúng.
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE skus SET stock = stock + {delta} WHERE id = {skuId}");
        await context.SaveChangesAsync();
    }

    private async Task<(List<JsonElement> Items, int Total)> ReadMovementsAsync(string skuId, string query)
    {
        var response = await GetAsync($"/api/admin/skus/{skuId}/movements?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        var items = data.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();
        return (items, data.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("manual", 2)]
    [InlineData("return", 1)]
    [InlineData("order_reserve", 0)]
    // Mỗi `type` chỉ trả biến động đúng loại, và `total` đếm cùng điều kiện lọc. SKU có hai `manual` và một `return`.
    public async Task Movements_TypeFilter_ReturnsOnlyThatTypeAndCountsSameCondition(string type, int expectedCount)
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(0));
        var skuId = product.SkuIds[0];
        var start = DateTime.UtcNow.AddHours(-1);
        await SeedTypedMovementAsync(skuId, 1, InventoryMovementType.Manual, start);
        await SeedTypedMovementAsync(skuId, 2, InventoryMovementType.Return, start.AddMinutes(1));
        await SeedTypedMovementAsync(skuId, 3, InventoryMovementType.Manual, start.AddMinutes(2));

        var (items, total) = await ReadMovementsAsync(skuId, $"type={type}&limit=1");

        Assert.Equal(expectedCount, total);
        Assert.Equal(Math.Min(expectedCount, 1), items.Count);
        Assert.All(items, item => Assert.Equal(type, item.GetProperty("type").GetString()));
    }

    [Fact]
    // Không gửi `type` thì trả mọi loại, như trước khi thêm bộ lọc. Giá trị trống được coi như không gửi.
    public async Task Movements_NoTypeOrBlankType_ReturnsEveryType()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(0));
        var skuId = product.SkuIds[0];
        var start = DateTime.UtcNow.AddHours(-1);
        await SeedTypedMovementAsync(skuId, 1, InventoryMovementType.Manual, start);
        await SeedTypedMovementAsync(skuId, 2, InventoryMovementType.Return, start.AddMinutes(1));

        var (_, withoutType) = await ReadMovementsAsync(skuId, "limit=100");
        var (_, blankType) = await ReadMovementsAsync(skuId, "type=&limit=100");

        Assert.Equal(2, withoutType);
        Assert.Equal(2, blankType);
    }

    [Theory]
    [InlineData("type=MANUAL")]
    [InlineData("type=0")]
    [InlineData("type=khong-biet")]
    [InlineData("type=manual&type=return")]
    // `type` lạ, viết hoa, chuỗi số và `type` lặp nhận 400 `VALIDATION_ERROR` cho trường `type`.
    public async Task Movements_InvalidType_Returns400(string query)
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(1));

        var response = await GetAsync($"/api/admin/skus/{product.SkuIds[0]}/movements?{query}");

        await AssertValidationErrorForFieldAsync(response, "type");
    }

    [Fact]
    // SKU không tồn tại vẫn nhận 404 khi `type` hợp lệ. Kiểm SKU chạy trước bộ lọc.
    public async Task Movements_UnknownSkuWithValidType_Returns404()
    {
        var response = await GetAsync($"/api/admin/skus/{Guid.NewGuid():N}/movements?type=manual");

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "SKU_NOT_FOUND");
    }

    [Fact]
    public async Task LowStock_InvalidPage_Returns400()
    {
        var response = await GetAsync("/api/admin/inventory/low-stock?page=0");

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }
}
