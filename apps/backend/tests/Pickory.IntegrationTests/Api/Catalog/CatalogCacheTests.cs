using System.Data.Common;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.IntegrationTests.Api.Catalog;

// CACHE-01: `CATALOG_CACHE_SECONDS` bật cache ngắn cho `GET /api/products` và `GET /api/categories`.
// Mọi thay đổi catalog xóa cache ngay sau commit, nên khách không thấy dữ liệu cũ sau khi admin sửa.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CatalogCacheTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    // Đếm lệnh SQL của `CatalogDbContext`. Test chứng minh cache bằng số lệnh, không bằng thời gian phản hồi.
    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _commands;

        public int Commands => Volatile.Read(ref _commands);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _commands);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private readonly IntegrationDatabaseFixture _database;
    private readonly PickoryWebApplicationFactory _factory;
    private readonly StockTestData _data;
    private readonly CommandCounter _counter = new();
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];
    private HttpClient _client = null!;
    private string _adminCookie = null!;
    private WebApplicationFactory<Program> _cachedFactory = null!;
    private StockTestData.SeededProduct _inStock = null!;

    public CatalogCacheTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _data = new StockTestData(database, _suffix);
    }

    private string CategorySlug => $"ton-kho-{_suffix}";

    public async Task InitializeAsync()
    {
        await IdentityTestData.SeedRolesAsync(_database);
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(_database, DefaultRoles.AdminCode, $"cache-admin-{_suffix}");
        _adminCookie = $"pk_session={token}";
        await _data.InitializeAsync();
        _inStock = await _data.CreateProductAsync(new StockTestData.SkuSeed(3));
        await _data.CreateProductAsync(new StockTestData.SkuSeed(0));

        _cachedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("CATALOG_CACHE_SECONDS", "60");
            builder.ConfigureTestServices(services =>
                services.ConfigureDbContext<CatalogDbContext>(options => options.AddInterceptors(_counter)));
        });
        _client = _cachedFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task DisposeAsync()
    {
        await _data.CleanupAsync();
        _client.Dispose();
        _cachedFactory.Dispose();
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetJsonAsync(string path)
    {
        var response = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, path, null));
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    private async Task<List<JsonElement>> ListItemsAsync(string query)
    {
        var (status, body) = await GetJsonAsync($"/api/products?category={CategorySlug}{query}");
        Assert.Equal(HttpStatusCode.OK, status);
        return body.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
    }

    private async Task<HttpResponseMessage> AdminAsync(HttpMethod method, string path, object? body) =>
        await _client.SendAsync(ActorHttp.CreateJsonRequest(method, path, body, _adminCookie));

    [Fact]
    public async Task Products_SameQueryTwice_SecondCallRunsNoCatalogSql()
    {
        await ListItemsAsync(string.Empty);
        var before = _counter.Commands;

        var items = await ListItemsAsync(string.Empty);

        Assert.Equal(2, items.Count);
        Assert.Equal(before, _counter.Commands);
    }

    [Fact]
    public async Task Products_SameParametersInAnotherOrder_ShareOneEntry()
    {
        await GetJsonAsync($"/api/products?category={CategorySlug}&sort=newest");
        var before = _counter.Commands;

        var (status, _) = await GetJsonAsync($"/api/products?sort=newest&category={CategorySlug}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(before, _counter.Commands);
    }

    [Fact]
    public async Task Products_DifferentParameters_GiveDifferentResults()
    {
        var all = await ListItemsAsync(string.Empty);
        var inStockOnly = await ListItemsAsync("&inStock=true");

        Assert.Equal(2, all.Count);
        Assert.Single(inStockOnly);
        Assert.Equal(_inStock.ProductId, inStockOnly[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Categories_SecondCallRunsNoCatalogSql()
    {
        await GetJsonAsync("/api/categories");
        var before = _counter.Commands;

        var (status, _) = await GetJsonAsync("/api/categories");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(before, _counter.Commands);
    }

    [Fact]
    public async Task AdminRenamesAProduct_NextReadSeesTheNewName()
    {
        await ListItemsAsync(string.Empty);

        var response = await AdminAsync(HttpMethod.Patch, $"/api/admin/products/{_inStock.ProductId}", new
        {
            name = $"Tên mới {_suffix}",
            // Sản phẩm dựng sẵn có một SKU với tùy chọn `i=0`. PATCH phải gửi lại tổ hợp khớp để qua kiểm tra biến thể.
            options = new[] { new { name = "i", values = new[] { "0" } } },
            variants = new[]
            {
                new
                {
                    optionValues = new Dictionary<string, string> { ["i"] = "0" },
                    price = 1_000_000, enabled = true, weightGrams = (int?)200, lengthCm = (int?)15, widthCm = (int?)10, heightCm = (int?)5,
                },
            },
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var items = await ListItemsAsync(string.Empty);

        var renamed = items.Single(item => item.GetProperty("id").GetString() == _inStock.ProductId);
        Assert.Equal($"Tên mới {_suffix}", renamed.GetProperty("name").GetString());
    }

    [Fact]
    public async Task AdminHidesAProduct_NextReadNoLongerListsIt()
    {
        await ListItemsAsync(string.Empty);

        var response = await AdminAsync(HttpMethod.Put, $"/api/admin/products/{_inStock.ProductId}/status", new { status = "hidden" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await ListItemsAsync(string.Empty);

        Assert.DoesNotContain(items, item => item.GetProperty("id").GetString() == _inStock.ProductId);
    }

    [Fact]
    public async Task AdminAdjustsStock_NextReadSeesTheNewAvailability()
    {
        var before = await ListItemsAsync(string.Empty);
        Assert.True(before.Single(item => item.GetProperty("id").GetString() == _inStock.ProductId).GetProperty("inStock").GetBoolean());

        var response = await AdminAsync(HttpMethod.Post, $"/api/admin/skus/{_inStock.SkuIds[0]}/movements",
            new { delta = -3, reason = "Bán hết" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var after = await ListItemsAsync(string.Empty);

        Assert.False(after.Single(item => item.GetProperty("id").GetString() == _inStock.ProductId).GetProperty("inStock").GetBoolean());
    }

    [Fact]
    public async Task AdminRenamesACategory_NextCategoriesReadSeesTheNewName()
    {
        var (_, first) = await GetJsonAsync("/api/categories");
        Assert.Contains(first.GetProperty("data").EnumerateArray(), item => item.GetProperty("slug").GetString() == CategorySlug);

        var response = await AdminAsync(HttpMethod.Patch, $"/api/admin/categories/{_data.CategoryId}", new { name = $"Danh mục mới {_suffix}" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (_, second) = await GetJsonAsync("/api/categories");

        Assert.Contains(second.GetProperty("data").EnumerateArray(),
            item => item.GetProperty("name").GetString() == $"Danh mục mới {_suffix}");
    }

    [Fact]
    public async Task ErrorResponse_IsNotCached()
    {
        var path = $"/api/products?category={CategorySlug}&attr.gpu=rtx";
        var (firstStatus, _) = await GetJsonAsync(path);
        var before = _counter.Commands;

        var (secondStatus, _) = await GetJsonAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, firstStatus);
        Assert.Equal(HttpStatusCode.BadRequest, secondStatus);
        Assert.True(_counter.Commands > before, "Lỗi lần hai phải chạy lại truy vấn, nên lỗi không được cache.");
    }

    [Fact]
    public async Task CacheDisabledByDefault_EverySecondCallRunsSql()
    {
        var counter = new CommandCounter();
        using var plain = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.ConfigureDbContext<CatalogDbContext>(options => options.AddInterceptors(counter))));
        using var client = plain.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var path = $"/api/products?category={CategorySlug}";
        await client.GetAsync(path);
        var before = counter.Commands;

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(counter.Commands > before);
    }
}
