using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Pickory.Api.Middleware;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Xunit;

namespace Pickory.IntegrationTests.Api.Identity;

// Bộ lọc quyền phải chạy thật trên route và trả JSON lỗi như `requireAuth` của bản Node.
// Test dựng app nhỏ với middleware lỗi thật. Actor do test cấp qua header nên test không cần DB.
public sealed class ActorAuthorizationEndpointTests : IAsyncLifetime
{
    private const string ActorHeaderName = "X-Test-Actor";
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        _app = builder.Build();
        _app.UseMiddleware<ExceptionHandlingMiddleware>();
        _app.Use(async (context, next) =>
        {
            context.SetActor(CreateActor(context.Request.Headers[ActorHeaderName].ToString()));
            await next(context);
        });
        _app.MapGet("/signed-in", () => Results.Ok()).RequireAuth();
        _app.MapGet("/staff-area", () => Results.Ok()).RequireStaff();
        _app.MapGet("/cancel-order", () => Results.Ok()).RequirePermission(Permissions.OrdersRead, Permissions.OrdersCancel);
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData("/signed-in")]
    [InlineData("/staff-area")]
    [InlineData("/cancel-order")]
    public async Task Guest_Receives401Unauthorized(string path)
    {
        var response = await SendAsync(path, actorName: "guest");

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Vui lòng đăng nhập để tiếp tục.");
    }

    [Fact]
    public async Task Customer_PassesRequireAuth()
    {
        var response = await SendAsync("/signed-in", actorName: "customer");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Customer_Receives403OnStaffArea()
    {
        var response = await SendAsync("/staff-area", actorName: "customer");

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN", "Bạn không có quyền truy cập khu vực này.");
    }

    [Fact]
    public async Task Staff_PassesRequireStaff()
    {
        var response = await SendAsync("/staff-area", actorName: "staff-orders-read");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StaffMissingOneOfTwoPermissions_Receives403()
    {
        var response = await SendAsync("/cancel-order", actorName: "staff-orders-read");

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN", "Bạn không có đủ quyền thực hiện thao tác này.");
    }

    [Fact]
    public async Task StaffWithAllPermissions_PassesRequirePermission()
    {
        var response = await SendAsync("/cancel-order", actorName: "staff-orders-full");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(string path, string actorName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(ActorHeaderName, actorName);
        return await _client.SendAsync(request);
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code, string message)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.Equal(message, error.GetProperty("message").GetString());
    }

    private static Actor CreateActor(string actorName)
    {
        var guest = new Actor
        {
            AnonymousId = "6f1d2c3b-4a5e-4f60-8a7b-9c0d1e2f3a4b",
            Kind = ActorKind.Guest,
            Roles = [],
            Permissions = [],
            Consent = ConsentState.AllDisabled,
        };

        return actorName switch
        {
            "guest" => guest,
            "customer" => guest with { UserId = "customer-1", Kind = ActorKind.Customer },
            "staff-orders-read" => guest with { UserId = "staff-1", Kind = ActorKind.Staff, Permissions = [Permissions.OrdersRead] },
            "staff-orders-full" => guest with { UserId = "staff-2", Kind = ActorKind.Staff, Permissions = [Permissions.OrdersRead, Permissions.OrdersCancel] },
            _ => throw new ArgumentOutOfRangeException(nameof(actorName), actorName, "Actor thử không xác định."),
        };
    }
}
