using System.Net;
using System.Text.Json;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.IntegrationTests.ActivityTracking;

// ACT-11: `GET /api/admin/analytics/funnel`. Test ghi bộ đếm vào năm 2031 và dùng năm 2035 làm khoảng không có dòng nào, nên kết quả không đổi theo ngày chạy.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AdminFunnelEndpointTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string FunnelPath = "/api/admin/analytics/funnel";

    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });
    private string _salesCookie = null!;
    private string _supportCookie = null!;
    private string _customerCookie = null!;

    public async Task InitializeAsync()
    {
        await FunnelSeedData.ResetAsync(database);
        // Role sales có `reports:read`, role support thì không. Hai role này lấy từ seeder thật.
        _salesCookie = await CreateSessionCookieAsync(UserKind.Staff, DefaultRoles.SalesCode);
        _supportCookie = await CreateSessionCookieAsync(UserKind.Staff, DefaultRoles.SupportCode);
        _customerCookie = await CreateSessionCookieAsync(UserKind.Customer, DefaultRoles.CustomerCode);
    }

    public async Task DisposeAsync()
    {
        await FunnelSeedData.ClearAsync(database);
    }

    [Fact]
    public async Task Get_SeededRange_Returns200WithDataEnvelopeAndHandCalculatedNumbers()
    {
        var response = await GetAsync("from=2031-05-10&to=2031-05-12", _salesCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("2031-05-10", data.GetProperty("from").GetString());
        Assert.Equal("2031-05-12", data.GetProperty("to").GetString());
        var steps = data.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(["product_view", "add_to_cart", "order_placed"], steps.Select(step => step.GetProperty("metric").GetString()));
        Assert.Equal([200L, 50L, 10L], steps.Select(step => step.GetProperty("count").GetInt64()));
        // Tỉ lệ là số thập phân, không phải phần trăm. Bước đầu không có bước trước nên là `null`.
        Assert.Equal(JsonValueKind.Null, steps[0].GetProperty("conversionRate").ValueKind);
        Assert.Equal(0.25m, steps[1].GetProperty("conversionRate").GetDecimal());
        Assert.Equal(0.2m, steps[2].GetProperty("conversionRate").GetDecimal());
    }

    [Fact]
    public async Task Get_SeededRange_ReturnsOnlyAggregatedStepsWithoutPerCustomerFields()
    {
        var response = await GetAsync("from=2031-05-10&to=2031-05-12", _salesCookie);

        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        // Response chỉ có khoảng ngày và ba bước. Không có trường nào xác định khách.
        Assert.Equal(["from", "to", "steps"], data.EnumerateObject().Select(property => property.Name));
        foreach (var step in data.GetProperty("steps").EnumerateArray())
        {
            Assert.Equal(["metric", "count", "conversionRate"], step.EnumerateObject().Select(property => property.Name));
        }
    }

    [Fact]
    public async Task Get_DaysOutsideTheRange_AreNotAddedToTheFunnel()
    {
        // Hai ngày kề khoảng mỗi chỉ số 1000. Nếu cộng nhầm thì số đếm vượt 200, 50, 10.
        var response = await GetAsync("from=2031-05-11&to=2031-05-12", _salesCookie);

        using var document = await ActorHttp.ReadJsonAsync(response);
        var counts = document.RootElement.GetProperty("data").GetProperty("steps").EnumerateArray()
            .Select(step => step.GetProperty("count").GetInt64());
        Assert.Equal([80L, 20L, 4L], counts);
    }

    [Fact]
    public async Task Get_FromEqualsTo_ReturnsOnlyThatDay()
    {
        var response = await GetAsync("from=2031-05-11&to=2031-05-11", _salesCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var counts = document.RootElement.GetProperty("data").GetProperty("steps").EnumerateArray()
            .Select(step => step.GetProperty("count").GetInt64());
        Assert.Equal([80L, 20L, 4L], counts);
    }

    [Fact]
    public async Task Get_RangeWithoutData_ReturnsThreeZeroStepsWithNullRates()
    {
        var response = await GetAsync("from=2035-03-01&to=2035-03-05", _salesCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var steps = document.RootElement.GetProperty("data").GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(3, steps.Count);
        Assert.All(steps, step => Assert.Equal(0L, step.GetProperty("count").GetInt64()));
        Assert.All(steps, step => Assert.Equal(JsonValueKind.Null, step.GetProperty("conversionRate").ValueKind));
    }

    [Theory]
    [InlineData("to=2031-05-12")]
    [InlineData("from=2031-05-10")]
    [InlineData("")]
    [InlineData("from=&to=2031-05-12")]
    [InlineData("from=abc&to=2031-05-12")]
    [InlineData("from=2031-5-10&to=2031-05-12")]
    [InlineData("from=2031-05-10T00:00:00&to=2031-05-12")]
    [InlineData("from=2031-05-13&to=2031-05-12")]
    [InlineData("from=2031-01-01&to=2032-01-02")]
    [InlineData("from=2031-05-10&from=2031-05-11&to=2031-05-12")]
    public async Task Get_InvalidQuery_Returns400ValidationError(string query)
    {
        var response = await GetAsync(query, _salesCookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("VALIDATION_ERROR", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_Exactly366Days_Returns200()
    {
        // Từ 2031-01-01 đến 2032-01-01 là đúng 366 ngày khi tính cả hai đầu.
        var response = await GetAsync("from=2031-01-01&to=2032-01-01", _salesCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_StaffWithoutReportsRead_Returns403()
    {
        var response = await GetAsync("from=2031-05-10&to=2031-05-12", _supportCookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_StaffWithoutReportsRead_Gets403BeforeQueryValidation()
    {
        // Kiểm quyền chạy trước kiểm query. Role thiếu quyền gửi query sai vẫn nhận 403, không nhận 400.
        var response = await GetAsync("from=abc", _supportCookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Customer_Returns403()
    {
        var response = await GetAsync("from=2031-05-10&to=2031-05-12", _customerCookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Guest_Returns401()
    {
        var response = await GetAsync("from=2031-05-10&to=2031-05-12", cookieHeader: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpResponseMessage> GetAsync(string query, string? cookieHeader)
    {
        var path = query.Length == 0 ? FunnelPath : $"{FunnelPath}?{query}";
        using var request = ActorHttp.CreateRequest(HttpMethod.Get, path, cookieHeader);
        return await _client.SendAsync(request);
    }

    private async Task<string> CreateSessionCookieAsync(UserKind kind, string roleCode)
    {
        var user = await IdentityTestData.CreateUserAsync(
            database, IdentityTestData.NewEmail("funnel"), kind, roleCode, emailVerified: true);
        return $"pk_session={await IdentityTestData.CreateSessionAsync(database, user.Id)}";
    }
}
