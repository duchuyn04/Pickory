using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Throttling;
using Pickory.Modules.Identity.Tokens;
using Xunit;

namespace Pickory.IntegrationTests.Api.Identity;

// Port từ test ID-05 và ID-17 của bản Node cho `POST /api/auth/register`.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class RegisterTests : IClassFixture<PickoryWebApplicationFactory>
{
    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public RegisterTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        // Bộ đếm theo địa chỉ IP (Internet Protocol) dùng chung trong một factory. Mỗi test bắt đầu với bộ đếm trống.
        factory.Services.GetRequiredService<IdentityRateLimiters>().ClearAll();
        // Đăng ký gán role `customer`, nên role này phải có sẵn trong DB.
        IdentityTestData.SeedRolesAsync(database).GetAwaiter().GetResult();
    }

    [Fact]
    // Đăng ký tạo khách hàng, đăng nhập luôn và gửi thư xác thực. Khách không phải nhập lại thông tin vừa gõ.
    public async Task Register_CreatesCustomerSignsInAndSendsVerificationEmail()
    {
        var email = IdentityTestData.NewEmail("register");
        var anonymousId = Guid.NewGuid().ToString();

        // Gửi email viết hoa, tên có khoảng trắng thừa và số điện thoại chỉ có khoảng trắng.
        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post,
            "/api/auth/register",
            new { email = email.ToUpperInvariant(), password = "ValidPassword123!", name = " Khách hàng 1 ", phone = "  " },
            $"pk_anon={anonymousId}"));

        // API trả 201 vì route vừa tạo tài khoản, giống bản Node.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var user = document.RootElement.GetProperty("data").GetProperty("user");
        var userId = user.GetProperty("id").GetString()!;
        // Email về chữ thường và tên bị cắt khoảng trắng. Một email chỉ thuộc một tài khoản.
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.Equal("Khách hàng 1", user.GetProperty("name").GetString());
        // Chuỗi trống thành null để DB không lưu số điện thoại rỗng.
        Assert.Equal(JsonValueKind.Null, user.GetProperty("phone").ValueKind);
        Assert.Equal("customer", user.GetProperty("kind").GetString());

        // Cookie phiên 30 ngày có thuộc tính bảo mật giống cookie của đăng nhập.
        var sessionCookie = Assert.Single(ActorHttp.ReadSetCookies(response, "pk_session"));
        Assert.Contains("max-age=2592000", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", sessionCookie, StringComparison.OrdinalIgnoreCase);

        await using var identity = _database.CreateIdentityContext();
        var stored = await identity.Users.Include(value => value.Roles).ThenInclude(value => value.Role).SingleAsync(value => value.Id == userId);
        // Email chỉ được xác thực khi người dùng bấm link trong thư.
        Assert.False(stored.EmailVerified);
        Assert.Equal(UserKind.Customer, stored.Kind);
        Assert.Equal(["customer"], stored.Roles.Select(value => value.Role!.Code));
        // Mật khẩu lưu dạng hash Argon2id với tham số giống bản Node. Hash cũ và mới kiểm tra được lẫn nhau.
        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", stored.PasswordHash);
        // DB lưu hash của token trong cookie, không lưu token gốc.
        var sessionHash = SecureTokens.HashToken(ActorHttp.ReadCookieValue(sessionCookie));
        Assert.True(await identity.Sessions.AnyAsync(value => value.TokenHash == sessionHash && value.UserId == userId));

        // Token xác thực email có hạn 24 giờ.
        var verifyToken = await identity.EmailTokens.SingleAsync(value => value.UserId == userId);
        Assert.Equal(EmailTokenPurpose.Verify, verifyToken.Purpose);
        Assert.Equal(DateTime.UtcNow.AddHours(24), verifyToken.ExpiresAt, TimeSpan.FromMinutes(1));

        // Thư chứa token gốc khớp hash trong DB. Link trong thư dẫn tới trang xác thực email.
        var message = Assert.Single(_factory.EmailSender.MessagesTo(email));
        Assert.Equal("Xác thực tài khoản Pickory", message.Subject);
        Assert.Equal("verify", message.Metadata["purpose"]);
        Assert.Equal(verifyToken.TokenHash, SecureTokens.HashToken(message.Metadata["token"]));
        Assert.Contains($"/verify-email?token={message.Metadata["token"]}", message.Text);

        // Sự kiện phát đúng một lần với mã ẩn danh. Giỏ hàng của khách vãng lai được gộp vào tài khoản mới.
        var linkedEvent = Assert.Single(_factory.DomainEvents.OfType<UserLinkedEvent>(), value => value.UserId == userId);
        Assert.Equal(anonymousId, linkedEvent.AnonymousId);
    }

    [Fact]
    // Cookie phiên từ bước đăng ký dùng được ngay cho request kế tiếp.
    public async Task Register_SessionCookieSignsCustomerIn()
    {
        var response = await RegisterAsync(IdentityTestData.NewEmail("register-me"));

        // Gửi token phiên vừa nhận tới `GET /api/me`.
        var meResponse = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", $"pk_session={ActorHttp.ReadSessionToken(response)}"));

        // Actor là khách hàng, không phải khách vãng lai.
        using var document = await ActorHttp.ReadJsonAsync(meResponse);
        Assert.Equal("customer", document.RootElement.GetProperty("data").GetProperty("kind").GetString());
    }

    [Theory]
    [InlineData("khong-phai-email", "ValidPassword123!", "Dữ liệu không hợp lệ (email: Email không đúng định dạng.).")]
    [InlineData(" ok@pickory.local", "ValidPassword123!", "Dữ liệu không hợp lệ (email: Email không đúng định dạng.).")]
    [InlineData("ok@pickory.local", "short", "Dữ liệu không hợp lệ (password: Mật khẩu phải chứa tối thiểu 8 ký tự.).")]
    // Email sai định dạng, email có khoảng trắng đầu và mật khẩu ngắn bị từ chối trước khi API chạm DB.
    public async Task Register_InvalidInputReturnsValidationError(string email, string password, string expectedMessage)
    {
        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/auth/register", new { email, password }));

        // Thông điệp có tên trường và câu lỗi giống bản Node, vì frontend hiển thị trực tiếp.
        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", expectedMessage);
    }

    [Fact]
    // Mật khẩu dài hơn 128 ký tự bị từ chối. Băm Argon2 chuỗi rất dài có thể làm máy chủ quá tải.
    public async Task Register_PasswordLongerThan128CharactersIsRejected()
    {
        // Gửi mật khẩu 129 ký tự, vượt giới hạn đúng một ký tự.
        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, "/api/auth/register", new { email = IdentityTestData.NewEmail("long"), password = new string('a', 129) }));

        // API trả lỗi kiểm tra dữ liệu với thông điệp giống bản Node.
        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", "Dữ liệu không hợp lệ (password: Mật khẩu không được vượt quá 128 ký tự.).");
    }

    [Theory]
    [InlineData(UserKind.Customer, "customer")]
    [InlineData(UserKind.Staff, "admin")]
    // Email đã dùng bởi khách hàng hoặc nhân viên nhận cùng thông báo. Người ngoài không dò được email nào thuộc nhân viên.
    public async Task Register_ExistingEmailOfAnyKindIsRejectedWithOneMessage(UserKind kind, string roleCode)
    {
        var email = IdentityTestData.NewEmail("existing");
        await IdentityTestData.CreateUserAsync(_database, email, kind, roleCode);

        // Gửi email viết hoa. API chuẩn hóa email nên vẫn phát hiện email trùng.
        var response = await RegisterAsync(email.ToUpperInvariant());

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "EMAIL_ALREADY_EXISTS", "Email này đã được sử dụng. Vui lòng đăng nhập hoặc chọn email khác.");
    }

    [Theory]
    [InlineData("website")]
    [InlineData("honeypot")]
    // Bot điền ô ẩn bị từ chối và không tạo tài khoản. Người thật không thấy ô ẩn nên không điền.
    public async Task Register_FilledHoneypotIsRejectedWithoutCreatingUser(string trapField)
    {
        var email = IdentityTestData.NewEmail("bot");
        var body = new Dictionary<string, string> { ["email"] = email, ["password"] = "ValidPassword123!", [trapField] = "https://spam-bot.site" };

        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/auth/register", body));

        // Lỗi chung không cho bot biết ô nào là bẫy.
        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "BAD_REQUEST", "Yêu cầu không hợp lệ.");
        // DB không có tài khoản mới với email của bot.
        Assert.False(await UserExistsAsync(email));
    }

    [Fact]
    // Ô ẩn rỗng không bị coi là bot. Một số form gửi chuỗi rỗng cho ô người dùng không điền.
    public async Task Register_EmptyHoneypotIsAllowed()
    {
        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, "/api/auth/register", new { email = IdentityTestData.NewEmail("human"), password = "ValidPassword123!", website = "" }));

        // API tạo tài khoản như request bình thường.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    // Request của bot không tiêu lượt đăng ký của IP. Bot không chặn được người thật dùng chung IP.
    public async Task Register_HoneypotRejectionDoesNotConsumeIpQuota()
    {
        // Gửi 6 request bẫy bot, vượt giới hạn 5 lần mỗi giờ.
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            await _client.SendAsync(ActorHttp.CreateJsonRequest(
                HttpMethod.Post, "/api/auth/register", new { email = IdentityTestData.NewEmail("bot"), password = "ValidPassword123!", honeypot = "x" }));
        }

        // Đăng ký thật sau đó vẫn thành công.
        var response = await RegisterAsync(IdentityTestData.NewEmail("after-bot"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    // Lần đăng ký thứ 6 từ cùng IP trong một giờ bị chặn. Bot không tạo được hàng loạt tài khoản rác.
    public async Task Register_SixthAttemptFromSameIpIsRejectedWithoutCreatingUser()
    {
        // Năm lần đầu đều thành công.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(IdentityTestData.NewEmail("quota"))).StatusCode);
        }

        var sixthEmail = IdentityTestData.NewEmail("quota-sixth");
        var response = await RegisterAsync(sixthEmail);

        // API trả 429 với thông điệp giống bản Node.
        await AssertErrorAsync(response, HttpStatusCode.TooManyRequests, "TOO_MANY_REQUESTS", "Bạn đã đăng ký quá nhiều lần từ địa chỉ này. Vui lòng thử lại sau 1 giờ.");
        // API chặn trước khi tạo tài khoản, nên DB không có email thứ 6.
        Assert.False(await UserExistsAsync(sixthEmail));
    }

    [Fact]
    // Với `TRUST_PROXY=1`, địa chỉ giả trong `X-Forwarded-For` không né được giới hạn theo IP.
    public async Task Register_WithTrustProxyOne_SpoofedForwardedAddressesShareRealClientQuota()
    {
        // Tạo app riêng tin một proxy. App dùng chung của các test khác giữ cấu hình mặc định.
        using var proxiedFactory = _factory.WithWebHostBuilder(builder => builder.UseSetting("TRUST_PROXY", "1"));
        using var proxiedClient = proxiedFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        for (var attempt = 1; attempt <= 6; attempt++)
        {
            var request = ActorHttp.CreateJsonRequest(
                HttpMethod.Post, "/api/auth/register", new { email = IdentityTestData.NewEmail("spoof"), password = "ValidPassword123!" });
            // Kẻ xấu đổi địa chỉ giả ở đầu header. Proxy tin cậy luôn ghi địa chỉ thật ở cuối header.
            request.Headers.Add("X-Forwarded-For", $"198.51.100.{attempt}, 203.0.113.195");

            var response = await proxiedClient.SendAsync(request);

            // API chỉ đọc địa chỉ cuối do proxy ghi. Sáu request dùng chung bộ đếm, nên lần thứ 6 nhận 429.
            Assert.Equal(attempt <= 5 ? HttpStatusCode.Created : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    // Gửi request đăng ký hợp lệ tối thiểu. Test chỉ đổi email giữa các lần gọi.
    private Task<HttpResponseMessage> RegisterAsync(string email)
    {
        return _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/auth/register", new { email, password = "ValidPassword123!" }));
    }

    // Kiểm tra trực tiếp trong DB. Phản hồi lỗi không đủ chứng minh API không tạo tài khoản.
    private async Task<bool> UserExistsAsync(string email)
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.Users.AnyAsync(value => value.Email == email);
    }

    // Kiểm tra mã HTTP, mã lỗi và thông điệp cùng lúc. Các lớp test identity khác dùng lại hàm này.
    internal static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code, string message)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.Equal(message, error.GetProperty("message").GetString());
    }
}
