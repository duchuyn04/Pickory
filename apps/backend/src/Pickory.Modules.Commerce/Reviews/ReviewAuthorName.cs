using System.Globalization;

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
}
