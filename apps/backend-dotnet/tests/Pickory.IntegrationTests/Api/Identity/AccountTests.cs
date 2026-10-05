using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Passwords;
using Pickory.Modules.Identity.Throttling;
using Pickory.Modules.Identity.Tokens;
using Xunit;

namespace Pickory.IntegrationTests.Api.Identity;

// Port từ test ID-10 của bản Node cho sửa hồ sơ, đổi mật khẩu và xóa tài khoản khách.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AccountTests : IClassFixture<PickoryWebApplicationFactory>
{
    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public AccountTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        // Test đăng nhập lại sau khi đổi mật khẩu. Bộ đếm đăng nhập sai bắt đầu trống.
        factory.Services.GetRequiredService<IdentityRateLimiters>().ClearAll();
    }

    [Theory]
    [InlineData("PATCH", "/api/me")]
    [InlineData("POST", "/api/me/password")]
    [InlineData("DELETE", "/api/me")]
    // Ba route tài khoản dùng `RequireAuth`. Khách chưa đăng nhập nhận 401 trước khi route đọc body.
    public async Task AccountRoutes_RequireSignedInUser(string method, string path)
    {
        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(new HttpMethod(method), path, new { }));

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Vui lòng đăng nhập để tiếp tục.");
    }

    [Fact]
    // Người dùng sửa được tên và số điện thoại của chính mình. Response trả hồ sơ mới, không có hash mật khẩu.
    public async Task UpdateProfile_ChangesNameAndPhone()
    {
        var (user, sessionToken) = await CreateCustomerSessionAsync("patch-me");

        var response = await SendAsync(HttpMethod.Patch, "/api/me", new { name = " Tên Mới Đã Sửa ", phone = "0988888888" }, sessionToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var body = document.RootElement.GetProperty("data").GetProperty("user");
        Assert.Equal(user.Id, body.GetProperty("id").GetString());
        // Khoảng trắng hai đầu bị cắt trước khi lưu.
        Assert.Equal("Tên Mới Đã Sửa", body.GetProperty("name").GetString());
        Assert.Equal("0988888888", body.GetProperty("phone").GetString());
        Assert.Equal("customer", body.GetProperty("kind").GetString());
        Assert.False(body.TryGetProperty("passwordHash", out _));
    }

    [Fact]
    // Trường không gửi giữ nguyên. Trường gửi `null` hoặc chuỗi trống bị xóa, giống bản Node.
    public async Task UpdateProfile_DistinguishesMissingFieldFromNull()
    {
        var (user, sessionToken) = await CreateCustomerSessionAsync("patch-null");

        var clearPhoneResponse = await SendRawJsonAsync(HttpMethod.Patch, "/api/me", """{"phone":null}""", sessionToken);
        var clearNameResponse = await SendRawJsonAsync(HttpMethod.Patch, "/api/me", """{"name":"   "}""", sessionToken);

        Assert.Equal(HttpStatusCode.OK, clearPhoneResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, clearNameResponse.StatusCode);
        var stored = await ReadUserAsync(user.Id);
        Assert.Null(stored.Phone);
        Assert.Null(stored.Name);
    }

    [Fact]
    // Prisma tự cập nhật `updatedAt` khi sửa user. Bản .NET phải ghi cùng giá trị để báo cáo không lệch.
    public async Task UpdateProfile_RefreshesUpdatedAt()
    {
        var (user, sessionToken) = await CreateCustomerSessionAsync("patch-updated-at");
        var before = (await ReadUserAsync(user.Id)).UpdatedAt;

        await SendAsync(HttpMethod.Patch, "/api/me", new { name = "Tên mới" }, sessionToken);

        Assert.True((await ReadUserAsync(user.Id)).UpdatedAt > before);
    }

    [Fact]
    // Tên phải là chuỗi hoặc `null`. Kiểu khác nhận lỗi validate thay vì lỗi 500.
    public async Task UpdateProfile_NonStringNameIsRejected()
    {
        var (_, sessionToken) = await CreateCustomerSessionAsync("patch-type");

        var response = await SendRawJsonAsync(HttpMethod.Patch, "/api/me", """{"name":123}""", sessionToken);

        await RegisterTests.AssertErrorAsync(
            response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", "Dữ liệu không hợp lệ (name: Giá trị phải là chuỗi hoặc null.).");
    }

    [Fact]
    // Đổi mật khẩu đăng xuất mọi thiết bị. Kẻ đang dùng mật khẩu cũ bị đẩy ra ngay.
    public async Task ChangePassword_DeletesEverySessionAndAcceptsNewPassword()
    {
        var (user, sessionToken) = await CreateCustomerSessionAsync("change-password");
        var otherSessionToken = await AddSessionAsync(user.Id, DateTime.UtcNow);

        var response = await SendAsync(
            HttpMethod.Post, "/api/me/password", new { currentPassword = IdentityTestData.DefaultPassword, newPassword = "BrandNewPassword123!" }, sessionToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("Đổi mật khẩu thành công. Mọi phiên đăng nhập cũ đã được xóa.", document.RootElement.GetProperty("data").GetProperty("message").GetString());
        // Phiên hiện tại cũng bị xóa. Cookie bị xóa để trình duyệt về trạng thái đăng xuất.
        Assert.Contains("expires=Thu, 01 Jan 1970 00:00:00 GMT", Assert.Single(ActorHttp.ReadSetCookies(response, "pk_session")), StringComparison.OrdinalIgnoreCase);
        await using (var identity = _database.CreateIdentityContext())
        {
            Assert.Equal(0, await identity.Sessions.CountAsync(session => session.UserId == user.Id));
        }

        var otherDevice = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", $"pk_session={otherSessionToken}"));
        using var otherDocument = await ActorHttp.ReadJsonAsync(otherDevice);
        Assert.Equal("guest", otherDocument.RootElement.GetProperty("data").GetProperty("kind").GetString());

        var loginResponse = await SendAsync(HttpMethod.Post, "/api/auth/login", new { email = user.Email, password = "BrandNewPassword123!" }, sessionToken: null);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Theory]
    [InlineData(null, "Vui lòng cung cấp mật khẩu hiện tại.")]
    [InlineData("WrongOldPassword!", "Mật khẩu hiện tại không chính xác.")]
    // Người ngồi nhầm máy đang đăng nhập không đổi được mật khẩu khi không biết mật khẩu cũ.
    public async Task ChangePassword_RequiresCorrectCurrentPassword(string? currentPassword, string expectedMessage)
    {
        var (user, sessionToken) = await CreateCustomerSessionAsync("change-password-wrong");

        var response = await SendAsync(HttpMethod.Post, "/api/me/password", new { currentPassword, newPassword = "BrandNewPassword123!" }, sessionToken);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, "INVALID_CREDENTIALS", expectedMessage);
        // Mật khẩu cũ vẫn dùng được vì yêu cầu bị từ chối.
        Assert.True(PasswordHasher.Verify((await ReadUserAsync(user.Id)).PasswordHash!, IdentityTestData.DefaultPassword));
    }

    [Fact]
    // Tài khoản chỉ dùng Google chưa có mật khẩu cũ. Người dùng đặt mật khẩu đầu tiên mà không cần mật khẩu hiện tại.
    public async Task ChangePassword_GoogleOnlyAccountSetsFirstPassword()
    {
        var (user, sessionToken) = await CreateCustomerSessionAsync("change-password-google", password: null);

        var response = await SendAsync(HttpMethod.Post, "/api/me/password", new { newPassword = "FirstPassword123!" }, sessionToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(PasswordHasher.Verify((await ReadUserAsync(user.Id)).PasswordHash!, "FirstPassword123!"));
    }

    [Fact]
    // Mật khẩu mới phải đủ 8 ký tự. Thông báo lỗi giữ nguyên bản Node.
    public async Task ChangePassword_WeakNewPasswordIsRejected()
    {
        var (_, sessionToken) = await CreateCustomerSessionAsync("change-password-weak");

        var response = await SendAsync(HttpMethod.Post, "/api/me/password", new { currentPassword = IdentityTestData.DefaultPassword, newPassword = "short" }, sessionToken);

        await RegisterTests.AssertErrorAsync(
            response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", "Dữ liệu không hợp lệ (newPassword: Mật khẩu mới phải chứa tối thiểu 8 ký tự.).");
    }

    [Theory]
    [InlineData(null, "UNAUTHORIZED", "Vui lòng nhập mật khẩu để xác nhận xóa tài khoản.")]
    [InlineData("WrongPassword!", "INVALID_CREDENTIALS", "Mật khẩu xác nhận không chính xác.")]
    // Xóa tài khoản không đảo ngược được. Người dùng phải xác nhận lại mật khẩu.
    public async Task DeleteAccount_RequiresCorrectPassword(string? password, string expectedCode, string expectedMessage)
    {
        var (user, sessionToken) = await CreateCustomerSessionAsync("delete-wrong");

        var response = await SendAsync(HttpMethod.Delete, "/api/me", new { password }, sessionToken);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.Unauthorized, expectedCode, expectedMessage);
        Assert.Null((await ReadUserAsync(user.Id)).DeletedAt);
    }

    [Fact]
    // Xóa tài khoản ẩn danh dữ liệu cá nhân nhưng giữ dòng user. Đơn hàng cũ vẫn tham chiếu đúng tài khoản.
    public async Task DeleteAccount_AnonymizesUserRemovesAccessAndPublishesEvent()
    {
        var (user, sessionToken) = await CreateCustomerSessionAsync("delete-target");
        await using (var setup = _database.CreateIdentityContext())
        {
            // Gắn token email và liên kết Google để kiểm tra mọi đường đăng nhập bị xóa.
            setup.EmailTokens.Add(new EmailToken { TokenHash = SecureTokens.HashToken(SecureTokens.GenerateToken()), UserId = user.Id, Purpose = EmailTokenPurpose.Verify, ExpiresAt = DateTime.UtcNow.AddHours(1) });
            setup.OAuthAccounts.Add(new OAuthAccount { Provider = "google", ProviderUserId = $"sub-{Guid.NewGuid():N}", UserId = user.Id });
            await setup.SaveChangesAsync();
        }

        var response = await SendAsync(HttpMethod.Delete, "/api/me", new { password = IdentityTestData.DefaultPassword }, sessionToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("Tài khoản của bạn đã được xóa thành công.", document.RootElement.GetProperty("data").GetProperty("message").GetString());
        Assert.Contains("expires=Thu, 01 Jan 1970 00:00:00 GMT", Assert.Single(ActorHttp.ReadSetCookies(response, "pk_session")), StringComparison.OrdinalIgnoreCase);

        // Email ẩn danh chứa id để vẫn duy nhất. Email gốc được giải phóng cho người khác đăng ký.
        await using var identity = _database.CreateIdentityContext();
        var stored = await identity.Users.SingleAsync(value => value.Id == user.Id);
        Assert.Equal($"deleted-{user.Id}@deleted.pickory.local", stored.Email);
        Assert.Equal("Người dùng đã xóa", stored.Name);
        Assert.Null(stored.Phone);
        Assert.Null(stored.PasswordHash);
        Assert.NotNull(stored.DeletedAt);
        Assert.False(await identity.Sessions.AnyAsync(value => value.UserId == user.Id));
        Assert.False(await identity.EmailTokens.AnyAsync(value => value.UserId == user.Id));
        Assert.False(await identity.OAuthAccounts.AnyAsync(value => value.UserId == user.Id));

        // Module khác nhận đúng một sự kiện để xóa dữ liệu riêng của người dùng.
        Assert.Single(_factory.DomainEvents.OfType<UserDeletedEvent>(), value => value.UserId == user.Id);

        var loginResponse = await SendAsync(HttpMethod.Post, "/api/auth/login", new { email = user.Email, password = IdentityTestData.DefaultPassword }, sessionToken: null);
        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }

    [Fact]
    // Tài khoản chỉ dùng Google xác nhận bằng phiên vừa tạo. Phiên cũ hơn 5 phút phải đăng nhập Google lại.
    public async Task DeleteAccount_GoogleOnlyAccountNeedsSessionFromLastFiveMinutes()
    {
        var (user, _) = await CreateCustomerSessionAsync("delete-google", password: null);
        await using (var setup = _database.CreateIdentityContext())
        {
            setup.OAuthAccounts.Add(new OAuthAccount { Provider = "google", ProviderUserId = $"sub-{Guid.NewGuid():N}", UserId = user.Id });
            await setup.SaveChangesAsync();
        }

        var oldSessionToken = await AddSessionAsync(user.Id, DateTime.UtcNow.AddMinutes(-10));
        var freshSessionToken = await AddSessionAsync(user.Id, DateTime.UtcNow);

        var oldSessionResponse = await SendAsync(HttpMethod.Delete, "/api/me", new { }, oldSessionToken);
        var freshSessionResponse = await SendAsync(HttpMethod.Delete, "/api/me", new { }, freshSessionToken);

        await RegisterTests.AssertErrorAsync(
            oldSessionResponse, HttpStatusCode.Unauthorized, "REAUTH_REQUIRED", "Vui lòng đăng nhập lại Google trong vòng 5 phút để xác nhận xóa tài khoản.");
        Assert.Equal(HttpStatusCode.OK, freshSessionResponse.StatusCode);
        Assert.NotNull((await ReadUserAsync(user.Id)).DeletedAt);
    }

    [Fact]
    // Nhân viên do admin quản lý. Nhân viên không tự xóa tài khoản qua route của khách.
    public async Task DeleteAccount_StaffIsForbidden()
    {
        var email = IdentityTestData.NewEmail("delete-staff");
        var staff = await IdentityTestData.CreateUserAsync(_database, email, UserKind.Staff, "sales");
        var sessionToken = await AddSessionAsync(staff.Id, DateTime.UtcNow);

        var response = await SendAsync(HttpMethod.Delete, "/api/me", new { password = IdentityTestData.DefaultPassword }, sessionToken);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN", "Chỉ tài khoản khách hàng mới có thể xóa qua chức năng này.");
    }

    private async Task<(User User, string SessionToken)> CreateCustomerSessionAsync(string prefix, string? password = IdentityTestData.DefaultPassword)
    {
        var user = await IdentityTestData.CreateUserAsync(_database, IdentityTestData.NewEmail(prefix), UserKind.Customer, "customer", password: password);
        return (user, await AddSessionAsync(user.Id, DateTime.UtcNow));
    }

    // Tạo phiên trực tiếp trong DB. Test đặt được thời điểm tạo phiên cho quy tắc 5 phút.
    private async Task<string> AddSessionAsync(string userId, DateTime createdAt)
    {
        var rawToken = SecureTokens.GenerateToken();
        await using var identity = _database.CreateIdentityContext();
        identity.Sessions.Add(new Session
        {
            TokenHash = SecureTokens.HashToken(rawToken),
            UserId = userId,
            CreatedAt = createdAt,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        });
        await identity.SaveChangesAsync();
        return rawToken;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object body, string? sessionToken)
    {
        return _client.SendAsync(ActorHttp.CreateJsonRequest(method, path, body, sessionToken is null ? null : $"pk_session={sessionToken}"));
    }

    // Gửi JSON thô. Test cần gửi `null` và kiểu sai mà đối tượng ẩn danh không biểu diễn được.
    private Task<HttpResponseMessage> SendRawJsonAsync(HttpMethod method, string path, string json, string sessionToken)
    {
        var request = ActorHttp.CreateJsonRequest(method, path, body: null, $"pk_session={sessionToken}");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return _client.SendAsync(request);
    }

    private async Task<User> ReadUserAsync(string userId)
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.Users.AsNoTracking().SingleAsync(value => value.Id == userId);
    }
}
