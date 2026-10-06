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
// Test dựng app nhỏ với middleware lỗi thật và không cần DB.
public sealed class ActorAuthorizationEndpointTests : IAsyncLifetime
{
    // Header riêng của test thay cho actor middleware thật. Test chọn actor mà không cần phiên trong DB.
    private const string ActorHeaderName = "X-Test-Actor";
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        _app = builder.Build();
        // Dùng middleware lỗi thật. Test kiểm tra đúng JSON lỗi mà client nhận.
        _app.UseMiddleware<ExceptionHandlingMiddleware>();
        // Gắn actor trước khi route chạy, giống vị trí của actor middleware trong app thật.
        _app.Use(async (context, next) =>
        {
            context.SetActor(CreateActor(context.Request.Headers[ActorHeaderName].ToString()));
            await next(context);
        });
        // Mỗi route dùng một bộ lọc quyền. Một test chỉ kiểm tra một quy tắc.
        _app.MapGet("/signed-in", () => Results.Ok()).RequireAuth();
        _app.MapGet("/staff-area", () => Results.Ok()).RequireStaff();
        // Route này cần hai quyền. Test kiểm tra quy tắc phải đủ mọi quyền.
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
    // Khách chưa đăng nhập nhận 401 ở mọi bộ lọc. API kiểm tra đăng nhập trước khi kiểm tra quyền.
    public async Task Guest_Receives401Unauthorized(string path)
    {
        var response = await SendAsync(path, actorName: "guest");

        // Mã và thông điệp giữ nguyên bản Node vì frontend hiển thị trực tiếp thông điệp này.
        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Vui lòng đăng nhập để tiếp tục.");
    }

    [Fact]
    // Khách hàng đã đăng nhập qua `RequireAuth`. Bộ lọc không chặn nhầm người dùng hợp lệ.
    public async Task Customer_PassesRequireAuth()
    {
        var response = await SendAsync("/signed-in", actorName: "customer");

        // Handler của route chạy và trả 200.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    // Khách hàng đã đăng nhập vào khu vực nhân viên nhận 403, không nhận 401.
    public async Task Customer_Receives403OnStaffArea()
    {
        var response = await SendAsync("/staff-area", actorName: "customer");

        // API đã biết người gửi nên trả 403 kèm thông điệp khu vực, giống bản Node.
        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN", "Bạn không có quyền truy cập khu vực này.");
    }

    [Fact]
    // Nhân viên vào khu vực nhân viên mà không cần quyền cụ thể nào.
    public async Task Staff_PassesRequireStaff()
    {
        var response = await SendAsync("/staff-area", actorName: "staff-orders-read");

        // Handler của route chạy và trả 200.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    // Nhân viên thiếu một trong hai quyền bị từ chối. Một quyền đúng không đủ cho route cần nhiều quyền.
    public async Task StaffMissingOneOfTwoPermissions_Receives403()
    {
        var response = await SendAsync("/cancel-order", actorName: "staff-orders-read");

        // Thông điệp thiếu quyền khác thông điệp khu vực. Frontend phân biệt được hai trường hợp.
        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN", "Bạn không có đủ quyền thực hiện thao tác này.");
    }

    [Fact]
    // Nhân viên có đủ mọi quyền qua `RequirePermission`.
    public async Task StaffWithAllPermissions_PassesRequirePermission()
    {
        var response = await SendAsync("/cancel-order", actorName: "staff-orders-full");

        // Handler của route chạy và trả 200.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Gửi request với tên actor trong header. Middleware test đọc header này để chọn actor.
    private async Task<HttpResponseMessage> SendAsync(string path, string actorName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(ActorHeaderName, actorName);
        return await _client.SendAsync(request);
    }

    // Kiểm tra mã HTTP, mã lỗi và thông điệp cùng lúc. Ba giá trị này tạo thành hợp đồng lỗi với frontend.
    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code, string message)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.Equal(message, error.GetProperty("message").GetString());
    }

    // Tạo actor theo tên. Mọi actor dùng chung giá trị gốc nên chỉ khác ở loại người dùng và quyền.
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
            // Tên lạ là lỗi của test. Test dừng ngay thay vì chạy với actor sai.
            _ => throw new ArgumentOutOfRangeException(nameof(actorName), actorName, "Actor thử không xác định."),
        };
    }
}
