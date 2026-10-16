using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.GoogleOAuth;
using Pickory.Modules.Identity.Throttling;
using Pickory.Modules.Identity.Tokens;
using Xunit;

namespace Pickory.IntegrationTests.Api.Identity;

// COM-21: identity phát `EmailVerifiedEvent` đúng lúc `EmailVerified` của tài khoản khách đổi từ sai sang đúng.
// Test đi qua route thật và đọc lại sự kiện đã ghi, nên việc đăng ký handler cũng được kiểm.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class EmailVerifiedEventTests : IClassFixture<PickoryWebApplicationFactory>
{
    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public EmailVerifiedEventTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        // Bộ giới hạn là singleton dùng chung. Xóa bộ đếm để lượt gọi của test trước không chặn test này.
        factory.Services.GetRequiredService<IdentityRateLimiters>().ClearAll();
        IdentityTestData.SeedRolesAsync(database).GetAwaiter().GetResult();
    }

    private List<EmailVerifiedEvent> EventsOf(string userId) =>
        _factory.DomainEvents.OfType<EmailVerifiedEvent>().Where(value => value.UserId == userId).ToList();

    [Fact]
    // Xác thực email bằng link trong thư phát đúng một sự kiện, mang email đã lưu trên tài khoản.
    public async Task VerifyEmail_PublishesOneEventWithStoredEmail()
    {
        var user = await CreateCustomerAsync("evt-verify", emailVerified: false);
        var rawToken = await AddEmailTokenAsync(user.Id, EmailTokenPurpose.Verify);

        var response = await PostAsync("/api/auth/email-verifications", new { token = rawToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var published = Assert.Single(EventsOf(user.Id));
        Assert.Equal(user.Email, published.Email);
    }

    [Fact]
    // Link thứ hai của tài khoản đã xác thực không phát lại. Link cũ dùng lại cũng không phát.
    public async Task VerifyEmail_AlreadyVerifiedAccountOrReusedLink_DoesNotPublishAgain()
    {
        var user = await CreateCustomerAsync("evt-verify-twice", emailVerified: false);
        var firstToken = await AddEmailTokenAsync(user.Id, EmailTokenPurpose.Verify);
        var secondToken = await AddEmailTokenAsync(user.Id, EmailTokenPurpose.Verify);

        await PostAsync("/api/auth/email-verifications", new { token = firstToken });
        // Link cũ đã dùng bị từ chối, và link mới của tài khoản đã xác thực vẫn thành công nhưng không đổi trạng thái.
        var reused = await PostAsync("/api/auth/email-verifications", new { token = firstToken });
        var second = await PostAsync("/api/auth/email-verifications", new { token = secondToken });

        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single(EventsOf(user.Id));
    }

    [Fact]
    // Tài khoản đã xác thực từ trước không phát sự kiện khi bấm link xác thực.
    public async Task VerifyEmail_AccountVerifiedBefore_DoesNotPublish()
    {
        var user = await CreateCustomerAsync("evt-verified-before", emailVerified: true);
        var rawToken = await AddEmailTokenAsync(user.Id, EmailTokenPurpose.Verify);

        var response = await PostAsync("/api/auth/email-verifications", new { token = rawToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(EventsOf(user.Id));
    }

    [Fact]
    // Google tạo tài khoản mới với email đã xác thực thì phát một sự kiện.
    public async Task Google_NewAccountWithVerifiedEmail_Publishes()
    {
        var email = IdentityTestData.NewEmail("evt-google-new");
        _factory.GoogleClient.UserInfo = new GoogleUserInfo($"sub-{Guid.NewGuid():N}", email.ToUpperInvariant(), EmailVerified: true, "Google User");

        var userId = await GoogleCallbackAsync();

        var published = Assert.Single(EventsOf(userId));
        // Sự kiện mang email đã chuẩn hóa và lưu trên tài khoản, không mang chữ hoa Google trả về.
        Assert.Equal(email, published.Email);
    }

    [Fact]
    // Google trả email chưa xác thực cho tài khoản mới thì không phát, vì email chưa được chứng minh.
    public async Task Google_NewAccountWithUnverifiedEmail_DoesNotPublish()
    {
        var email = IdentityTestData.NewEmail("evt-google-unverified");
        _factory.GoogleClient.UserInfo = new GoogleUserInfo($"sub-{Guid.NewGuid():N}", email, EmailVerified: false, "Google User");

        var userId = await GoogleCallbackAsync();

        Assert.Empty(EventsOf(userId));
    }

    [Fact]
    // Google liên kết tài khoản chưa xác thực thì tài khoản thành đã xác thực và phát một sự kiện.
    public async Task Google_LinkUnverifiedExistingAccount_Publishes()
    {
        var existing = await CreateCustomerAsync("evt-google-link", emailVerified: false);
        _factory.GoogleClient.UserInfo = new GoogleUserInfo($"sub-{Guid.NewGuid():N}", existing.Email, EmailVerified: true, "Google User");

        var userId = await GoogleCallbackAsync();

        Assert.Equal(existing.Id, userId);
        var published = Assert.Single(EventsOf(existing.Id));
        Assert.Equal(existing.Email, published.Email);
    }

    [Fact]
    // Tài khoản đã xác thực đăng nhập bằng Google không phát lại, kể cả lần liên kết Google đầu tiên.
    public async Task Google_AccountVerifiedBefore_DoesNotPublish()
    {
        var existing = await CreateCustomerAsync("evt-google-verified", emailVerified: true);
        _factory.GoogleClient.UserInfo = new GoogleUserInfo($"sub-{Guid.NewGuid():N}", existing.Email, EmailVerified: true, "Google User");

        // Lần đầu liên kết Google, lần hai đăng nhập qua liên kết đã có.
        await GoogleCallbackAsync();
        await GoogleCallbackAsync();

        Assert.Empty(EventsOf(existing.Id));
    }

    [Fact]
    // Đặt lại mật khẩu bằng link trong thư chứng minh khách giữ email. Tài khoản chưa xác thực phát một sự kiện.
    public async Task ResetPassword_UnverifiedCustomer_Publishes()
    {
        var user = await CreateCustomerAsync("evt-reset", emailVerified: false);
        var rawToken = await AddEmailTokenAsync(user.Id, EmailTokenPurpose.Reset);

        var response = await PutAsync("/api/auth/password", new { token = rawToken, newPassword = "BrandNewPassword123!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var published = Assert.Single(EventsOf(user.Id));
        Assert.Equal(user.Email, published.Email);
    }

    [Fact]
    // Tài khoản đã xác thực đặt lại mật khẩu không phát sự kiện.
    public async Task ResetPassword_VerifiedCustomer_DoesNotPublish()
    {
        var user = await CreateCustomerAsync("evt-reset-verified", emailVerified: true);
        var rawToken = await AddEmailTokenAsync(user.Id, EmailTokenPurpose.Reset);

        var response = await PutAsync("/api/auth/password", new { token = rawToken, newPassword = "BrandNewPassword123!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(EventsOf(user.Id));
    }

    [Fact]
    // Nhân viên đặt mật khẩu bằng link mời không phát sự kiện, vì nhân viên không có đơn hàng của khách.
    public async Task ResetPassword_StaffInvite_DoesNotPublish()
    {
        var staff = await IdentityTestData.CreateUserAsync(
            _database, IdentityTestData.NewEmail("evt-staff"), UserKind.Staff, "sales", emailVerified: false);
        var rawToken = await AddEmailTokenAsync(staff.Id, EmailTokenPurpose.Invite);

        var response = await PutAsync("/api/auth/password", new { token = rawToken, newPassword = "BrandNewPassword123!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(EventsOf(staff.Id));
    }

    private Task<User> CreateCustomerAsync(string prefix, bool emailVerified) =>
        IdentityTestData.CreateUserAsync(_database, IdentityTestData.NewEmail(prefix), UserKind.Customer, "customer", emailVerified: emailVerified);

    private Task<HttpResponseMessage> PostAsync(string path, object body) =>
        _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Post, path, body));

    private Task<HttpResponseMessage> PutAsync(string path, object body) =>
        _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Put, path, body));

    // Ghi token thẳng vào DB. Test đặt được mục đích mà không cần gửi thư.
    private async Task<string> AddEmailTokenAsync(string userId, EmailTokenPurpose purpose)
    {
        var rawToken = SecureTokens.GenerateToken();
        await using var identity = _database.CreateIdentityContext();
        identity.EmailTokens.Add(new EmailToken
        {
            TokenHash = SecureTokens.HashToken(rawToken),
            UserId = userId,
            Purpose = purpose,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
        });
        await identity.SaveChangesAsync();
        return rawToken;
    }

    // Gọi callback Google ở chế độ JSON và trả mã tài khoản đăng nhập được.
    private async Task<string> GoogleCallbackAsync()
    {
        var stateJson = JsonSerializer.Serialize(new { state = "state-1", codeVerifier = "verifier-1" });
        var request = ActorHttp.CreateRequest(
            HttpMethod.Get, "/api/auth/google/callback?code=code-1&state=state-1&json=1", $"pk_oauth_state={Uri.EscapeDataString(stateJson)}");
        request.Headers.Add("Accept", "application/json");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        return document.RootElement.GetProperty("data").GetProperty("user").GetProperty("id").GetString()!;
    }
}
