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
    // Nguồn web khớp `WEB_ORIGIN` của môi trường test. Request ghi từ nguồn này qua bước kiểm tra nguồn.
    private const string WebOrigin = "http://localhost:5173";

    // Test tự đọc và gửi cookie. Client không được tự lưu cookie giữa các request.
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    [Fact]
    // Lần ghé thăm đầu tiên nhận đúng một cookie `pk_anon` với thuộc tính giống bản Node.
    public async Task FirstVisit_IssuesAnonymousCookieWithNodeAttributes()
    {
        // Gửi request không có cookie, giống khách mở trang lần đầu.
        var response = await _client.GetAsync("/api/me");

        // API cấp đúng một cookie ẩn danh. Hai cookie cùng tên làm trình duyệt giữ giá trị không xác định.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var anonymousCookie = Assert.Single(ActorHttp.ReadSetCookies(response, "pk_anon"));
        var anonymousId = ActorHttp.ReadCookieValue(anonymousCookie);
        // Mã ẩn danh là UUID (Universally Unique Identifier) phiên bản 4. Giá trị ngẫu nhiên không cho đoán mã của khách khác.
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", anonymousId);
        // Cookie sống một năm trên mọi đường dẫn. Giỏ hàng của khách còn khi khách quay lại.
        Assert.Contains("path=/", anonymousCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=31536000", anonymousCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", anonymousCookie, StringComparison.OrdinalIgnoreCase);
        // `HttpOnly` chặn script đọc cookie. `SameSite=Lax` chặn trang khác gửi cookie trong request ghi.
        Assert.Contains("httponly", anonymousCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", anonymousCookie, StringComparison.OrdinalIgnoreCase);
        // Môi trường test là Development. Cookie không có `Secure` vì dev chạy HTTP.
        Assert.DoesNotContain("secure", anonymousCookie, StringComparison.OrdinalIgnoreCase);

        // Actor của chính request này dùng mã vừa cấp. Request đầu tiên không tạo hai mã ẩn danh khác nhau.
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal(anonymousId, document.RootElement.GetProperty("data").GetProperty("anonymousId").GetString());
    }

    [Fact]
    // Khách quay lại giữ mã ẩn danh cũ. API không cấp cookie mới và không làm mất giỏ hàng.
    public async Task ReturningVisit_KeepsAnonymousIdWithoutNewCookie()
    {
        var anonymousId = Guid.NewGuid().ToString();

        // Gửi cookie ẩn danh có sẵn, giống trình duyệt của khách đã ghé thăm trước đó.
        var response = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", $"pk_anon={anonymousId}"));

        // API không ghi đè cookie đang có. Ghi đè sẽ tách khách khỏi dữ liệu cũ.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(ActorHttp.ReadSetCookies(response, "pk_anon"));
        // Actor dùng đúng mã trong cookie.
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal(anonymousId, document.RootElement.GetProperty("data").GetProperty("anonymousId").GetString());
    }

    [Fact]
    // `GET /api/me` của khách vãng lai trả đủ trường như bản Node. Frontend đọc mọi trường mà không kiểm tra thiếu.
    public async Task GetMe_GuestShapeMatchesNode()
    {
        var response = await _client.GetAsync("/api/me");

        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        // Trường thông tin tài khoản có mặt với giá trị null, không bị bỏ khỏi JSON.
        Assert.Equal(JsonValueKind.Null, data.GetProperty("userId").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("email").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("name").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("phone").ValueKind);
        // Khách vãng lai không có role và không có quyền nào.
        Assert.Equal("guest", data.GetProperty("kind").GetString());
        Assert.Equal(0, data.GetProperty("roles").GetArrayLength());
        Assert.Equal(0, data.GetProperty("permissions").GetArrayLength());
        // Khách chưa chọn consent thì mọi lựa chọn đều tắt.
        Assert.False(data.GetProperty("consent").GetProperty("personalization").GetBoolean());
        Assert.False(data.GetProperty("consent").GetProperty("marketing").GetBoolean());
    }

    [Fact]
    // Khách vãng lai nhận consent mới nhất của mã ẩn danh. Lựa chọn cũ không ghi đè lựa chọn mới.
    public async Task GetMe_GuestUsesLatestConsentOfAnonymousId()
    {
        var anonymousId = Guid.NewGuid().ToString();
        // Ghi hai bản consent có giá trị ngược nhau. Kết quả cho biết API chọn bản theo thời gian tạo.
        await using (var identity = database.CreateIdentityContext())
        {
            identity.ConsentRecords.AddRange(
                new ConsentRecord { AnonymousId = anonymousId, Personalization = false, Marketing = true, PolicyVersion = "v1", CreatedAt = DateTime.UtcNow.AddDays(-2) },
                new ConsentRecord { AnonymousId = anonymousId, Personalization = true, Marketing = false, PolicyVersion = "v1", CreatedAt = DateTime.UtcNow.AddDays(-1) });
            await identity.SaveChangesAsync();
        }

        var response = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", $"pk_anon={anonymousId}"));

        // Giá trị khớp bản tạo một ngày trước, không khớp bản tạo hai ngày trước.
        using var document = await ActorHttp.ReadJsonAsync(response);
        var consent = document.RootElement.GetProperty("data").GetProperty("consent");
        Assert.True(consent.GetProperty("personalization").GetBoolean());
        Assert.False(consent.GetProperty("marketing").GetBoolean());
    }

    [Fact]
    // Request đọc từ nguồn lạ không bị chặn. Request đọc không đổi dữ liệu nên không cần chống CSRF (Cross-Site Request Forgery).
    public async Task Get_WithForeignOrigin_IsNotBlocked()
    {
        // Gắn nguồn lạ vào request `GET`.
        var request = ActorHttp.CreateRequest(HttpMethod.Get, "/api/me");
        request.Headers.Add("Origin", "https://malicious-site.com");

        var response = await _client.SendAsync(request);

        // API trả 200. Bước kiểm tra nguồn chỉ áp dụng cho request ghi.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    // Mọi request ghi từ nguồn lạ nhận 403. Trang khác không gửi được request ghi nhân danh người dùng (CSRF).
    public async Task MutatingRequest_WithForeignOrigin_Returns403JsonWithoutAnonymousCookie(string method)
    {
        // Gắn nguồn lạ vào request ghi, giống form của trang tấn công.
        var request = ActorHttp.CreateRequest(new HttpMethod(method), "/api/health");
        request.Headers.Add("Origin", "https://malicious-site.com");

        var response = await _client.SendAsync(request);

        // API trả 403 dạng JSON với mã và thông điệp giống bản Node.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("FORBIDDEN_ORIGIN", error.GetProperty("code").GetString());
        Assert.Equal("Yêu cầu bị từ chối do thiếu hoặc Origin/Referer không hợp lệ.", error.GetProperty("message").GetString());
        // Bản Node kiểm tra nguồn trước khi cấp cookie ẩn danh.
        Assert.Empty(ActorHttp.ReadSetCookies(response, "pk_anon"));
    }

    [Fact]
    // Request ghi thiếu cả `Origin` và `Referer` bị chặn. API không chứng minh được request đến từ trang web.
    public async Task Post_WithoutOriginAndReferer_Returns403()
    {
        var response = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Post, "/api/health"));

        // API trả 403 với mã `FORBIDDEN_ORIGIN`, không cho request đi tiếp.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("FORBIDDEN_ORIGIN", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // `Referer` từ trang web thay được `Origin`. Một số trình duyệt không gửi `Origin` cho request ghi.
    public async Task Post_WithWebReferer_PassesOriginCheck()
    {
        // Chỉ gửi `Referer` có đường dẫn trang. API chỉ so phần nguồn của URL.
        var request = ActorHttp.CreateRequest(HttpMethod.Post, "/api/health");
        request.Headers.Referrer = new Uri($"{WebOrigin}/some-page");

        var response = await _client.SendAsync(request);

        // `POST /api/health` không có route. API trả 404 thay vì 403 khi request đã qua bước kiểm tra nguồn.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
