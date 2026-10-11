using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Database;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-04 lát 5: hai route danh mục địa chỉ công khai, chỉ trả dòng đang dùng, sắp theo tên tiếng Việt, và kiểm tra `provinceCode`.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AddressEndpointsTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private static readonly DateTime SyncedAt = new(2026, 10, 10, 6, 30, 15, DateTimeKind.Utc);

    private readonly PickoryWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AddressEndpointsTests(PickoryWebApplicationFactory factory)
    {
        _factory = factory;
        // Client không có cookie, nên mọi request là của khách vãng lai chưa đăng nhập.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync() => await WithContextAsync(AddressTestDatabase.ClearAsync);

    public async Task DisposeAsync() => await WithContextAsync(AddressTestDatabase.ClearAsync);

    private async Task WithContextAsync(Func<ShippingDbContext, Task> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        // Test xóa cả bảng, nên phải chắc context trỏ tới DB test chứ không phải DB dev.
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        await action(context);
    }

    private Task SeedProvinceAsync(string code, string name, bool isActive = true) =>
        WithContextAsync(async context =>
        {
            context.AddressProvinces.Add(AddressTestDatabase.Province(code, name, isActive, SyncedAt));
            await context.SaveChangesAsync();
        });

    private Task SeedWardAsync(string code, string provinceCode, string name, bool isActive = true) =>
        WithContextAsync(async context =>
        {
            context.AddressWards.Add(AddressTestDatabase.Ward(code, provinceCode, name, isActive, SyncedAt));
            await context.SaveChangesAsync();
        });

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static List<string> NamesOf(JsonElement body) =>
        body.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("name").GetString()!).ToList();

    [Fact]
    public async Task Provinces_AnonymousVisitor_ReturnsOnlyActiveRows_WithCodeAndNameOnly()
    {
        await SeedProvinceAsync("1001", "Hà Nội");
        await SeedProvinceAsync("1002", "Tỉnh đã tắt", isActive: false);

        var response = await _client.GetAsync("/api/addresses/provinces");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var item = Assert.Single(body.GetProperty("data").EnumerateArray());
        Assert.Equal("1001", item.GetProperty("code").GetString());
        Assert.Equal("Hà Nội", item.GetProperty("name").GetString());
        // Chỉ hai trường `code` và `name`. `isActive` và `syncedAt` là chi tiết nội bộ.
        Assert.Equal(["code", "name"], item.EnumerateObject().Select(property => property.Name).Order());
    }

    [Fact]
    public async Task Provinces_AreSortedByVietnameseName()
    {
        // Thêm theo thứ tự lộn xộn. Phép so `Ordinal` đặt "Đà Nẵng" sau "Hồ Chí Minh".
        await SeedProvinceAsync("1004", "Hồ Chí Minh");
        await SeedProvinceAsync("1002", "Đà Nẵng");
        await SeedProvinceAsync("1003", "Hà Nội");
        await SeedProvinceAsync("1001", "Cần Thơ");

        var response = await _client.GetAsync("/api/addresses/provinces");

        Assert.Equal(["Cần Thơ", "Đà Nẵng", "Hà Nội", "Hồ Chí Minh"], NamesOf(await ReadJsonAsync(response)));
    }

    [Fact]
    public async Task Provinces_EmptyCatalog_ReturnsEmptyList()
    {
        var response = await _client.GetAsync("/api/addresses/provinces");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await ReadJsonAsync(response)).GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Wards_ReturnsOnlyActiveWardsOfThatProvince_SortedByVietnameseName()
    {
        await SeedProvinceAsync("1001", "Hồ Chí Minh");
        await SeedProvinceAsync("1002", "Hà Nội");
        await SeedWardAsync("2004", "1001", "Phường Tân Định");
        await SeedWardAsync("2002", "1001", "Phường Đa Kao");
        await SeedWardAsync("2001", "1001", "Phường Bến Nghé");
        await SeedWardAsync("2003", "1001", "Phường Cầu Ông Lãnh");
        await SeedWardAsync("2005", "1001", "Phường đã tắt", isActive: false);
        await SeedWardAsync("2006", "1002", "Phường của tỉnh khác");

        var response = await _client.GetAsync("/api/addresses/wards?provinceCode=1001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(
            ["Phường Bến Nghé", "Phường Cầu Ông Lãnh", "Phường Đa Kao", "Phường Tân Định"],
            NamesOf(body));
        var first = body.GetProperty("data")[0];
        Assert.Equal("2001", first.GetProperty("code").GetString());
        Assert.Equal(["code", "name"], first.EnumerateObject().Select(property => property.Name).Order());
    }

    [Fact]
    public async Task Wards_OfInactiveProvince_AreHidden()
    {
        await SeedProvinceAsync("1099", "Tỉnh đã giải thể", isActive: false);
        await SeedWardAsync("2001", "1099", "Phường còn đang dùng");

        var response = await _client.GetAsync("/api/addresses/wards?provinceCode=1099");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await ReadJsonAsync(response)).GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Wards_UnknownProvinceCode_Returns200WithEmptyList()
    {
        await SeedProvinceAsync("1001", "Hà Nội");

        var response = await _client.GetAsync("/api/addresses/wards?provinceCode=khong-co");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await ReadJsonAsync(response)).GetProperty("data").EnumerateArray());
    }

    [Theory]
    [InlineData("/api/addresses/wards")]
    [InlineData("/api/addresses/wards?provinceCode=")]
    [InlineData("/api/addresses/wards?provinceCode=%20%20")]
    public async Task Wards_MissingOrBlankProvinceCode_Returns400ValidationError(string path)
    {
        await SeedProvinceAsync("1001", "Hà Nội");
        await SeedWardAsync("2001", "1001", "Phường A");

        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await ReadJsonAsync(response)).GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        Assert.Contains("provinceCode", error.GetProperty("message").GetString());
    }
}
