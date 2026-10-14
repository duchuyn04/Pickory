using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Pickory.Modules.Commerce.Reviews;

// Cursor phân trang của danh sách đánh giá. Cursor là chuỗi mờ gồm thời điểm tạo và `Id` của dòng cuối trang trước.
internal static class ReviewCursor
{
    private const char Separator = ':';

    public static string Encode(DateTime createdAt, string id)
    {
        // Dùng tick thay cho chuỗi ngày, để giá trị không phụ thuộc văn hóa và không mất độ chính xác.
        var plain = string.Create(CultureInfo.InvariantCulture, $"{createdAt.Ticks}{Separator}{id}");
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(plain));
    }

    // Trả `false` với mọi chuỗi không phải cursor do `Encode` tạo. Bên gọi trả 400 thay vì lỗi 500.
    public static bool TryDecode(string? value, out DateTime createdAt, out string id)
    {
        createdAt = default;
        id = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string plain;
        try
        {
            plain = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(value));
        }
        catch (FormatException)
        {
            return false;
        }

        // Tách ở dấu `:` đầu tiên, vì `Id` có thể chứa ký tự bất kỳ còn tick chỉ là chữ số.
        var separatorIndex = plain.IndexOf(Separator);
        if (separatorIndex <= 0 || separatorIndex == plain.Length - 1)
        {
            return false;
        }

        if (!long.TryParse(plain.AsSpan(0, separatorIndex), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        createdAt = new DateTime(ticks, DateTimeKind.Utc);
        id = plain[(separatorIndex + 1)..];
        return true;
    }
}
