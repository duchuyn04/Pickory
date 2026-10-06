using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Passwords;
using Pickory.Modules.Identity.Throttling;
using Pickory.Modules.Identity.Tokens;
using Xunit;

namespace Pickory.IntegrationTests.Api.Identity;

// Port từ test ID-07 và ID-17 của bản Node cho xác thực email, quên và đặt lại mật khẩu.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class EmailTokenTests : IClassFixture<PickoryWebApplicationFactory>
{
    // Thông điệp giữ nguyên bản Node vì frontend hiển thị trực tiếp.
    private const string ForgotPasswordMessage = "Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được gửi.";
    private const string InvalidVerifyTokenMessage = "Liên kết không hợp lệ, đã hết hạn hoặc đã được sử dụng.";
    private const string InvalidResetTokenMessage = "Liên kết đặt lại mật khẩu không hợp lệ, đã hết hạn hoặc đã được sử dụng.";
    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public EmailTokenTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        // Bộ giới hạn là singleton dùng chung. Xóa bộ đếm để lượt gọi của test trước không chặn test này.
        factory.Services.GetRequiredService<IdentityRateLimiters>().ClearAll();
    }

    [Fact]
    // Token xác thực email chỉ dùng được một lần. Link bị lộ sau khi dùng không còn giá trị.
    public async Task VerifyEmail_ValidTokenVerifiesOnceOnly()
    {
        var user = await CreateCustomerAsync("verify");
        var rawToken = await AddEmailTokenAsync(user.Id, EmailTokenPurpose.Verify, TimeSpan.FromHours(24));

        // Gửi cùng token hai lần liên tiếp.
        var firstResponse = await PostAsync("/api/auth/verify-email", new { token = rawToken });
        var secondResponse = await PostAsync("/api/auth/verify-email", new { token = rawToken });

        // Lần đầu thành công với body giống bản Node.
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(firstResponse);
        var data = document.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("success").GetBoolean());
        Assert.Equal("Xác thực email thành công.", data.GetProperty("message").GetString());
        // Tài khoản được đánh dấu đã xác thực email trong DB.
        Assert.True((await ReadUserAsync(user.Id)).EmailVerified);
        // Lần hai nhận `INVALID_TOKEN`, cùng lỗi với token sai.
        await RegisterTests.AssertErrorAsync(secondResponse, HttpStatusCode.BadRequest, "INVALID_TOKEN", InvalidVerifyTokenMessage);
    }

    [Fact]
    // Token đặt lại mật khẩu không xác thực được email. Mỗi token chỉ dùng cho mục đích đã cấp.
    public async Task VerifyEmail_ResetTokenCannotVerifyEmail()
    {
        var user = await CreateCustomerAsync("verify-wrong-purpose");
        // Tạo token còn hạn nhưng mục đích là `Reset`.
        var rawToken = await AddEmailTokenAsync(user.Id, EmailTokenPurpose.Reset, TimeSpan.FromMinutes(30));

        var response = await PostAsync("/api/auth/verify-email", new { token = rawToken });

        // API trả cùng lỗi với token sai. Người ngoài không biết token tồn tại với mục đích khác.
        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, "INVALID_TOKEN", InvalidVerifyTokenMessage);
        // Trạng thái xác thực email không đổi.
        Assert.False((await ReadUserAsync(user.Id)).EmailVerified);
    }

    [Fact]
    // Hai request đồng thời với một token xác thực chỉ có một request thắng. Token không bị dùng hai lần.
    public async Task VerifyEmail_ConcurrentRequestsWithOneTokenHaveExactlyOneWinner()
    {
        var user = await CreateCustomerAsync("verify-race");
        var rawToken = await AddEmailTokenAsync(user.Id, EmailTokenPurpose.Verify, TimeSpan.FromMinutes(1));

        // Gửi hai request cùng lúc. Cả hai có thể đọc thấy token còn dùng được.
        var responses = await Task.WhenAll(
            PostAsync("/api/auth/verify-email", new { token = rawToken }),
            PostAsync("/api/auth/verify-email", new { token = rawToken }));

        // Đúng một request trả 200. Request còn lại trả 400.
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.BadRequest], responses.Select(response => response.StatusCode).Order());
    }

    [Fact]
    // Email có và không có tài khoản nhận cùng phản hồi. Kẻ dò không biết email nào đã đăng ký.
    public async Task ForgotPassword_ExistingAndUnknownEmailGetSameResponse()
    {
        var user = await CreateCustomerAsync("forgot");

        // Gọi quên mật khẩu với một email đã đăng ký và một email chưa đăng ký.
        var existingResponse = await PostAsync("/api/auth/forgot-password", new { email = user.Email });
        var unknownEmail = IdentityTestData.NewEmail("nobody");
        var unknownResponse = await PostAsync("/api/auth/forgot-password", new { email = unknownEmail });

        // Hai phản hồi giống nhau. Chỉ email đã đăng ký nhận thư.
        await AssertForgotPasswordResponseAsync(existingResponse);
        await AssertForgotPasswordResponseAsync(unknownResponse);
        Assert.Empty(_factory.EmailSender.MessagesTo(unknownEmail));

        // Thư có tiêu đề và mục đích giống bản Node.
        var message = Assert.Single(_factory.EmailSender.MessagesTo(user.Email));
        Assert.Equal("Đặt lại mật khẩu Pickory", message.Subject);
        Assert.Equal("reset", message.Metadata["purpose"]);
        var rawToken = message.Metadata["token"];
        // Link trong thư phải là địa chỉ đầy đủ. Đường dẫn tương đối không mở được từ hòm thư.
        Assert.Contains($"http://localhost:5173/reset-password?token={rawToken}", message.Text);

        // DB chỉ lưu hash của token. Người đọc được DB không dùng được token trong thư.
        await using var identity = _database.CreateIdentityContext();
        Assert.False(await identity.EmailTokens.AnyAsync(value => value.TokenHash == rawToken));
        var storedToken = await identity.EmailTokens.SingleAsync(value => value.TokenHash == SecureTokens.HashToken(rawToken));
        // Token đặt lại mật khẩu sống 30 phút. Sai lệch một phút bù thời gian chạy test.
        Assert.Equal(EmailTokenPurpose.Reset, storedToken.Purpose);
        Assert.Equal(DateTime.UtcNow.AddMinutes(30), storedToken.ExpiresAt, TimeSpan.FromMinutes(1));
    }

    [Fact]
    // Tài khoản bị khóa không nhận thư đặt lại mật khẩu. Người bị khóa không dùng bước này để quay lại.
    public async Task ForgotPassword_LockedAccountReceivesNoEmail()
    {
        var email = IdentityTestData.NewEmail("forgot-locked");
        await IdentityTestData.CreateUserAsync(_database, email, UserKind.Customer, "customer", locked: true);

        var response = await PostAsync("/api/auth/forgot-password", new { email });

        // Phản hồi vẫn giống email hợp lệ, nên kẻ dò không biết tài khoản bị khóa.
        await AssertForgotPasswordResponseAsync(response);
        Assert.Empty(_factory.EmailSender.MessagesTo(email));
    }

    [Fact]
    // Lần gọi thứ tư cho cùng email bị bỏ qua im lặng. Kẻ xấu không spam được hòm thư của nạn nhân.
    public async Task ForgotPassword_FourthRequestForSameEmailIsSilentlyDropped()
    {
        var user = await CreateCustomerAsync("forgot-quota");

        // Mọi lần gọi đều nhận cùng phản hồi thành công. API không trả 429 vì lỗi đó cho biết email đang bị gọi nhiều.
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await AssertForgotPasswordResponseAsync(await PostAsync("/api/auth/forgot-password", new { email = user.Email }));
        }

        // Giới hạn là 3 lần mỗi 15 phút. Chỉ 3 token được tạo và chỉ 3 thư được gửi.
        await using var identity = _database.CreateIdentityContext();
        Assert.Equal(3, await identity.EmailTokens.CountAsync(value => value.UserId == user.Id && value.Purpose == EmailTokenPurpose.Reset));
        Assert.Equal(3, _factory.EmailSender.MessagesTo(user.Email).Count);
    }

    [Fact]
    // Đặt lại mật khẩu đổi mật khẩu, đăng xuất mọi phiên và làm token hết dùng được.
    public async Task ResetPassword_ChangesPasswordDeletesSessionsAndCannotBeReused()
    {
        var user = await CreateCustomerAsync("reset");
        // Tạo hai phiên đang hoạt động, giống người dùng đăng nhập trên hai thiết bị.
        var oldSession = await AddSessionAsync(user.Id);
        await AddSessionAsync(user.Id);
        var rawToken = await AddEmailTokenAsync(user.Id, EmailTokenPurpose.Reset, TimeSpan.FromMinutes(30));

        var response = await PostAsync("/api/auth/reset-password", new { token = rawToken, newPassword = "BrandNewPassword123!" });

        // Body giống bản Node. Frontend dùng `userKind` để chọn trang đăng nhập phù hợp.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("success").GetBoolean());
        Assert.Equal("customer", data.GetProperty("userKind").GetString());
        Assert.Equal("Đặt lại mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới.", data.GetProperty("message").GetString());

        // DB lưu hash của mật khẩu mới.
        var stored = await ReadUserAsync(user.Id);
        Assert.True(PasswordHasher.Verify(stored.PasswordHash!, "BrandNewPassword123!"));
        // Bấm link trong thư chứng tỏ người dùng sở hữu hòm thư.
        Assert.True(stored.EmailVerified);
        // Mật khẩu có thể đã bị lộ. API xóa mọi phiên để người dùng mật khẩu cũ bị đăng xuất.
        await using (var identity = _database.CreateIdentityContext())
        {
            Assert.Equal(0, await identity.Sessions.CountAsync(value => value.UserId == user.Id));
        }

        // Cookie của phiên cũ chỉ cho quyền khách vãng lai.
        var oldSessionResponse = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", $"pk_session={oldSession}"));
        using var meDocument = await ActorHttp.ReadJsonAsync(oldSessionResponse);
        Assert.Equal("guest", meDocument.RootElement.GetProperty("data").GetProperty("kind").GetString());

        // Token đã dùng không đổi được mật khẩu lần nữa.
        var reuseResponse = await PostAsync("/api/auth/reset-password", new { token = rawToken, newPassword = "AnotherPassword123!" });
        await RegisterTests.AssertErrorAsync(reuseResponse, HttpStatusCode.BadRequest, "INVALID_TOKEN", InvalidResetTokenMessage);
    }

    [Fact]
    // Đặt lại mật khẩu vô hiệu mọi link đặt lại và lời mời khác. Token xác thực email vẫn dùng được.
    public async Task ResetPassword_InvalidatesOtherResetAndInviteTokensButKeepsVerifyToken()
    {
        var user = await CreateCustomerAsync("reset-others");
        // Tạo một token để dùng và ba token khác với ba mục đích.
        var usedToken = await AddEmailTokenAsync(user.Id, EmailTokenPurpose.Reset, TimeSpan.FromMinutes(30));
        var otherResetToken = await AddEmailTokenAsync(user.Id, EmailTokenPurpose.Reset, TimeSpan.FromMinutes(30));
        var inviteToken = await AddEmailTokenAsync(user.Id, EmailTokenPurpose.Invite, TimeSpan.FromDays(3));
        var verifyToken = await AddEmailTokenAsync(user.Id, EmailTokenPurpose.Verify, TimeSpan.FromHours(24));

        await PostAsync("/api/auth/reset-password", new { token = usedToken, newPassword = "BrandNewPassword123!" });

        await using var identity = _database.CreateIdentityContext();
        var tokens = await identity.EmailTokens.Where(value => value.UserId == user.Id).ToDictionaryAsync(value => value.TokenHash);
        // Link cũ không được đặt mật khẩu lần nữa sau khi mật khẩu đã đổi.
        Assert.NotNull(tokens[SecureTokens.HashToken(otherResetToken)].UsedAt);
        Assert.NotNull(tokens[SecureTokens.HashToken(inviteToken)].UsedAt);
        // Token xác thực email không liên quan tới mật khẩu, nên API giữ nguyên.
        Assert.Null(tokens[SecureTokens.HashToken(verifyToken)].UsedAt);
    }

    [Fact]
    // Token mời nhân viên dùng chung bước đặt mật khẩu. Nhân viên mới tự đặt mật khẩu lần đầu.
    public async Task ResetPassword_InviteTokenSetsStaffPassword()
    {
        var email = IdentityTestData.NewEmail("invite");
        // Nhân viên được mời chưa có mật khẩu.
        var staff = await IdentityTestData.CreateUserAsync(_database, email, UserKind.Staff, "sales", password: null);
        var inviteToken = await AddEmailTokenAsync(staff.Id, EmailTokenPurpose.Invite, TimeSpan.FromDays(3));

        var response = await PostAsync("/api/auth/reset-password", new { token = inviteToken, newPassword = "StaffNewPassword123!" });

        // API trả `userKind` là `staff`. Frontend chuyển nhân viên sang trang đăng nhập nhân viên.
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("staff", document.RootElement.GetProperty("data").GetProperty("userKind").GetString());
    }

    [Theory]
    [InlineData(EmailTokenPurpose.Verify, 30)]
    [InlineData(EmailTokenPurpose.Reset, -1)]
    // Token xác thực email và token hết hạn không đặt được mật khẩu.
    public async Task ResetPassword_VerifyOrExpiredTokenIsRejected(EmailTokenPurpose purpose, int minutesUntilExpiry)
    {
        var user = await CreateCustomerAsync("reset-rejected");
        var rawToken = await AddEmailTokenAsync(user.Id, purpose, TimeSpan.FromMinutes(minutesUntilExpiry));

        var response = await PostAsync("/api/auth/reset-password", new { token = rawToken, newPassword = "NewPassword123!" });

        // Hai trường hợp nhận cùng lỗi. Người ngoài không phân biệt được lý do token hỏng.
        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, "INVALID_TOKEN", InvalidResetTokenMessage);
    }

    [Fact]
    // Mật khẩu yếu bị từ chối trước khi API dùng token. Người dùng sửa mật khẩu mà không cần link mới.
    public async Task ResetPassword_WeakPasswordKeepsTokenUsable()
    {
        var user = await CreateCustomerAsync("reset-weak");
        var rawToken = await AddEmailTokenAsync(user.Id, EmailTokenPurpose.Reset, TimeSpan.FromMinutes(30));

        // Gửi mật khẩu yếu trước. Sau đó gửi mật khẩu hợp lệ với cùng token.
        var weakResponse = await PostAsync("/api/auth/reset-password", new { token = rawToken, newPassword = "short" });
        var validResponse = await PostAsync("/api/auth/reset-password", new { token = rawToken, newPassword = "LongEnough123!" });

        // Lần đầu nhận lỗi kiểm tra dữ liệu. Lần hai thành công vì token chưa bị dùng.
        await RegisterTests.AssertErrorAsync(
            weakResponse, HttpStatusCode.BadRequest, "VALIDATION_ERROR", "Dữ liệu không hợp lệ (newPassword: Mật khẩu mới phải chứa tối thiểu 8 ký tự.).");
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
    }

    [Fact]
    // Hai request đồng thời với một token đặt lại chỉ có một request thắng. Mật khẩu không bị đặt hai lần.
    public async Task ResetPassword_ConcurrentRequestsWithOneTokenHaveExactlyOneWinner()
    {
        var user = await CreateCustomerAsync("reset-race");
        var rawToken = await AddEmailTokenAsync(user.Id, EmailTokenPurpose.Reset, TimeSpan.FromMinutes(30));

        // Gửi hai request cùng lúc với hai mật khẩu khác nhau.
        var responses = await Task.WhenAll(
            PostAsync("/api/auth/reset-password", new { token = rawToken, newPassword = "RacePasswordOne1!" }),
            PostAsync("/api/auth/reset-password", new { token = rawToken, newPassword = "RacePasswordTwo2!" }));

        // Đúng một request trả 200. Request còn lại trả 400.
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.BadRequest], responses.Select(response => response.StatusCode).Order());
    }

    [Theory]
    [InlineData(EmailTokenPurpose.Verify, "/api/auth/verify-email")]
    [InlineData(EmailTokenPurpose.Reset, "/api/auth/reset-password")]
    // Prisma tự cập nhật `updatedAt` khi sửa user. Bản .NET phải ghi cùng cột, nếu không thì báo cáo thấy user chưa đổi.
    public async Task TokenRoutes_RefreshUserUpdatedAt(EmailTokenPurpose purpose, string path)
    {
        var user = await CreateCustomerAsync("updated-at");
        var rawToken = await AddEmailTokenAsync(user.Id, purpose, TimeSpan.FromMinutes(30));
        // Đặt `UpdatedAt` về một ngày trước. Giá trị mới phải là thời điểm request, không phải thời điểm tạo user.
        await using (var setup = _database.CreateIdentityContext())
        {
            await setup.Users.Where(value => value.Id == user.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.UpdatedAt, DateTime.UtcNow.AddDays(-1)));
        }

        var response = await PostAsync(path, new { token = rawToken, newPassword = "BrandNewPassword123!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DateTime.UtcNow, (await ReadUserAsync(user.Id)).UpdatedAt, TimeSpan.FromMinutes(1));
    }

    // Gửi request ghi có `Origin` của web. Request qua được bước chống CSRF (Cross-Site Request Forgery).
    private Task<HttpResponseMessage> PostAsync(string path, object body)
    {
        return _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Post, path, body));
    }

    // Tạo khách hàng với email riêng. Thư và token của test khác không lẫn vào kết quả.
    private Task<User> CreateCustomerAsync(string prefix)
    {
        return IdentityTestData.CreateUserAsync(_database, IdentityTestData.NewEmail(prefix), UserKind.Customer, "customer");
    }

    // Ghi token thẳng vào DB. Test đặt được mục đích và hạn mà không cần gửi thư.
    private async Task<string> AddEmailTokenAsync(string userId, EmailTokenPurpose purpose, TimeSpan expiresIn)
    {
        var rawToken = SecureTokens.GenerateToken();
        await using var identity = _database.CreateIdentityContext();
        identity.EmailTokens.Add(new EmailToken
        {
            // Lưu hash giống API. Test gửi token gốc như link trong thư.
            TokenHash = SecureTokens.HashToken(rawToken),
            UserId = userId,
            Purpose = purpose,
            ExpiresAt = DateTime.UtcNow.Add(expiresIn),
        });
        await identity.SaveChangesAsync();
        return rawToken;
    }

    // Tạo phiên còn hạn 30 ngày. Test kiểm tra đặt lại mật khẩu xóa phiên này.
    private async Task<string> AddSessionAsync(string userId)
    {
        var rawToken = SecureTokens.GenerateToken();
        await using var identity = _database.CreateIdentityContext();
        identity.Sessions.Add(new Session { TokenHash = SecureTokens.HashToken(rawToken), UserId = userId, ExpiresAt = DateTime.UtcNow.AddDays(30) });
        await identity.SaveChangesAsync();
        return rawToken;
    }

    // Đọc tài khoản bằng context mới. Kết quả phản ánh dữ liệu API đã ghi, không lấy từ bộ nhớ đệm.
    private async Task<User> ReadUserAsync(string userId)
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.Users.AsNoTracking().SingleAsync(value => value.Id == userId);
    }

    // Mọi nhánh quên mật khẩu phải trả cùng 200 và cùng thông điệp.
    private static async Task AssertForgotPasswordResponseAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal(ForgotPasswordMessage, document.RootElement.GetProperty("data").GetProperty("message").GetString());
    }
}
