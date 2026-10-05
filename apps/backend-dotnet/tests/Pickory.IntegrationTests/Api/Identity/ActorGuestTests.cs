using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Entities;
using Xunit;

namespace Pickory.IntegrationTests.Api.Identity;

// Hợp đồng của khách vãng lai theo test ID-02 của bản Node: cookie `pk_anon`, kiểm tra nguồn và `GET /api/me`.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ActorGuestTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>
{
    private const string WebOrigin = "http://localhost:5173";

    // Test tự đọc và gửi cookie. Client không được tự lưu cookie giữa các request.
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    [Fact]
    public async Task FirstVisit_IssuesAnonymousCookieWithNodeAttributes()
    {
        var response = await _client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var anonymousCookie = Assert.Single(ActorHttp.ReadSetCookies(response, "pk_anon"));
        var anonymousId = ActorHttp.ReadCookieValue(anonymousCookie);
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", anonymousId);
        Assert.Contains("path=/", anonymousCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=31536000", anonymousCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", anonymousCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", anonymousCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", anonymousCookie, StringComparison.OrdinalIgnoreCase);
        // Môi trường test là Development. Cookie không có `Secure` vì dev chạy HTTP.
        Assert.DoesNotContain("secure", anonymousCookie, StringComparison.OrdinalIgnoreCase);

        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal(anonymousId, document.RootElement.GetProperty("data").GetProperty("anonymousId").GetString());
    }

    [Fact]
    public async Task ReturningVisit_KeepsAnonymousIdWithoutNewCookie()
    {
        var anonymousId = Guid.NewGuid().ToString();

        var response = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", $"pk_anon={anonymousId}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(ActorHttp.ReadSetCookies(response, "pk_anon"));
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal(anonymousId, document.RootElement.GetProperty("data").GetProperty("anonymousId").GetString());
    }

    [Fact]
    public async Task GetMe_GuestShapeMatchesNode()
    {
        var response = await _client.GetAsync("/api/me");

        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("userId").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("email").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("name").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("phone").ValueKind);
        Assert.Equal("guest", data.GetProperty("kind").GetString());
        Assert.Equal(0, data.GetProperty("roles").GetArrayLength());
        Assert.Equal(0, data.GetProperty("permissions").GetArrayLength());
        Assert.False(data.GetProperty("consent").GetProperty("personalization").GetBoolean());
        Assert.False(data.GetProperty("consent").GetProperty("marketing").GetBoolean());
    }

    [Fact]
    public async Task GetMe_GuestUsesLatestConsentOfAnonymousId()
    {
        var anonymousId = Guid.NewGuid().ToString();
        await using (var identity = database.CreateIdentityContext())
        {
            identity.ConsentRecords.AddRange(
                new ConsentRecord { AnonymousId = anonymousId, Personalization = false, Marketing = true, PolicyVersion = "v1", CreatedAt = DateTime.UtcNow.AddDays(-2) },
                new ConsentRecord { AnonymousId = anonymousId, Personalization = true, Marketing = false, PolicyVersion = "v1", CreatedAt = DateTime.UtcNow.AddDays(-1) });
            await identity.SaveChangesAsync();
        }

        var response = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", $"pk_anon={anonymousId}"));

        using var document = await ActorHttp.ReadJsonAsync(response);
        var consent = document.RootElement.GetProperty("data").GetProperty("consent");
        Assert.True(consent.GetProperty("personalization").GetBoolean());
        Assert.False(consent.GetProperty("marketing").GetBoolean());
    }

    [Fact]
    public async Task Get_WithForeignOrigin_IsNotBlocked()
    {
        var request = ActorHttp.CreateRequest(HttpMethod.Get, "/api/me");
        request.Headers.Add("Origin", "https://malicious-site.com");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task MutatingRequest_WithForeignOrigin_Returns403JsonWithoutAnonymousCookie(string method)
    {
        var request = ActorHttp.CreateRequest(new HttpMethod(method), "/api/health");
        request.Headers.Add("Origin", "https://malicious-site.com");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("FORBIDDEN_ORIGIN", error.GetProperty("code").GetString());
        Assert.Equal("Yêu cầu bị từ chối do thiếu hoặc Origin/Referer không hợp lệ.", error.GetProperty("message").GetString());
        // Bản Node kiểm tra nguồn trước khi cấp cookie ẩn danh.
        Assert.Empty(ActorHttp.ReadSetCookies(response, "pk_anon"));
    }

    [Fact]
    public async Task Post_WithoutOriginAndReferer_Returns403()
    {
        var response = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Post, "/api/health"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("FORBIDDEN_ORIGIN", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Post_WithWebReferer_PassesOriginCheck()
    {
        var request = ActorHttp.CreateRequest(HttpMethod.Post, "/api/health");
        request.Headers.Referrer = new Uri($"{WebOrigin}/some-page");

        var response = await _client.SendAsync(request);

        // `POST /api/health` không có route. API trả 404 thay vì 403 khi request đã qua bước kiểm tra nguồn.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
