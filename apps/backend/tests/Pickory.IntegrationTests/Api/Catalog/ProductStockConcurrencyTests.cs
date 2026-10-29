using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Contracts.Stock;
using Pickory.Modules.Identity.Roles;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.Api.Catalog;

// CAT-08 review 1: sửa và xóa sản phẩm chạy cùng lúc với giữ hàng phải dùng cùng thứ tự khóa (`products` rồi `skus`).
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ProductStockConcurrencyTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly IntegrationDatabaseFixture _database;
    private readonly PickoryWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly StockTestData _data;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];
    private string _adminCookie = null!;

    public ProductStockConcurrencyTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _data = new StockTestData(database, _suffix);
    }

    public async Task InitializeAsync()
    {
        await IdentityTestData.SeedRolesAsync(_database);
        var (_, adminToken) = await IdentityTestData.CreateStaffSessionAsync(_database, DefaultRoles.AdminCode, $"conc-admin-{_suffix}");
        _adminCookie = $"pk_session={adminToken}";
        await _data.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await StockInvariant.AssertHoldsAsync(_database);
        await _data.CleanupAsync();
    }

    private sealed record CreatedProduct(string ProductId, string SkuId);

    // Tạo sản phẩm bằng API để biến thể khớp tổ hợp tùy chọn, rồi nhập kho bằng điều chỉnh tay.
    private async Task<CreatedProduct> CreateProductWithStockAsync(int stock)
    {
        var payload = new
        {
            categoryId = _data.CategoryId,
            brandId = _data.BrandId,
            name = $"Sản phẩm đồng thời {_suffix}-{Guid.NewGuid():N}",
            shortDesc = "Mô tả ngắn",
            description = new { type = "doc", content = new[] { new { type = "paragraph" } } },
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[] { new { optionValues = new Dictionary<string, string>(), price = 100000, stock, enabled = true } },
        };
        var created = await _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(created);
        var data = json.RootElement.GetProperty("data");
        var productId = data.GetProperty("id").GetString()!;
        var skuId = data.GetProperty("variants")[0].GetProperty("id").GetString()!;
        _data.TrackProduct(productId);

        return new CreatedProduct(productId, skuId);
    }

    private async Task<HttpResponseMessage> PatchAsync(string productId, object body) =>
        await _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"/api/admin/products/{productId}", body, _adminCookie));

    private async Task<HttpResponseMessage> DeleteAsync(string productId) =>
        await _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Delete, $"/api/admin/products/{productId}", null, _adminCookie));

    // Mô phỏng module `commerce`: giữ hàng trong transaction của bên gọi và chưa commit.
    private async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction, DomainEventCollector Events)> BeginCallerAsync()
    {
        var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        return (connection, transaction, new DomainEventCollector(_factory.Services.GetRequiredService<IEventDispatcher>()));
    }

    private async Task ReserveInCallerAsync(DomainEventCollector events, NpgsqlTransaction transaction, string skuId, int quantity)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IStockService>();
        var result = await service.ReserveAsync($"order-{_suffix}-{Guid.NewGuid():N}", [new StockItem(skuId, quantity)],
            new StockCallerTransaction(transaction, events), CancellationToken.None);
        Assert.True(result.Ok);
    }

    [Fact]
    // PATCH chờ khóa sản phẩm của bên gọi, rồi đọc tồn kho sau khi bên gọi commit. `inStock` vì vậy phải là false.
    public async Task Patch_WhileReserveHoldsLastUnits_KeepsInStockFalse()
    {
        var product = await CreateProductWithStockAsync(5);
        var (connection, transaction, events) = await BeginCallerAsync();
        await using var _ = connection;
        await using var __ = transaction;
        await ReserveInCallerAsync(events, transaction, product.SkuId, 5);

        var pendingPatch = Task.Run(() => PatchAsync(product.ProductId, new { name = $"Tên mới {_suffix}-{Guid.NewGuid():N}" }));
        await LockWait.UntilSessionIsBlockedAsync(_database);
        await transaction.CommitAsync();
        var response = await pendingPatch.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await _data.StockOfAsync(product.SkuId));
        Assert.False((await _data.DisplayOfAsync(product.ProductId)).InStock);
    }

    [Fact]
    // DELETE chờ khóa sản phẩm của bên gọi, rồi thấy movement `order_reserve` đã commit và chuyển sang xóa mềm.
    public async Task Delete_WhileReserveIsUncommitted_SoftDeletesAfterCommit()
    {
        var product = await CreateProductWithStockAsync(5);
        var (connection, transaction, events) = await BeginCallerAsync();
        await using var _ = connection;
        await using var __ = transaction;
        await ReserveInCallerAsync(events, transaction, product.SkuId, 2);

        var pendingDelete = Task.Run(() => DeleteAsync(product.ProductId));
        await LockWait.UntilSessionIsBlockedAsync(_database);
        await transaction.CommitAsync();
        var response = await pendingDelete.WaitAsync(TimeSpan.FromSeconds(30));

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Mong đợi 200, nhận {response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        Assert.Equal("soft_deleted", json.RootElement.GetProperty("data").GetProperty("outcome").GetString());
        await using var context = _database.CreateCatalogContext();
        Assert.NotNull((await context.Products.SingleAsync(item => item.Id == product.ProductId)).DeletedAt);
        Assert.Equal(3, await _data.StockOfAsync(product.SkuId));
        Assert.Equal(2, await context.InventoryMovements.CountAsync(movement => movement.SkuId == product.SkuId));
    }

    private sealed record TwoVariantProduct(string ProductId, string RedSkuId);

    // Sản phẩm hai màu: Đỏ (giá thấp, tồn 1) và Xanh (giá cao, tồn 5).
    private async Task<TwoVariantProduct> CreateTwoVariantProductAsync()
    {
        var payload = new
        {
            categoryId = _data.CategoryId,
            brandId = _data.BrandId,
            name = $"Sản phẩm hai màu {_suffix}-{Guid.NewGuid():N}",
            shortDesc = "Mô tả ngắn",
            description = new { type = "doc", content = new[] { new { type = "paragraph" } } },
            specs = new { },
            options = new[] { new { name = "Màu", values = new[] { "Đỏ", "Xanh" } } },
            warrantyMonths = 12,
            variants = TwoVariantsWithStock(100000),
        };
        var created = await _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(created);
        var data = json.RootElement.GetProperty("data");
        var productId = data.GetProperty("id").GetString()!;
        _data.TrackProduct(productId);
        var redSkuId = data.GetProperty("variants").EnumerateArray()
            .Single(variant => variant.GetProperty("optionValues").GetProperty("Màu").GetString() == "Đỏ")
            .GetProperty("id").GetString()!;
        return new TwoVariantProduct(productId, redSkuId);
    }

    private static object[] TwoVariants(int redPrice) =>
    [
        new { optionValues = new Dictionary<string, string> { ["Màu"] = "Đỏ" }, price = redPrice, enabled = true },
        new { optionValues = new Dictionary<string, string> { ["Màu"] = "Xanh" }, price = 200000, enabled = true },
    ];

    // Bản có tồn kho cho lúc tạo. Bản không có tồn kho dùng cho PATCH, để không ghi đè hàng đang giữ.
    private static object[] TwoVariantsWithStock(int redPrice) =>
    [
        new { optionValues = new Dictionary<string, string> { ["Màu"] = "Đỏ" }, price = redPrice, stock = 1, enabled = true },
        new { optionValues = new Dictionary<string, string> { ["Màu"] = "Xanh" }, price = 200000, stock = 5, enabled = true },
    ];

    [Fact]
    // Biến thể rẻ vừa hết hàng khi PATCH đổi giá của nó. Giá hiển thị phải tính theo tồn kho sau commit, tức là giá biến thể còn hàng.
    public async Task Patch_WhileReserveSellsOutCheapVariant_KeepsMinPriceOfInStockVariant()
    {
        var product = await CreateTwoVariantProductAsync();
        var (connection, transaction, events) = await BeginCallerAsync();
        await using var _ = connection;
        await using var __ = transaction;
        await ReserveInCallerAsync(events, transaction, product.RedSkuId, 1);

        var pendingPatch = Task.Run(() => PatchAsync(product.ProductId, new { variants = TwoVariants(150000) }));
        await LockWait.UntilSessionIsBlockedAsync(_database);
        await transaction.CommitAsync();
        var response = await pendingPatch.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var display = await _data.DisplayOfAsync(product.ProductId);
        Assert.Equal(200000, display.MinPrice);
        Assert.True(display.InStock);
    }

    [Fact]
    // PATCH đổi giá và giữ hàng chạy song song trên cùng SKU. Hai bên khóa cùng thứ tự nên không có deadlock hay lỗi 500.
    public async Task Patch_PriceChangesRunningWithReserves_NeverFail()
    {
        var product = await CreateProductWithStockAsync(100);
        const int rounds = 40;

        var patches = Enumerable.Range(0, rounds).Select(round => Task.Run(() => PatchAsync(product.ProductId, new
        {
            variants = new[] { new { optionValues = new Dictionary<string, string>(), price = 100000 + round, enabled = true } },
        })));
        var reserves = Enumerable.Range(0, rounds).Select(_ => Task.Run(async () =>
        {
            using var scope = _factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IStockService>();
            return await service.ReserveAsync($"order-{_suffix}-{Guid.NewGuid():N}", [new StockItem(product.SkuId, 1)], null, CancellationToken.None);
        }));

        var patchResponses = await Task.WhenAll(patches);
        var reserveResults = await Task.WhenAll(reserves);

        Assert.All(patchResponses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.All(reserveResults, result => Assert.True(result.Ok));
        Assert.Equal(100 - rounds, await _data.StockOfAsync(product.SkuId));
        Assert.True((await _data.DisplayOfAsync(product.ProductId)).InStock);
    }
}
