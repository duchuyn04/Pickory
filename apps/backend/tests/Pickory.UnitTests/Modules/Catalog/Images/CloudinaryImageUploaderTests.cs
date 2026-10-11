using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Pickory.Modules.Catalog.Images;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Images;

// Bộ tải ảnh theo URL nguồn gọi Cloudinary bằng yêu cầu có ký. Test dùng handler giả nên không gọi mạng thật.
public sealed class CloudinaryImageUploaderTests
{
    private static readonly CloudinaryCredentials Credentials = new("pickory-demo", "123456", "bi-mat-test");

    private const string PublicId = "pickory/products/seed/apple-iphone-15-1";
    private const string SourceUrl = "https://www.apple.com/newsroom/iphone-15.jpg";

    // Handler giả ghi lại yêu cầu đã gửi và trả về phản hồi cố định.
    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public Dictionary<string, string> Form { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            var raw = await request.Content!.ReadAsStringAsync(cancellationToken);
            Form = raw.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }

    private static CloudinaryImageUploader CreateUploader(HttpMessageHandler handler)
    {
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        return new CloudinaryImageUploader(new HttpClient(handler), Credentials, time);
    }

    [Fact]
    public async Task UploadFromUrlAsync_PostsToTheUploadEndpointOfTheCloud()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"public_id":"x","bytes":1000,"format":"webp"}""");

        await CreateUploader(handler).UploadFromUrlAsync(PublicId, SourceUrl, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.cloudinary.com/v1_1/pickory-demo/image/upload", handler.Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task UploadFromUrlAsync_SendsSourceUrlAsTheFileAndKeepsExistingImages()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"public_id":"x","bytes":1000,"format":"webp"}""");

        await CreateUploader(handler).UploadFromUrlAsync(PublicId, SourceUrl, CancellationToken.None);

        Assert.Equal(SourceUrl, handler.Form["file"]);
        Assert.Equal(PublicId, handler.Form["public_id"]);
        Assert.Equal("false", handler.Form["overwrite"]);
        Assert.Equal("123456", handler.Form["api_key"]);
        Assert.Equal("1700000000", handler.Form["timestamp"]);
    }

    [Fact]
    public async Task UploadFromUrlAsync_LimitsTheLongEdgeAndStoresWebp()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"public_id":"x","bytes":1000,"format":"webp"}""");

        await CreateUploader(handler).UploadFromUrlAsync(PublicId, SourceUrl, CancellationToken.None);

        Assert.Equal("c_limit,w_1600,h_1600", handler.Form["transformation"]);
        Assert.Equal("webp", handler.Form["format"]);
    }

    [Fact]
    public async Task UploadFromUrlAsync_SignsEverySentParameterExceptFileAndKey()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"public_id":"x","bytes":1000,"format":"webp"}""");

        await CreateUploader(handler).UploadFromUrlAsync(PublicId, SourceUrl, CancellationToken.None);

        var signedParameters = handler.Form
            .Where(pair => pair.Key is not ("file" or "api_key" or "signature"))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal(CloudinarySignature.Sign(signedParameters, Credentials.ApiSecret), handler.Form["signature"]);
        Assert.DoesNotContain(Credentials.ApiSecret, string.Join("&", handler.Form.Values));
    }

    [Fact]
    public async Task UploadFromUrlAsync_CloudinaryRejectsTheUrl_ThrowsWithTheCloudinaryMessage()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, """{"error":{"message":"Invalid image file"}}""");

        var error = await Assert.ThrowsAsync<ImageUploadException>(
            () => CreateUploader(handler).UploadFromUrlAsync(PublicId, SourceUrl, CancellationToken.None));

        Assert.Contains("Invalid image file", error.Message);
        Assert.DoesNotContain(Credentials.ApiSecret, error.Message);
    }

    [Fact]
    public async Task UploadFromUrlAsync_ErrorWithoutJsonBody_StillThrowsWithTheStatusCode()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadGateway, "<html>bad gateway</html>");

        var error = await Assert.ThrowsAsync<ImageUploadException>(
            () => CreateUploader(handler).UploadFromUrlAsync(PublicId, SourceUrl, CancellationToken.None));

        Assert.Contains("502", error.Message);
    }

    [Fact]
    public async Task UploadFromUrlAsync_NetworkFailure_ThrowsImageUploadException()
    {
        var handler = new ThrowingHandler(new HttpRequestException("connection reset"));

        var error = await Assert.ThrowsAsync<ImageUploadException>(
            () => CreateUploader(handler).UploadFromUrlAsync(PublicId, SourceUrl, CancellationToken.None));

        Assert.Contains("connection reset", error.Message);
    }

    [Fact]
    public async Task UploadFromUrlAsync_NewAsset_ReturnsUploaded()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"public_id":"x","bytes":1000,"format":"webp"}""");

        var outcome = await CreateUploader(handler).UploadFromUrlAsync(PublicId, SourceUrl, CancellationToken.None);

        Assert.Equal(ImageUploadOutcome.Uploaded, outcome);
    }

    [Fact]
    public async Task UploadFromUrlAsync_CloudinaryReportsExisting_ReturnsAlreadyExisted()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"public_id":"x","bytes":1000,"format":"webp","existing":true}""");

        var outcome = await CreateUploader(handler).UploadFromUrlAsync(PublicId, SourceUrl, CancellationToken.None);

        Assert.Equal(ImageUploadOutcome.AlreadyExisted, outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData((HttpStatusCode)420)]
    public async Task UploadFromUrlAsync_RateLimited_ThrowsImageRateLimitedException(HttpStatusCode status)
    {
        var handler = new RecordingHandler(status, """{"error":{"message":"Rate Limit Exceeded"}}""");

        await Assert.ThrowsAsync<ImageRateLimitedException>(
            () => CreateUploader(handler).UploadFromUrlAsync(PublicId, SourceUrl, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public async Task UploadFromUrlAsync_ErrorStatus_IsTransientOnlyForServerErrors(HttpStatusCode status, bool expected)
    {
        var handler = new RecordingHandler(status, """{"error":{"message":"x"}}""");

        var error = await Assert.ThrowsAsync<ImageUploadException>(
            () => CreateUploader(handler).UploadFromUrlAsync(PublicId, SourceUrl, CancellationToken.None));

        Assert.Equal(expected, error.IsTransient);
    }

    [Fact]
    public async Task UploadFromUrlAsync_NetworkFailureAndTimeout_AreTransient()
    {
        var network = await Assert.ThrowsAsync<ImageUploadException>(() => CreateUploader(
            new ThrowingHandler(new HttpRequestException("connection reset")))
            .UploadFromUrlAsync(PublicId, SourceUrl, CancellationToken.None));
        var timeout = await Assert.ThrowsAsync<ImageUploadException>(() => CreateUploader(
            new ThrowingHandler(new TaskCanceledException("timeout", new TimeoutException())))
            .UploadFromUrlAsync(PublicId, SourceUrl, CancellationToken.None));

        Assert.True(network.IsTransient);
        Assert.True(timeout.IsTransient);
    }
}
