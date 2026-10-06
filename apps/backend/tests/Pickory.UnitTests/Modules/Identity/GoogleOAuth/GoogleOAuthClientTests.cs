using System.Net;
using System.Text;
using Pickory.Modules.Identity.GoogleOAuth;
using Pickory.Shared.Configuration;
using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.GoogleOAuth;

// Kiểm tra request gửi tới Google và cách client che lỗi chi tiết của Google.
public class GoogleOAuthClientTests
{
    // Cấu hình tối thiểu. Test chỉ cần `client_id` và `client_secret` của Google.
    private static readonly AppConfig Config = new()
    {
        DatabaseUrl = "postgresql://pickory:pickory@localhost:5432/pickory",
        GoogleClientId = "client-id",
        GoogleClientSecret = "client-secret",
    };

    [Fact]
    // Đổi code gửi form PKCE (Proof Key for Code Exchange) đúng chuẩn OAuth tới Google và trả access token.
    public async Task ExchangeCode_PostsPkceFormAndReturnsAccessToken()
    {
        // Google giả trả access token hợp lệ.
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"access_token":"access-123","token_type":"Bearer"}""");
        var client = new GoogleOAuthClient(new HttpClient(handler), Config);

        var accessToken = await client.ExchangeCodeAsync("code-1", "verifier-1", "http://localhost:3000/api/auth/google/callback", CancellationToken.None);

        // Client trả access token và gọi `POST` tới endpoint token của Google.
        Assert.Equal("access-123", accessToken);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://oauth2.googleapis.com/token", handler.Request.RequestUri!.ToString());
        // Google từ chối nếu `redirect_uri` khác lúc chuyển hướng hoặc thiếu `code_verifier`.
        var form = ParseForm(handler.Body!);
        Assert.Equal("code-1", form["code"]);
        Assert.Equal("client-id", form["client_id"]);
        Assert.Equal("client-secret", form["client_secret"]);
        Assert.Equal("http://localhost:3000/api/auth/google/callback", form["redirect_uri"]);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("verifier-1", form["code_verifier"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""", "Không thể đổi mã xác thực với Google.")]
    [InlineData(HttpStatusCode.OK, """{"token_type":"Bearer"}""", "Phản hồi từ Google thiếu access token.")]
    // Lỗi đổi code không lộ chi tiết của Google. Người dùng chỉ thấy thông điệp chung bằng tiếng Việt.
    public async Task ExchangeCode_FailureHidesGoogleDetails(HttpStatusCode status, string body, string expectedMessage)
    {
        // Trường hợp đầu Google trả lỗi. Trường hợp hai Google trả 200 nhưng thiếu access token.
        var client = new GoogleOAuthClient(new HttpClient(new RecordingHandler(status, body)), Config);

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            client.ExchangeCodeAsync("code-1", "verifier-1", "http://localhost:3000/api/auth/google/callback", CancellationToken.None));

        // Client ném `AppException` 400 với mã `GOOGLE_AUTH_FAILED` thay vì lỗi HTTP thô.
        Assert.Equal("GOOGLE_AUTH_FAILED", exception.Code);
        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    // Lấy thông tin người dùng gửi token trong header và đọc đúng các trường snake_case của Google.
    public async Task GetUserInfo_SendsBearerTokenAndMapsFields()
    {
        // Phản hồi có trường thừa `picture`. Client phải bỏ qua trường này.
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """{"sub":"sub-1","email":"Googler@Example.com","email_verified":true,"name":"Google User","picture":"x"}""");
        var client = new GoogleOAuthClient(new HttpClient(handler), Config);

        var userInfo = await client.GetUserInfoAsync("access-123", CancellationToken.None);

        // Client giữ nguyên chữ hoa của email. Bước chuẩn hóa email thuộc về handler đăng nhập.
        Assert.Equal(new GoogleUserInfo("sub-1", "Googler@Example.com", EmailVerified: true, "Google User"), userInfo);
        Assert.Equal("https://openidconnect.googleapis.com/v1/userinfo", handler.Request!.RequestUri!.ToString());
        // Token đi trong header `Authorization`, không đi trong URL. URL có thể bị ghi vào log.
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("access-123", handler.Request.Headers.Authorization.Parameter);
    }

    [Fact]
    // Lỗi lấy thông tin người dùng trả thông điệp chung, không lộ phản hồi của Google.
    public async Task GetUserInfo_FailureReturnsGoogleAuthFailed()
    {
        // Google giả trả 401, giống khi access token đã hết hạn.
        var client = new GoogleOAuthClient(new HttpClient(new RecordingHandler(HttpStatusCode.Unauthorized, "{}")), Config);

        var exception = await Assert.ThrowsAsync<AppException>(() => client.GetUserInfoAsync("expired", CancellationToken.None));

        // Thông điệp tiếng Việt giống bản Node.
        Assert.Equal("Không thể lấy thông tin người dùng từ Google.", exception.Message);
    }

    // Đọc body dạng form thành từ điển. Test so sánh từng trường mà không phụ thuộc thứ tự trường.
    private static Dictionary<string, string> ParseForm(string body)
    {
        return body.Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(parts => WebUtility.UrlDecode(parts[0]), parts => WebUtility.UrlDecode(parts[1]));
    }

    // Handler giả ghi lại request và trả response cố định. Test không gọi mạng thật.
    private sealed class RecordingHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Lưu body dạng chuỗi ngay khi nhận. Test đọc lại form mà không phụ thuộc vòng đời của request.
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }
}
