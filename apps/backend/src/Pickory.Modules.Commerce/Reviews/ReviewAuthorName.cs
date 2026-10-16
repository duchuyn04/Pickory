using System.Globalization;
using System.Text.Json;

namespace Pickory.Modules.Commerce.Reviews;

// Tên người viết hiện trên đánh giá công khai. Chỉ lộ tên cuối và chữ cái đầu của họ, để không lộ họ tên đầy đủ.
internal static class ReviewAuthorName
{
    public const string AnonymousLabel = "Khách hàng";

    // Tên "Nguyễn Văn An" thành "An N" kèm dấu chấm. Tên một từ giữ nguyên, và tên trống trả nhãn chung.
    public static string Shorten(string? fullName)
    {
        // Tách theo khoảng trắng và bỏ phần rỗng, vì người dùng hay gõ thừa khoảng trắng.
        var words = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return AnonymousLabel;
        }

        // Một từ thì không có họ để rút gọn.
        if (words.Length == 1)
        {
            return words[0];
        }

        // Lấy trọn một ký tự người đọc thấy. Chữ có dấu dạng tổ hợp không bị cắt rời dấu.
        var firstInitial = new StringInfo(words[0]).SubstringByTextElements(0, 1).ToUpper(CultureInfo.InvariantCulture);
        return $"{words[^1]} {firstInitial}.";
    }

    // Tên hiện trên một đánh giá. Đánh giá đã ẩn danh hiện nhãn chung, dù đơn gắn với đánh giá vẫn giữ tên người nhận.
    public static string ForReview(string? userId, JsonDocument orderContact)
    {
        if (userId is null)
        {
            return AnonymousLabel;
        }

        // Đơn cũ hoặc đơn nhập tay có thể thiếu tên. Khi đó hàm rút gọn trả nhãn chung.
        var recipientName = orderContact.RootElement.ValueKind == JsonValueKind.Object
            && orderContact.RootElement.TryGetProperty("name", out var name)
            && name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : null;
        return Shorten(recipientName);
    }
}
