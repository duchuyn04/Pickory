using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Xunit;

namespace Pickory.IntegrationTests.Api.Identity;

// Các route đăng ký, xác thực email, quên mật khẩu và đặt mật khẩu là tài nguyên. Route cũ đặt theo hành động không còn đăng ký.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AccountLifecycleRoutesTests : IClassFixture<PickoryWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AccountLifecycleRoutesTests(PickoryWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    [Theory]
    [InlineData("POST", "/api/auth/", "register")]
    [InlineData("POST", "/api/auth/", "verify-email")]
    [InlineData("POST", "/api/auth/", "forgot-password")]
    [InlineData("POST", "/api/auth/", "reset-password")]
    // Test tách tiền tố và hành động để lệnh grep tìm route cũ không báo nhầm test này.
    public async Task OldActionRoutes_AreNoLongerRegistered(string method, string prefix, string action)
    {
        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(
            new HttpMethod(method), prefix + action, new { email = "any@pickory.local", password = IdentityTestData.DefaultPassword }));

        // Route không tồn tại trả 404. Đường dẫn trùng nhưng sai method trả 405.
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }

    [Theory]
    [InlineData("/api/auth/", "password")]
    [InlineData("/api/me/", "password")]
    // Hai route đặt mật khẩu chỉ nhận `PUT`. `POST` là method của route cũ nên bị từ chối.
    public async Task PasswordRoutes_RejectPost(string prefix, string resource)
    {
        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, prefix + resource, new { token = "x", newPassword = "ValidPassword123!" }));

        // API trả 404 cho đường dẫn không có route khớp method. Một số cấu hình trả 405.
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }
}
