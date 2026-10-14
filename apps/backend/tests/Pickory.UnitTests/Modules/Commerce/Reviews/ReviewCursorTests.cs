using Pickory.Modules.Commerce.Reviews;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Reviews;

public sealed class ReviewCursorTests
{
    [Fact]
    // Cursor mã hóa rồi giải mã trả đúng thời điểm và `Id`, kể cả phần nhỏ của giây.
    public void EncodeThenDecode_RoundTripsCreatedAtAndId()
    {
        var createdAt = new DateTime(2026, 10, 14, 8, 30, 15, 123, DateTimeKind.Utc);

        var cursor = ReviewCursor.Encode(createdAt, "review-abc-123");
        var decoded = ReviewCursor.TryDecode(cursor, out var decodedAt, out var decodedId);

        Assert.True(decoded);
        Assert.Equal(createdAt, decodedAt);
        Assert.Equal("review-abc-123", decodedId);
    }

    [Fact]
    // Cursor đi trong query string, nên chỉ chứa ký tự an toàn của URL.
    public void Encode_ProducesUrlSafeText()
    {
        var cursor = ReviewCursor.Encode(DateTime.UtcNow, "id/with+odd?chars");

        Assert.Matches("^[A-Za-z0-9_-]+$", cursor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("khong-phai-cursor")]
    // Chuỗi base64url hợp lệ nhưng nội dung không đúng dạng.
    [InlineData("YWJj")]
    // Chuỗi có ký tự ngoài bảng base64url.
    [InlineData("a+b/c=")]
    public void TryDecode_MalformedValue_ReturnsFalse(string value)
    {
        Assert.False(ReviewCursor.TryDecode(value, out _, out _));
    }

    [Fact]
    // Số tick ngoài khoảng của `DateTime` là cursor sai, không phải lỗi 500.
    public void TryDecode_TicksOutOfRange_ReturnsFalse()
    {
        var cursor = ForgeCursor("99999999999999999999:id");

        Assert.False(ReviewCursor.TryDecode(cursor, out _, out _));
    }

    private static string ForgeCursor(string plain) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plain)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
