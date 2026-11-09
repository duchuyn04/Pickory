using System.Globalization;
using Pickory.Modules.SupportDesk.Sessions;
using Xunit;

namespace Pickory.UnitTests.Modules.SupportDesk.Sessions;

// Kiểm tra hàm thuần rút gọn nội dung tin nhắn phục vụ hiển thị hàng chờ.
public sealed class SupportSessionPreviewTests
{
    [Fact]
    // Chuỗi rỗng hoặc giá trị rỗng phải trả về chuỗi rỗng.
    public void Truncate_NullOrEmpty_ReturnsEmpty()
    {
        // Kiểm tra xử lý giá trị rỗng để tránh lỗi tham chiếu rỗng ở tầng hiển thị.
        Assert.Equal(string.Empty, SupportSessionPreview.Truncate(null));
        Assert.Equal(string.Empty, SupportSessionPreview.Truncate(string.Empty));
    }

    [Fact]
    // Chuỗi có độ dài nhỏ hơn hoặc bằng 200 ký tự phải được giữ nguyên.
    public void Truncate_200CharactersOrLess_ReturnsOriginalString()
    {
        // Chuỗi ngắn không cần cắt ngắn để người đọc xem trọn nội dung.
        var shortText = "Đơn hàng PK123 của mình giao tới đâu rồi?";
        Assert.Equal(shortText, SupportSessionPreview.Truncate(shortText));

        // Chuỗi đúng 200 ký tự phải giữ nguyên toàn bộ 200 ký tự.
        var exactly200 = new string('a', 200);
        Assert.Equal(exactly200, SupportSessionPreview.Truncate(exactly200));
    }

    [Fact]
    // Chuỗi có 201 ký tự phải bị cắt còn đúng 200 phần tử văn bản.
    public void Truncate_201Characters_TruncatesTo200TextElements()
    {
        // Tạo chuỗi 201 ký tự để kiểm tra giới hạn cắt chuỗi.
        var text201 = new string('a', 201);
        var truncated = SupportSessionPreview.Truncate(text201);

        // Độ dài chuỗi sau khi cắt phải đúng 200 phần tử văn bản.
        Assert.Equal(200, new StringInfo(truncated).LengthInTextElements);
        Assert.Equal(new string('a', 200), truncated);
    }

    [Fact]
    // Ký tự emoji dạng cặp surrogate không được bị cắt đôi làm hỏng dữ liệu chuỗi.
    public void Truncate_EmojiSurrogatePair_DoesNotSplitSurrogatePair()
    {
        // Ghép 199 ký tự thường với emoji tạo thành phần tử thứ 200 và thêm ký tự thứ 201.
        var text = new string('a', 199) + "🎉" + "b";
        var truncated = SupportSessionPreview.Truncate(text);

        // Ký tự emoji nằm ở vị trí thứ 200 phải còn nguyên vẹn.
        Assert.Equal(200, new StringInfo(truncated).LengthInTextElements);
        Assert.Equal(new string('a', 199) + "🎉", truncated);
    }

    [Fact]
    // Chữ tiếng Việt dạng tổ hợp không được bị tách dấu thanh khỏi nguyên âm.
    public void Truncate_VietnameseCombiningMarks_DoesNotSplitCombiningMark()
    {
        // Ký tự 'e' ghép với dấu sắc tổ hợp U+0301 tính là một phần tử văn bản.
        var combiningE = "e\u0301";
        var text = new string('a', 199) + combiningE + "b";
        var truncated = SupportSessionPreview.Truncate(text);

        // Phần tử thứ 200 phải giữ trọn vẹn cả nguyên âm và dấu thanh tổ hợp.
        Assert.Equal(200, new StringInfo(truncated).LengthInTextElements);
        Assert.Equal(new string('a', 199) + combiningE, truncated);
    }
}
