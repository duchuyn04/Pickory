using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Throttling;
using Pickory.Modules.Identity.Tokens;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.Api.Identity;

// Port từ test ID-03, ID-05 và ID-06 của bản Node cho đăng nhập và đăng xuất.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class LoginTests : IClassFixture<PickoryWebApplicationFactory>
{
    // Một thông điệp cho mọi lỗi đăng nhập, giữ nguyên bản Node.
    private const string InvalidCredentialsMessage = "Email hoặc mật khẩu không chính xác.";
    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public LoginTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        // Bộ đếm đăng nhập sai là singleton dùng chung. Xóa bộ đếm để test trước không khóa test này.
        factory.Services.GetRequiredService<IdentityRateLimiters>().ClearAll();
    }

    [Fact]
    // Khách hàng đăng nhập nhận phiên 30 ngày. API phát `UserLinkedEvent` để gộp giỏ hàng của mã ẩn danh.
    public async Task CustomerLogin_IssuesThirtyDaySessionAndPublishesUserLinked()
    {
        var email = IdentityTestData.NewEmail("customer-login");
        var user = await IdentityTestData.CreateUserAsync(_database, email, UserKind.Customer, "customer");
        var anonymousId = Guid.NewGuid().ToString();

        // Gửi email viết hoa. API chuẩn hóa email nên vẫn tìm đúng tài khoản.
        var response = await SendAsync("/api/auth/login", email.ToUpperInvariant(), IdentityTestData.DefaultPassword, $"pk_anon={anonymousId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var body = document.RootElement.GetProperty("data").GetProperty("user");
        // Hồ sơ khách hàng có số điện thoại, giống body của bản Node.
        Assert.Equal(user.Id, body.GetProperty("id").GetString());
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Equal("Người dùng thử", body.GetProperty("name").GetString());
        Assert.Equal("0901234567", body.GetProperty("phone").GetString());
        Assert.Equal("customer", body.GetProperty("kind").GetString());

        // Cookie và dòng phiên trong DB cùng có hạn 30 ngày.
        var sessionCookie = Assert.Single(ActorHttp.ReadSetCookies(response, "pk_session"));
        Assert.Contains("max-age=2592000", sessionCookie, StringComparison.OrdinalIgnoreCase);
        var expiresAt = await ReadSessionExpiryAsync(ActorHttp.ReadCookieValue(sessionCookie));
        Assert.Equal(DateTime.UtcNow.AddDays(30), expiresAt!.Value, TimeSpan.FromMinutes(1));

        // Sự kiện phát đúng một lần với mã ẩn danh trong cookie. Phát hai lần có thể gộp giỏ hàng hai lần.
        var linkedEvent = Assert.Single(_factory.DomainEvents.OfType<UserLinkedEvent>(), value => value.UserId == user.Id);
        Assert.Equal(anonymousId, linkedEvent.AnonymousId);
    }

    [Fact]
    // Nhân viên đăng nhập nhận phiên 12 giờ. Phiên bị đánh cắp của tài khoản quyền lớn chỉ dùng được ngắn hạn.
    public async Task StaffLogin_IssuesTwelveHourSessionWithoutUserLinked()
    {
        var email = IdentityTestData.NewEmail("staff-login");
        var user = await IdentityTestData.CreateUserAsync(_database, email, UserKind.Staff, "sales");

        // Nhân viên dùng route đăng nhập riêng của khu vực quản trị.
        var response = await SendAsync("/api/admin/auth/login", email, IdentityTestData.DefaultPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var body = document.RootElement.GetProperty("data").GetProperty("user");
        Assert.Equal(user.Id, body.GetProperty("id").GetString());
        Assert.Equal("staff", body.GetProperty("kind").GetString());
        // Bản Node không trả số điện thoại cho đăng nhập nhân viên.
        Assert.False(body.TryGetProperty("phone", out _));

        // Cookie và dòng phiên trong DB cùng có hạn 12 giờ.
        var sessionCookie = Assert.Single(ActorHttp.ReadSetCookies(response, "pk_session"));
        Assert.Contains("max-age=43200", sessionCookie, StringComparison.OrdinalIgnoreCase);
        var expiresAt = await ReadSessionExpiryAsync(ActorHttp.ReadCookieValue(sessionCookie));
        Assert.Equal(DateTime.UtcNow.AddHours(12), expiresAt!.Value, TimeSpan.FromMinutes(1));
        // Nhân viên không có giỏ hàng khách cần gộp, nên API không phát `UserLinkedEvent`.
        Assert.DoesNotContain(_factory.DomainEvents.OfType<UserLinkedEvent>(), value => value.UserId == user.Id);

        // Phiên mới dùng được ngay. Quyền lấy từ role `sales` đã seed, không có quyền quản trị role.
        var meResponse = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", $"pk_session={ActorHttp.ReadCookieValue(sessionCookie)}"));
        using var meDocument = await ActorHttp.ReadJsonAsync(meResponse);
        var me = meDocument.RootElement.GetProperty("data");
        Assert.Equal(["sales"], me.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
        var permissions = me.GetProperty("permissions").EnumerateArray().Select(permission => permission.GetString()).ToList();
        Assert.Contains("orders:read", permissions);
        Assert.DoesNotContain("roles:manage", permissions);
    }

    [Theory]
    [InlineData("/api/auth/login", UserKind.Staff, "sales")]
    [InlineData("/api/admin/auth/login", UserKind.Customer, "customer")]
    // Tài khoản sai loại trên route đăng nhập nhận lỗi sai thông tin. Route không cho biết email thuộc loại tài khoản nào.
    public async Task Login_WrongAccountKindReturnsInvalidCredentials(string path, UserKind kind, string roleCode)
    {
        var email = IdentityTestData.NewEmail("wrong-kind");
        await IdentityTestData.CreateUserAsync(_database, email, kind, roleCode);

        // Mật khẩu đúng, nhưng route không dành cho loại tài khoản này.
        var response = await SendAsync(path, email, IdentityTestData.DefaultPassword);

        await AssertInvalidCredentialsAsync(response);
    }

    [Theory]
    [InlineData("/api/auth/login", UserKind.Customer, "customer")]
    [InlineData("/api/admin/auth/login", UserKind.Staff, "sales")]
    // Sai email và sai mật khẩu trả cùng lỗi. Kẻ dò không biết email nào đã đăng ký.
    public async Task Login_UnknownEmailAndWrongPasswordShareOneError(string path, UserKind kind, string roleCode)
    {
        var email = IdentityTestData.NewEmail("same-error");
        await IdentityTestData.CreateUserAsync(_database, email, kind, roleCode);

        // Lần đầu dùng email chưa đăng ký. Lần hai dùng email đúng với mật khẩu sai.
        await AssertInvalidCredentialsAsync(await SendAsync(path, IdentityTestData.NewEmail("unknown"), IdentityTestData.DefaultPassword));
        await AssertInvalidCredentialsAsync(await SendAsync(path, email, "WrongPassword999!"));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    // Tài khoản bị khóa, bị xóa hoặc chỉ dùng Google không đăng nhập được bằng mật khẩu.
    public async Task CustomerLogin_LockedDeletedOrPasswordlessAccountIsRejected(bool locked, bool deleted, bool hasPassword)
    {
        var email = IdentityTestData.NewEmail("blocked");
        await IdentityTestData.CreateUserAsync(
            _database, email, UserKind.Customer, "customer", password: hasPassword ? IdentityTestData.DefaultPassword : null, locked: locked, deleted: deleted);

        var response = await SendAsync("/api/auth/login", email, IdentityTestData.DefaultPassword);

        // Mọi trường hợp nhận cùng lỗi sai thông tin. Người ngoài không biết trạng thái của tài khoản.
        await AssertInvalidCredentialsAsync(response);
    }

    [Fact]
    // Sau 5 lần sai, lần thử thứ 6 bị khóa kể cả khi mật khẩu đúng. Kẻ dò không thử tiếp được.
    public async Task CustomerLogin_SixthAttemptAfterFiveFailuresIsLockedEvenWithCorrectPassword()
    {
        var email = IdentityTestData.NewEmail("throttle");
        await IdentityTestData.CreateUserAsync(_database, email, UserKind.Customer, "customer");

        // Năm lần sai đầu tiên vẫn nhận 401, chưa bị khóa.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await AssertInvalidCredentialsAsync(await SendAsync("/api/auth/login", email, "WrongPassword999!"));
        }

        // Gửi mật khẩu đúng ở lần thứ 6.
        var response = await SendAsync("/api/auth/login", email, IdentityTestData.DefaultPassword);

        // API trả 429 với thông điệp khóa 15 phút giống bản Node.
        await RegisterTests.AssertErrorAsync(
            response,
            HttpStatusCode.TooManyRequests,
            "TOO_MANY_ATTEMPTS",
            "Tài khoản tạm thời bị khóa do nhập sai mật khẩu nhiều lần. Vui lòng thử lại sau 15 phút.");
    }

    [Theory]
    [InlineData("khong-phai-email", "ValidPassword123!", "Dữ liệu không hợp lệ (email: Email không đúng định dạng.).")]
    [InlineData("ok@pickory.local", "", "Dữ liệu không hợp lệ (password: Mật khẩu không được để trống.).")]
    // Email sai định dạng hoặc mật khẩu rỗng bị từ chối trước khi API tra cứu tài khoản.
    public async Task Login_InvalidInputReturnsValidationError(string email, string password, string expectedMessage)
    {
        var response = await SendAsync("/api/auth/login", email, password);

        // Thông điệp lỗi kiểm tra dữ liệu có tên trường, giống định dạng của bản Node.
        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", expectedMessage);
    }

    [Fact]
    // Handler sự kiện lỗi không làm đăng nhập thất bại. Lỗi gộp giỏ hàng chỉ bị ghi log.
    public async Task CustomerLogin_FailingEventHandlerDoesNotFailLogin()
    {
        var email = IdentityTestData.NewEmail("failing-handler");
        await IdentityTestData.CreateUserAsync(_database, email, UserKind.Customer, "customer");
        // Tạo app riêng có thêm handler luôn ném lỗi. App dùng chung của các test khác không bị đổi.
        using var failingFactory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IDomainEventHandler<UserLinkedEvent>, FailingUserLinkedHandler>()));
        using var client = failingFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, "/api/auth/login", new { email, password = IdentityTestData.DefaultPassword }));

        // API vẫn trả 200 dù handler ném lỗi.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    // Đăng xuất xóa phiên trong DB và xóa cookie. Bản sao cookie ở nơi khác cũng không dùng lại được.
    public async Task Logout_DeletesSessionAndClearsCookie()
    {
        var email = IdentityTestData.NewEmail("logout");
        await IdentityTestData.CreateUserAsync(_database, email, UserKind.Staff, "sales");
        // Đăng nhập thật để có token phiên do API cấp.
        var sessionToken = ActorHttp.ReadSessionToken(await SendAsync("/api/admin/auth/login", email, IdentityTestData.DefaultPassword));

        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/auth/logout", body: null, $"pk_session={sessionToken}"));

        // Body giống bản Node với `success` là true.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.True(document.RootElement.GetProperty("data").GetProperty("success").GetBoolean());
        // Cookie bị xóa và dòng phiên không còn trong DB.
        var clearedCookie = Assert.Single(ActorHttp.ReadSetCookies(response, "pk_session"));
        Assert.Contains("expires=Thu, 01 Jan 1970 00:00:00 GMT", clearedCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await ReadSessionExpiryAsync(sessionToken));

        // Gửi lại token cũ chỉ nhận quyền khách vãng lai.
        var meResponse = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", $"pk_session={sessionToken}"));
        using var meDocument = await ActorHttp.ReadJsonAsync(meResponse);
        Assert.Equal("guest", meDocument.RootElement.GetProperty("data").GetProperty("kind").GetString());
    }

    [Fact]
    // Đăng xuất khi chưa đăng nhập không phải lỗi. Frontend gọi đăng xuất an toàn ở mọi trạng thái.
    public async Task Logout_WithoutSessionStillSucceeds()
    {
        // Gửi request không có cookie phiên.
        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/auth/logout", body: null));

        // API trả 200 thay vì 401.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Gửi email và mật khẩu tới route đăng nhập. Request có `Origin` của web để qua bước chống CSRF (Cross-Site Request Forgery).
    private Task<HttpResponseMessage> SendAsync(string path, string email, string password, string? cookieHeader = null)
    {
        return _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Post, path, new { email, password }, cookieHeader));
    }

    // Đọc hạn phiên từ DB theo hash của token. Giá trị null nghĩa là API đã xóa dòng phiên.
    private async Task<DateTime?> ReadSessionExpiryAsync(string rawToken)
    {
        var tokenHash = SecureTokens.HashToken(rawToken);
        await using var identity = _database.CreateIdentityContext();
        return await identity.Sessions.Where(value => value.TokenHash == tokenHash).Select(value => (DateTime?)value.ExpiresAt).SingleOrDefaultAsync();
    }

    // Mọi lỗi đăng nhập phải trả cùng 401, cùng mã và cùng thông điệp.
    private static Task AssertInvalidCredentialsAsync(HttpResponseMessage response)
    {
        return RegisterTests.AssertErrorAsync(response, HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS", InvalidCredentialsMessage);
    }

    // Handler giả luôn ném lỗi. Test chứng minh dispatcher cách ly lỗi khỏi luồng đăng nhập.
    private sealed class FailingUserLinkedHandler : IDomainEventHandler<UserLinkedEvent>
    {
        public Task HandleAsync(UserLinkedEvent @event, CancellationToken cancellationToken = default)
        {
            // Ném lỗi ngay. Test mô phỏng module gộp giỏ hàng gặp sự cố.
            throw new InvalidOperationException("Sự cố giả lập trong handler UserLinkedEvent.");
        }
    }
}
