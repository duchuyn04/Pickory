using System.Globalization;

namespace Pickory.Modules.SupportDesk.Sessions;

// Rút gọn nội dung tin nhắn phục vụ hiển thị hàng chờ hỗ trợ.
internal static class SupportSessionPreview
{
    // Giữ đoạn trích ngắn để một dòng hàng chờ không bị tràn.
    public const int MaxPreviewElements = 200;

    // Rút gọn chuỗi thành tối đa 200 phần tử văn bản để tránh cắt đôi ký tự.
    public static string Truncate(string? text, int maxElements = MaxPreviewElements)
    {
        // Trả về chuỗi rỗng khi không có nội dung để tránh lỗi tham chiếu rỗng.
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Đếm theo text element để nhận diện chính xác ký tự tổ hợp và emoji.
        var stringInfo = new StringInfo(text);
        if (stringInfo.LengthInTextElements <= maxElements)
        {
            return text;
        }

        // Cắt đúng số phần tử văn bản yêu cầu mà không làm rách ký tự.
        return stringInfo.SubstringByTextElements(0, maxElements);
    }
}
