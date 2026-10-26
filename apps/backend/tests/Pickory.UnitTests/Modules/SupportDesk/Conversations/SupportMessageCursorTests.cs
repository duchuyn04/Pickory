using Pickory.Modules.SupportDesk.Conversations;
using Xunit;

namespace Pickory.UnitTests.Modules.SupportDesk.Conversations;

// SD-15: con trỏ phân trang của lịch sử tin. Con trỏ hỏng phải bị từ chối để route trả 400, không phải 500.
public sealed class SupportMessageCursorTests
{
    [Fact]
    // Mã hóa rồi giải mã trả đúng thời điểm và `Id`, kể cả phần nhỏ của giây.
    public void EncodeThenDecode_RoundTripsCreatedAtAndId()
    {
        var createdAt = new DateTime(2026, 10, 14, 8, 30, 15, 123, DateTimeKind.Utc);

        var cursor = SupportMessageCursor.Encode(createdAt, "message-abc-123");
        var decoded = SupportMessageCursor.TryDecode(cursor, out var decodedAt, out var decodedId);

        Assert.True(decoded);
        Assert.Equal(createdAt, decodedAt);
        Assert.Equal("message-abc-123", decodedId);
    }

    [Fact]
    // `Id` chứa dấu `:` vẫn giải mã đúng, vì tick chỉ gồm chữ số và dấu tách đầu tiên là dấu thật.
    public void EncodeThenDecode_IdContainingSeparator_RoundTrips()
    {
        var createdAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var cursor = SupportMessageCursor.Encode(createdAt, "a:b:c");

        Assert.True(SupportMessageCursor.TryDecode(cursor, out var decodedAt, out var decodedId));
        Assert.Equal(createdAt, decodedAt);
        Assert.Equal("a:b:c", decodedId);
    }

    [Fact]
    // Con trỏ đi trong query string, nên chỉ chứa ký tự an toàn của URL.
    public void Encode_ProducesUrlSafeText()
    {
        var cursor = SupportMessageCursor.Encode(DateTime.UtcNow, "id/with+odd?chars");

        Assert.Matches("^[A-Za-z0-9_-]+$", cursor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("khong-phai-cursor")]
    // Chuỗi base64url hợp lệ nhưng nội dung không đúng dạng.
    [InlineData("YWJj")]
    // Chuỗi có ký tự ngoài bảng base64url.
    [InlineData("a+b/c=")]
    public void TryDecode_MalformedValue_ReturnsFalse(string? value)
    {
        Assert.False(SupportMessageCursor.TryDecode(value, out _, out _));
    }

    [Theory]
    // Số tick vượt khoảng của `DateTime`.
    [InlineData("99999999999999999999:id")]
    // Thiếu `Id` sau dấu tách.
    [InlineData("123:")]
    // Thiếu tick trước dấu tách.
    [InlineData(":id")]
    // Tick âm.
    [InlineData("-5:id")]
    public void TryDecode_ForgedPayload_ReturnsFalse(string plain)
    {
        Assert.False(SupportMessageCursor.TryDecode(ForgeCursor(plain), out _, out _));
    }

    private static string ForgeCursor(string plain) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plain)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
