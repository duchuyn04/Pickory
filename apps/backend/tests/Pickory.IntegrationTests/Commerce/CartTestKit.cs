using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Identity.Entities;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// Dựng dữ liệu catalog, gửi request giỏ hàng và đọc kết quả. Các lớp test COM-04 dùng chung bộ công cụ này.
internal sealed class CartTestKit
{
    private const string WebOrigin = "http://localhost:5173";

    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;
    private readonly ReadingTestData _catalog;
    private readonly List<string> _anonymousIds = [];
    private readonly List<string> _userIds = [];
    private string _categoryId = null!;

    public CartTestKit(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        // Client không giữ cookie. Từng request tự gắn `pk_anon` và `pk_session` để test kiểm soát chủ giỏ.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _catalog = new ReadingTestData(database, Guid.NewGuid().ToString("N")[..10]);
    }

    public IntegrationDatabaseFixture Database => _database;

    public async Task InitializeAsync()
    {
        await _catalog.AddBrandAsync();
        var category = await _catalog.AddCategoryAsync("Giỏ hàng", null, []);
        _categoryId = category.Id;
    }

    // Xóa giỏ và cấu hình test đã tạo, rồi xóa dữ liệu catalog. Test sau bắt đầu với DB không còn dấu vết.
    public async Task CleanupAsync()
    {
        await using var commerce = _database.CreateCommerceContext();
        await commerce.Carts
            .Where(cart => (cart.AnonymousId != null && _anonymousIds.Contains(cart.AnonymousId))
                || (cart.UserId != null && _userIds.Contains(cart.UserId)))
            .ExecuteDeleteAsync();
        await commerce.Settings.ExecuteDeleteAsync();
        await _catalog.CleanupAsync();
    }

    public string NewAnonymousId()
    {
        var anonymousId = Guid.NewGuid().ToString();
        _anonymousIds.Add(anonymousId);
        return anonymousId;
    }

    public async Task<ReadingTestData.SeededProduct> AddProductAsync(
        int price = 100_000, int stock = 10, bool enabled = true, ProductStatus status = ProductStatus.Active,
        int? compareAtPrice = null)
    {
        var productId = Guid.NewGuid().ToString("N");
        return await _catalog.AddProductAsync(new ReadingTestData.ProductSeed(
            productId, _categoryId, status,
            Skus: [new ReadingTestData.SkuSeed(price, stock, enabled, compareAtPrice)]));
    }

    // Tạo nhiều sản phẩm một SKU và trả danh sách id SKU theo thứ tự tạo.
    public async Task<IReadOnlyList<string>> AddSkusAsync(int count)
    {
        var skuIds = new List<string>();
        for (var index = 0; index < count; index++)
        {
            skuIds.Add((await AddProductAsync()).SkuIds[0]);
        }

        return skuIds;
    }

    // Khách đã đăng nhập: trả `UserId` và cookie phiên. Giỏ của khách này gắn theo `UserId`.
    public async Task<(string UserId, string SessionToken)> CreateCustomerAsync()
    {
        var user = await IdentityTestData.CreateUserAsync(
            _database, IdentityTestData.NewEmail("cart"), UserKind.Customer, "customer", emailVerified: true);
        var token = await IdentityTestData.CreateSessionAsync(_database, user.Id);
        _userIds.Add(user.Id);
        return (user.Id, token);
    }

    // `body` là chuỗi thì gửi nguyên văn như JSON thô. Cách này cho phép test gửi kiểu dữ liệu sai.
    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, string anonymousId, object? body = null,
        string? sessionToken = null, bool withOrigin = true)
    {
        var request = new HttpRequestMessage(method, path);
        var cookie = $"pk_anon={anonymousId}";
        if (sessionToken is not null)
        {
            cookie += $"; pk_session={sessionToken}";
        }

        request.Headers.Add("Cookie", cookie);
        if (withOrigin)
        {
            request.Headers.Add("Origin", WebOrigin);
        }

        if (body is not null)
        {
            var json = body as string ?? JsonSerializer.Serialize(body);
            request.Content = new StringContent(json, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        }

        return await _client.SendAsync(request);
    }

    public Task<HttpResponseMessage> GetCartAsync(string anonymousId, string? sessionToken = null) =>
        SendAsync(HttpMethod.Get, "/api/cart", anonymousId, sessionToken: sessionToken);

    public Task<HttpResponseMessage> AddAsync(
        string anonymousId, string skuId, int quantity = 1, string? source = null, string? sessionToken = null) =>
        SendAsync(HttpMethod.Post, "/api/cart/items", anonymousId, new { skuId, quantity, source }, sessionToken);

    public Task<HttpResponseMessage> SetQuantityAsync(string anonymousId, string skuId, int quantity) =>
        SendAsync(new HttpMethod("PATCH"), $"/api/cart/items/{skuId}", anonymousId, new { quantity });

    public Task<HttpResponseMessage> RemoveAsync(string anonymousId, string skuId) =>
        SendAsync(HttpMethod.Delete, $"/api/cart/items/{skuId}", anonymousId);

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    // Đọc phần `data` của response thành công. Kiểm status trước để lỗi hiện rõ body khi test đỏ.
    public static async Task<JsonElement> ReadCartAsync(HttpResponseMessage response)
    {
        var json = await ReadJsonAsync(response);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Mong đợi 200 nhưng nhận {(int)response.StatusCode}: {json}");
        return json.GetProperty("data");
    }

    public static async Task<JsonElement> AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var json = await ReadJsonAsync(response);
        Assert.True(response.StatusCode == status, $"Mong đợi {(int)status} nhưng nhận {(int)response.StatusCode}: {json}");
        var error = json.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetString());
        return error;
    }

    public static JsonElement LineOf(JsonElement cart, string skuId) =>
        cart.GetProperty("items").EnumerateArray().Single(line => line.GetProperty("skuId").GetString() == skuId);

    // Đặt dòng `commerce_settings` để test giới hạn riêng. `CleanupAsync` xóa dòng này.
    public async Task SetLimitsAsync(int maxQuantityPerSku, int maxCartLines)
    {
        await using var commerce = _database.CreateCommerceContext();
        commerce.Settings.Add(new CommerceSettings
        {
            Id = "default",
            FreeShippingThreshold = 500_000,
            CodMaxAmount = 20_000_000,
            MaxQuantityPerSku = maxQuantityPerSku,
            MaxCartLines = maxCartLines,
            UpdatedAt = DateTime.UtcNow,
        });
        await commerce.SaveChangesAsync();
    }

    // Ghi thẳng một giỏ có cả `UserId` và `AnonymousId`. Dựng dữ liệu mà route giỏ không tự tạo.
    public async Task InsertCartAsync(string userId, string anonymousId, string skuId, int quantity)
    {
        await using var commerce = _database.CreateCommerceContext();
        var cart = new Cart { UserId = userId, AnonymousId = anonymousId, UpdatedAt = DateTime.UtcNow };
        cart.Items.Add(new CartItem { SkuId = skuId, Quantity = quantity });
        commerce.Carts.Add(cart);
        await commerce.SaveChangesAsync();
    }

    public async Task<List<Cart>> CartsOfAnonymousAsync(string anonymousId)
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.Carts.AsNoTracking().Include(cart => cart.Items)
            .Where(cart => cart.AnonymousId == anonymousId).ToListAsync();
    }

    public async Task<List<Cart>> CartsOfUserAsync(string userId)
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.Carts.AsNoTracking().Include(cart => cart.Items)
            .Where(cart => cart.UserId == userId).ToListAsync();
    }

    public async Task<int> StockOfAsync(string skuId)
    {
        await using var catalog = _database.CreateCatalogContext();
        return await catalog.Skus.AsNoTracking().Where(sku => sku.Id == skuId).Select(sku => sku.Stock).SingleAsync();
    }

    public async Task<int> MovementCountOfAsync(string skuId)
    {
        await using var catalog = _database.CreateCatalogContext();
        return await catalog.InventoryMovements.CountAsync(movement => movement.SkuId == skuId);
    }

    // Sửa catalog sau khi khách đã thêm hàng. Giỏ phải phản ánh thay đổi ở lần xem kế tiếp.
    public async Task UpdateSkuAsync(string skuId, Action<Sku> change)
    {
        await using var catalog = _database.CreateCatalogContext();
        var sku = await catalog.Skus.SingleAsync(item => item.Id == skuId);
        change(sku);
        await catalog.SaveChangesAsync();
    }

    public async Task SetProductStatusAsync(string productId, ProductStatus status)
    {
        await using var catalog = _database.CreateCatalogContext();
        var product = await catalog.Products.SingleAsync(item => item.Id == productId);
        product.Status = status;
        await catalog.SaveChangesAsync();
    }

    // Xóa hẳn sản phẩm khỏi catalog để dựng tình huống SKU không còn tồn tại.
    public async Task DeleteProductAsync(string productId)
    {
        await using var catalog = _database.CreateCatalogContext();
        await catalog.InventoryMovements
            .Where(movement => catalog.Skus.Any(sku => sku.Id == movement.SkuId && sku.ProductId == productId))
            .ExecuteDeleteAsync();
        await catalog.Products.Where(product => product.Id == productId).ExecuteDeleteAsync();
    }

    public IReadOnlyList<CartItemAddedEvent> AddedEventsOf(string anonymousId) =>
        _factory.DomainEvents.OfType<CartItemAddedEvent>().Where(item => item.AnonymousId == anonymousId).ToList();

    public IReadOnlyList<CartItemRemovedEvent> RemovedEventsOf(string anonymousId) =>
        _factory.DomainEvents.OfType<CartItemRemovedEvent>().Where(item => item.AnonymousId == anonymousId).ToList();
}
