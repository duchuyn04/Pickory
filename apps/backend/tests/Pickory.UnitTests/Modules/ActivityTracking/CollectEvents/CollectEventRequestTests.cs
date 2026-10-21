using System.Text;
using FluentValidation;
using Pickory.Modules.ActivityTracking.Features.CollectEvents;
using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.UnitTests.Modules.ActivityTracking.CollectEvents;

// ACT-06: luật body chặt của `POST /api/events`. Body sai phải bị từ chối trước khi chạm giới hạn tần suất hay bộ đếm.
public sealed class CollectEventRequestTests
{
    private static async Task<CollectEventRequest> ReadAsync(string rawBody, long? contentLength = null)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rawBody));
        // Chờ đọc xong trước khi `using` giải phóng stream. Bỏ `await` thì stream đóng khi phép đọc còn chạy.
        return await CollectEventRequest.ReadAsync(stream, contentLength, CancellationToken.None);
    }

    // Body hợp lệ thêm khoảng trắng cho đủ đúng số byte. Khoảng trắng cuối JSON không đổi nghĩa của body.
    private static string ValidBodyWithLength(int byteCount)
    {
        const string validBody = """{"type":"product_view"}""";
        return validBody + new string(' ', byteCount - validBody.Length);
    }

    [Fact]
    public async Task ReadAsync_ValidProductViewBody_ReturnsProductViewType()
    {
        var request = await ReadAsync("""{"type":"product_view"}""");

        Assert.Equal("product_view", request.Type);
    }

    [Theory]
    // Gốc không phải đối tượng JSON: lỗi gắn với cả body, khóa rỗng.
    [InlineData("[]", "")]
    [InlineData("\"product_view\"", "")]
    [InlineData("42", "")]
    [InlineData("null", "")]
    [InlineData("not json", "")]
    [InlineData("{\"type\":", "")]
    [InlineData("", "")]
    // Đối tượng hợp lệ nhưng thuộc tính sai: lỗi gắn với khóa `type`.
    [InlineData("{}", "type")]
    [InlineData("""{"type":null}""", "type")]
    [InlineData("""{"type":1}""", "type")]
    [InlineData("""{"type":"category_view"}""", "type")]
    [InlineData("""{"type":"PRODUCT_VIEW"}""", "type")]
    [InlineData("""{"type":"product_view "}""", "type")]
    [InlineData("""{"type":"product_view","productId":"p1"}""", "type")]
    [InlineData("""{"Type":"product_view"}""", "type")]
    [InlineData("""{"type":"product_view","type":"product_view"}""", "type")]
    public async Task ReadAsync_InvalidBody_ThrowsValidationExceptionWithExpectedKey(string rawBody, string expectedKey)
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(() => ReadAsync(rawBody));

        var failure = Assert.Single(exception.Errors);
        Assert.Equal(expectedKey, failure.PropertyName);
    }

    [Fact]
    public async Task ReadAsync_BodyOfExactlyMaxBytes_ReturnsRequest()
    {
        var request = await ReadAsync(ValidBodyWithLength(CollectEventRequest.MaxBodyBytes));

        Assert.Equal("product_view", request.Type);
    }

    [Fact]
    public async Task ReadAsync_BodyOneByteOverLimit_Throws413()
    {
        var exception = await Assert.ThrowsAsync<AppException>(
            () => ReadAsync(ValidBodyWithLength(CollectEventRequest.MaxBodyBytes + 1)));

        Assert.Equal("PAYLOAD_TOO_LARGE", exception.Code);
        Assert.Equal(413, exception.StatusCode);
    }

    [Fact]
    public async Task ReadAsync_DeclaredLengthOverLimit_Throws413WithoutReadingBody()
    {
        // Stream rỗng chứng minh lỗi đến từ độ dài khai báo, không đến từ việc đọc body.
        var exception = await Assert.ThrowsAsync<AppException>(
            () => ReadAsync(string.Empty, contentLength: CollectEventRequest.MaxBodyBytes + 1));

        Assert.Equal("PAYLOAD_TOO_LARGE", exception.Code);
        Assert.Equal(413, exception.StatusCode);
    }
}
