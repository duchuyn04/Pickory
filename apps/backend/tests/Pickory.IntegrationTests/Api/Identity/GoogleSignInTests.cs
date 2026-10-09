using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.GoogleOAuth;
using Pickory.Modules.Identity.Tokens;
using Xunit;

namespace Pickory.IntegrationTests.Api.Identity;

// Port từ test ID-09 và ID-17 của bản Node cho đăng nhập Google và chống chiếm trước tài khoản.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class GoogleSignInTests : IClassFixture<PickoryWebApplicationFactory>
{
    // Callback lấy từ `API_BASE_URL` của môi trường test, không lấy từ header `Host`.
    private const string CallbackUri = "http://localhost:3000/api/auth/google/callback";
    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public GoogleSignInTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        // Test đọc header `Location`. Client không được tự đi theo chuyển hướng.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        // Tạo khách hàng qua Google cần role `customer` có sẵn trong DB.
        IdentityTestData.SeedRolesAsync(database).GetAwaiter().GetResult();
    }

    [Fact]
    // Bước bắt đầu trả URL Google có PKCE (Proof Key for Code Exchange) và lưu state trong cookie.
    public async Task Start_JsonModeReturnsPkceUrlAndStoresStateCookie()
    {
        // Chế độ JSON trả URL thay vì chuyển hướng, nên test đọc được từng tham số.
        var response = await _client.GetAsync("/api/auth/google/start?json=1&returnTo=/account/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        var authUrl = new Uri(data.GetProperty("authUrl").GetString()!);
        var state = data.GetProperty("state").GetString()!;
        // URL trỏ đúng trang đồng ý của Google với tham số giống bản Node.
        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth", authUrl.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(authUrl.Query);
        Assert.Equal("mock_google_client_id", query["client_id"]);
        Assert.Equal(CallbackUri, query["redirect_uri"]);
        // Luồng code giữ token ngoài trình duyệt. Scope chỉ xin định danh, email và tên.
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("openid email profile", query["scope"]);
        // State trên URL khớp state trong body. Callback dùng state này để chống CSRF (Cross-Site Request Forgery).
        Assert.Equal(state, query["state"]);
        Assert.Equal("S256", query["code_challenge_method"]);

        // Cookie state sống 10 phút. Script không đọc được và trang khác không gửi kèm cookie này.
        var stateCookie = Assert.Single(ActorHttp.ReadSetCookies(response, "pk_oauth_state"));
        Assert.Contains("max-age=600", stateCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", stateCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", stateCookie, StringComparison.OrdinalIgnoreCase);
        // Cookie giữ state và `returnTo` nội bộ cho bước callback.
        using var storedState = JsonDocument.Parse(Uri.UnescapeDataString(ActorHttp.ReadCookieValue(stateCookie)));
        Assert.Equal(state, storedState.RootElement.GetProperty("state").GetString());
        Assert.Equal("/account/orders", storedState.RootElement.GetProperty("returnTo").GetString());
        // Google chỉ nhận challenge. Verifier bí mật nằm trong cookie, và challenge phải là SHA-256 của verifier.
        var codeVerifier = storedState.RootElement.GetProperty("codeVerifier").GetString()!;
        Assert.Equal(Base64UrlSha256(codeVerifier), query["code_challenge"]);
    }

    [Fact]
    // Trình duyệt thường nhận chuyển hướng 302 sang Google, giống bản Node.
    public async Task Start_WithoutJsonModeRedirectsToGoogle()
    {
        var response = await _client.GetAsync("/api/auth/google/start");

        // Header `Location` trỏ tới trang đồng ý của Google.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("//evil.example")]
    [InlineData("https://evil.example/steal")]
    // `returnTo` trỏ ra ngoài bị bỏ. Kẻ xấu không dùng luồng đăng nhập để chuyển người dùng sang trang lạ.
    public async Task Start_DropsExternalReturnTo(string returnTo)
    {
        var response = await _client.GetAsync($"/api/auth/google/start?json=1&returnTo={Uri.EscapeDataString(returnTo)}");

        var stateCookie = Assert.Single(ActorHttp.ReadSetCookies(response, "pk_oauth_state"));
        // Cookie không có trường `returnTo`. Callback sẽ chuyển về trang chủ của web.
        using var storedState = JsonDocument.Parse(Uri.UnescapeDataString(ActorHttp.ReadCookieValue(stateCookie)));
        Assert.False(storedState.RootElement.TryGetProperty("returnTo", out _));
    }

    [Theory]
    [InlineData("/api/auth/google/callback?state=s1")]
    [InlineData("/api/auth/google/callback?code=c1")]
    // Callback thiếu code hoặc state trả 400. Google không trả kết quả hợp lệ, ví dụ khi người dùng từ chối.
    public async Task Callback_MissingCodeOrStateReturns400(string path)
    {
        var response = await _client.GetAsync(path);

        // Mã và thông điệp giữ nguyên bản Node.
        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Thiếu mã code hoặc state từ Google.");
    }

    [Theory]
    [InlineData("wrong-state", true)]
    [InlineData("valid-state", false)]
    // State sai hoặc thiếu cookie state bị từ chối. Kẻ tấn công không gắn code của mình vào trình duyệt nạn nhân.
    public async Task Callback_StateMismatchOrMissingCookieIsRejectedAndCookieCleared(string returnedState, bool sendCookie)
    {
        // Trường hợp đầu gửi state khác cookie. Trường hợp hai không gửi cookie.
        var cookieHeader = sendCookie ? StateCookie("valid-state", "verifier-1") : null;

        var response = await _client.SendAsync(ActorHttp.CreateRequest(
            HttpMethod.Get, $"/api/auth/google/callback?code=c1&state={returnedState}", cookieHeader));

        await RegisterTests.AssertErrorAsync(
            response, HttpStatusCode.BadRequest, "INVALID_OAUTH_STATE", "Mã state không hợp lệ hoặc phiên đăng nhập Google đã hết hạn.");
        // API xóa cookie state kể cả khi thất bại. State chỉ dùng một lần.
        var clearedCookie = Assert.Single(ActorHttp.ReadSetCookies(response, "pk_oauth_state"));
        Assert.Contains("expires=Thu, 01 Jan 1970 00:00:00 GMT", clearedCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    // Người dùng Google mới thành khách hàng có phiên. API phát `UserLinkedEvent` để gộp dữ liệu của mã ẩn danh.
    public async Task Callback_NewGoogleUserCreatesCustomerAndPublishesUserLinked()
    {
        var email = IdentityTestData.NewEmail("google-new");
        // Google trả email viết hoa. API phải chuẩn hóa email trước khi lưu.
        _factory.GoogleClient.UserInfo = new GoogleUserInfo($"sub-{Guid.NewGuid():N}", email.ToUpperInvariant(), EmailVerified: true, "Google User");
        var anonymousId = Guid.NewGuid().ToString();

        var response = await CallbackJsonAsync($"{StateCookie("state-1", "verifier-1")}; pk_anon={anonymousId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var user = document.RootElement.GetProperty("data").GetProperty("user");
        var userId = user.GetProperty("id").GetString()!;
        // Body chứa email đã chuyển về chữ thường và tên lấy từ Google.
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.Equal("Google User", user.GetProperty("name").GetString());
        Assert.Equal("customer", user.GetProperty("kind").GetString());
        // Phiên khách hàng có hạn 30 ngày, giống đăng nhập bằng mật khẩu.
        Assert.Contains("max-age=2592000", Assert.Single(ActorHttp.ReadSetCookies(response, "pk_session")), StringComparison.OrdinalIgnoreCase);
        // API gửi verifier trong cookie cho Google. Google chỉ đổi code khi verifier khớp challenge.
        Assert.Contains(_factory.GoogleClient.Exchanges, exchange => exchange == ("code-1", "verifier-1", CallbackUri));

        await using var identity = _database.CreateIdentityContext();
        var stored = await identity.Users.Include(value => value.OAuthAccounts).Include(value => value.Roles).ThenInclude(value => value.Role).SingleAsync(value => value.Id == userId);
        // Tài khoản chỉ dùng Google nên không có mật khẩu. Cờ xác thực email lấy theo Google.
        Assert.True(stored.EmailVerified);
        Assert.Null(stored.PasswordHash);
        // Tài khoản chỉ có role `customer`. Liên kết Google lưu `sub` để lần sau tìm đúng tài khoản.
        Assert.Equal(["customer"], stored.Roles.Select(value => value.Role!.Code));
        Assert.Equal(_factory.GoogleClient.UserInfo.Sub, Assert.Single(stored.OAuthAccounts).ProviderUserId);
        // Sự kiện mang mã ẩn danh trong cookie. Module khác gộp giỏ hàng của khách vào tài khoản.
        Assert.Contains(_factory.DomainEvents.OfType<UserLinkedEvent>(), value => value.UserId == userId && value.AnonymousId == anonymousId);
    }

    [Theory]
    [InlineData("/orders/42", "http://localhost:5173/orders/42")]
    [InlineData(null, "http://localhost:5173")]
    // Callback thường chuyển hướng về `returnTo` nội bộ trên web. Thiếu `returnTo` thì về trang chủ.
    public async Task Callback_WithoutJsonModeRedirectsToWebReturnTo(string? returnTo, string expectedLocation)
    {
        _factory.GoogleClient.UserInfo = new GoogleUserInfo($"sub-{Guid.NewGuid():N}", IdentityTestData.NewEmail("google-redirect"), EmailVerified: true, null);

        var response = await _client.SendAsync(ActorHttp.CreateRequest(
            HttpMethod.Get, "/api/auth/google/callback?code=code-1&state=state-1", StateCookie("state-1", "verifier-1", returnTo)));

        // Địa chỉ đích ghép từ `WEB_ORIGIN`. Người dùng không bị đưa sang miền khác.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        // Đọc chuỗi gốc của header. `Uri` tự thêm dấu gạch chéo cuối, nên không cho biết API gửi gì.
        Assert.Equal(expectedLocation, response.Headers.NonValidated["Location"].ToString());
    }

    [Fact]
    // Khách hàng đã xác thực email được liên kết với Google. API giữ mật khẩu và phiên cũ vì chủ email đã được chứng minh.
    public async Task Callback_VerifiedExistingCustomerIsLinkedAndKeepsPasswordAndSessions()
    {
        var email = IdentityTestData.NewEmail("google-verified");
        // Tài khoản có mật khẩu, email đã xác thực và một phiên đang hoạt động.
        var existing = await IdentityTestData.CreateUserAsync(_database, email, UserKind.Customer, "customer", emailVerified: true);
        var oldSession = await AddSessionAsync(existing.Id);
        _factory.GoogleClient.UserInfo = new GoogleUserInfo($"sub-{Guid.NewGuid():N}", email, EmailVerified: true, "Google User");

        var response = await CallbackJsonAsync(StateCookie("state-1", "verifier-1"));

        // API đăng nhập vào tài khoản có sẵn, không tạo tài khoản mới.
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal(existing.Id, document.RootElement.GetProperty("data").GetProperty("user").GetProperty("id").GetString());
        // Mật khẩu và phiên cũ còn nguyên. Tài khoản có thêm đúng một liên kết Google.
        await using var identity = _database.CreateIdentityContext();
        var stored = await identity.Users.Include(value => value.OAuthAccounts).SingleAsync(value => value.Id == existing.Id);
        Assert.NotNull(stored.PasswordHash);
        Assert.Single(stored.OAuthAccounts);
        Assert.True(await identity.Sessions.AnyAsync(value => value.TokenHash == SecureTokens.HashToken(oldSession)));
    }

    [Fact]
    // Tài khoản chưa xác thực email có thể do kẻ xấu đăng ký trước. Google xác nhận chủ email thật, nên kẻ đăng ký trước mất mọi quyền truy cập.
    public async Task Callback_UnverifiedExistingCustomerLosesPreHijackerAccess()
    {
        var email = IdentityTestData.NewEmail("google-prehijack");
        // Kẻ đăng ký trước giữ mật khẩu, một phiên và một token đặt lại mật khẩu còn hạn.
        var existing = await IdentityTestData.CreateUserAsync(_database, email, UserKind.Customer, "customer", emailVerified: false);
        var attackerSession = await AddSessionAsync(existing.Id);
        await using (var setup = _database.CreateIdentityContext())
        {
            setup.EmailTokens.Add(new EmailToken
            {
                TokenHash = SecureTokens.HashToken(SecureTokens.GenerateToken()),
                UserId = existing.Id,
                Purpose = EmailTokenPurpose.Reset,
                ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            });
            await setup.SaveChangesAsync();
        }

        _factory.GoogleClient.UserInfo = new GoogleUserInfo($"sub-{Guid.NewGuid():N}", email, EmailVerified: true, "Victim");

        var response = await CallbackJsonAsync(StateCookie("state-1", "verifier-1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var identity = _database.CreateIdentityContext();
        var stored = await identity.Users.Include(value => value.OAuthAccounts).Include(value => value.EmailTokens).SingleAsync(value => value.Id == existing.Id);
        // Mật khẩu do kẻ đăng ký trước đặt bị xóa. Chủ email thật dùng Google hoặc đặt lại mật khẩu sau.
        Assert.Null(stored.PasswordHash);
        // Tài khoản được đánh dấu đã xác thực và có liên kết Google của chủ email.
        Assert.True(stored.EmailVerified);
        Assert.Single(stored.OAuthAccounts);
        // Token còn hạn bị vô hiệu. Kẻ đăng ký trước không đặt lại được mật khẩu.
        Assert.All(stored.EmailTokens, token => Assert.NotNull(token.UsedAt));
        // Phiên của kẻ đăng ký trước bị xóa.
        Assert.False(await identity.Sessions.AnyAsync(value => value.TokenHash == SecureTokens.HashToken(attackerSession)));
    }

    [Fact]
    // Email Google chưa xác thực không liên kết được với tài khoản có sẵn. Ai cũng tạo được tài khoản Google với email người khác.
    public async Task Callback_UnverifiedGoogleEmailCannotLinkExistingAccount()
    {
        var email = IdentityTestData.NewEmail("google-unverified");
        await IdentityTestData.CreateUserAsync(_database, email, UserKind.Customer, "customer", password: null);
        // Google trả cùng email nhưng cờ xác thực là false.
        _factory.GoogleClient.UserInfo = new GoogleUserInfo($"sub-{Guid.NewGuid():N}", email, EmailVerified: false, null);

        var response = await CallbackJsonAsync(StateCookie("state-1", "verifier-1"));

        // API trả 400 `UNVERIFIED_EMAIL` với thông điệp giống bản Node.
        await RegisterTests.AssertErrorAsync(
            response, HttpStatusCode.BadRequest, "UNVERIFIED_EMAIL", "Email Google chưa được xác thực, không thể tự động liên kết với tài khoản hiện có.");
    }

    [Fact]
    // Nhân viên không đăng nhập bằng Google. Tài khoản quyền lớn chỉ có một đường vào bằng mật khẩu.
    public async Task Callback_StaffEmailIsForbidden()
    {
        var email = IdentityTestData.NewEmail("google-staff");
        await IdentityTestData.CreateUserAsync(_database, email, UserKind.Staff, "admin");
        _factory.GoogleClient.UserInfo = new GoogleUserInfo($"sub-{Guid.NewGuid():N}", email, EmailVerified: true, null);

        var response = await CallbackJsonAsync(StateCookie("state-1", "verifier-1"));

        // API trả 403 dù email Google đã xác thực.
        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN", "Tài khoản nhân viên không được phép đăng nhập bằng Google.");
    }

    [Fact]
    // Tài khoản bị khóa không đăng nhập được bằng Google. Google không là đường vòng qua lệnh khóa.
    public async Task Callback_LockedAccountIsRejected()
    {
        var email = IdentityTestData.NewEmail("google-locked");
        await IdentityTestData.CreateUserAsync(_database, email, UserKind.Customer, "customer", emailVerified: true, locked: true);
        _factory.GoogleClient.UserInfo = new GoogleUserInfo($"sub-{Guid.NewGuid():N}", email, EmailVerified: true, null);

        var response = await CallbackJsonAsync(StateCookie("state-1", "verifier-1"));

        // Thông điệp chung cho tài khoản bị khóa và bị xóa. Người ngoài không phân biệt được hai trạng thái.
        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Tài khoản không tồn tại hoặc đã bị khóa.");
    }

    [Fact]
    // API tìm tài khoản theo `sub` trước email. Người dùng đổi email Google vẫn vào đúng tài khoản cũ.
    public async Task Callback_KnownGoogleSubjectSignsIntoLinkedAccountAfterGoogleEmailChange()
    {
        var originalEmail = IdentityTestData.NewEmail("google-linked");
        var existing = await IdentityTestData.CreateUserAsync(_database, originalEmail, UserKind.Customer, "customer", emailVerified: true);
        var subject = $"sub-{Guid.NewGuid():N}";
        await using (var setup = _database.CreateIdentityContext())
        {
            setup.OAuthAccounts.Add(new OAuthAccount { Provider = "google", ProviderUserId = subject, UserId = existing.Id });
            await setup.SaveChangesAsync();
        }

        // Google trả cùng `sub` nhưng email mới, không khớp email nào trong DB.
        _factory.GoogleClient.UserInfo = new GoogleUserInfo(subject, IdentityTestData.NewEmail("google-renamed"), EmailVerified: true, null);

        var response = await CallbackJsonAsync(StateCookie("state-1", "verifier-1"));

        // API đăng nhập vào tài khoản đã liên kết, không tạo tài khoản mới theo email mới.
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal(existing.Id, document.RootElement.GetProperty("data").GetProperty("user").GetProperty("id").GetString());
    }

    // Gọi callback ở chế độ JSON. Test đọc hồ sơ trong body thay vì header `Location`.
    private Task<HttpResponseMessage> CallbackJsonAsync(string cookieHeader)
    {
        var request = ActorHttp.CreateRequest(HttpMethod.Get, "/api/auth/google/callback?code=code-1&state=state-1&json=1", cookieHeader);
        request.Headers.Add("Accept", "application/json");
        return _client.SendAsync(request);
    }

    // Bản Node lưu state dạng JSON đã mã hóa URL trong cookie `pk_oauth_state`.
    private static string StateCookie(string state, string codeVerifier, string? returnTo = null)
    {
        // Bỏ trường `returnTo` khi không có giá trị, giống cookie do API tạo.
        var json = returnTo is null
            ? JsonSerializer.Serialize(new { state, codeVerifier })
            : JsonSerializer.Serialize(new { state, codeVerifier, returnTo });
        return $"pk_oauth_state={Uri.EscapeDataString(json)}";
    }

    // Tạo phiên còn hạn 30 ngày. Test kiểm tra API giữ hoặc xóa phiên này.
    private async Task<string> AddSessionAsync(string userId)
    {
        var rawToken = SecureTokens.GenerateToken();
        await using var identity = _database.CreateIdentityContext();
        identity.Sessions.Add(new Session { TokenHash = SecureTokens.HashToken(rawToken), UserId = userId, ExpiresAt = DateTime.UtcNow.AddDays(30) });
        await identity.SaveChangesAsync();
        return rawToken;
    }

    // Tính challenge S256 độc lập với code đang kiểm tra. Lỗi trong `Pkce` không tự che lỗi của chính nó.
    private static string Base64UrlSha256(string value)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(value))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
