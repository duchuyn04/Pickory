using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.IntegrationTests.Api.Catalog;

// Luồng SSE công khai gửi sự kiện tồn kho sau khi giao dịch đổi kho commit.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class StockEventsTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly IntegrationDatabaseFixture _database;
    private readonly PickoryWebApplicationFactory _factory;
    private readonly StockTestData _data;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];
    private HttpClient _client = null!;
    private string _adminCookie = null!;

    public StockEventsTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _data = new StockTestData(database, _suffix);
    }

    public async Task InitializeAsync()
    {
        await IdentityTestData.SeedRolesAsync(_database);
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            _database, DefaultRoles.AdminCode, $"stock-events-{_suffix}");
        _adminCookie = $"pk_session={token}";
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        await _data.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await StockInvariant.AssertHoldsAsync(_database);
        await _data.CleanupAsync();
    }

    [Fact]
    // Kết nối nhận sự kiện `connected` trước; sau khi sửa số lượng, cùng luồng nhận số mới.
    public async Task Stream_SendsConnectedThenStockChangeAfterAdjustment()
    {
        var product = await CreateProductAsync(stock: 5);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // `ResponseHeadersRead` trả về khi header đã gửi, không chờ luồng SSE kết thúc.
        using var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/stock-events"),
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);
        // Đọc dòng xác nhận để chắc kết nối đã vào hub trước khi đổi kho.
        await ReadEventDataAsync(reader, "connected", timeout.Token);

        var adjust = ActorHttp.CreateJsonRequest(
            HttpMethod.Patch, $"/api/admin/products/{product.ProductId}",
            new
            {
                variants = new[]
                {
                    new
                    {
                        optionValues = new Dictionary<string, string>(),
                        price = 1_000_000,
                        stock = 8,
                        enabled = true,
                        weightGrams = (int?)200,
                        lengthCm = (int?)15,
                        widthCm = (int?)10,
                        heightCm = (int?)5,
                    },
                },
            },
            _adminCookie);
        using var adjustResponse = await _client.SendAsync(adjust, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, adjustResponse.StatusCode);

        var data = await ReadEventDataAsync(reader, "stock.changed", timeout.Token);
        Assert.Contains($"\"skuId\":\"{product.SkuId}\"", data);
        Assert.Contains("\"stock\":8", data);
        Assert.Contains("\"productInStock\":true", data);
    }

    // Tạo sản phẩm nháp qua API để biến thể khớp tùy chọn. Sản phẩm nháp vẫn phát sự kiện tồn kho.
    private async Task<TestProduct> CreateProductAsync(int stock)
    {
        var payload = new
        {
            categoryId = _data.CategoryId,
            brandId = _data.BrandId,
            name = $"Sản phẩm SSE {_suffix}",
            shortDesc = "Mô tả ngắn",
            description = new { type = "doc", content = new[] { new { type = "paragraph" } } },
            specs = new { },
            options = Array.Empty<object>(),
            warrantyMonths = 12,
            variants = new[]
            {
                new { optionValues = new Dictionary<string, string>(), price = 1_000_000, stock, enabled = true },
            },
        };
        using var response = await _client.SendAsync(
            ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/admin/products", payload, _adminCookie));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ActorHttp.ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        var productId = data.GetProperty("id").GetString()!;
        _data.TrackProduct(productId);
        return new TestProduct(productId, data.GetProperty("variants")[0].GetProperty("id").GetString()!);
    }

    private sealed record TestProduct(string ProductId, string SkuId);

    // Đọc từng dòng SSE tới khi gặp dữ liệu của đúng tên sự kiện.
    private static async Task<string> ReadEventDataAsync(
        StreamReader reader, string expectedName, CancellationToken cancellationToken)
    {
        string? currentName = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new InvalidOperationException("Luồng SSE đã đóng trước khi có sự kiện mong đợi.");
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                currentName = line["event: ".Length..];
                continue;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal) && currentName == expectedName)
            {
                return line["data: ".Length..];
            }

            // Dòng trống kết thúc một sự kiện. Sự kiện không phải loại cần đọc thì bỏ qua.
            if (line.Length == 0)
            {
                currentName = null;
            }
        }

        throw new OperationCanceledException(cancellationToken);
    }
}
