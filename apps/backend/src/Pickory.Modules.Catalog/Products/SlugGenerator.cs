using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pickory.Modules.Catalog.Products;

// Tạo slug cho URL từ tên tiếng Việt, giống `domain/slug.ts` của bản Node.
public static partial class SlugGenerator
{
    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumericPattern();

    public static string ToSlug(string name)
    {
        // Chữ `đ` không tách được dấu bằng chuẩn hóa Unicode, nên phải đổi sang `d` trước.
        var lower = name.ToLowerInvariant().Replace('đ', 'd');

        // Tách chữ và dấu thành ký tự riêng, rồi bỏ các dấu kết hợp.
        var decomposed = lower.Normalize(NormalizationForm.FormD);
        var withoutAccents = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                withoutAccents.Append(character);
            }
        }

        // Mỗi chuỗi ký tự không phải chữ hoặc số thành một `-`. Hai đầu không giữ `-`.
        return NonAlphanumericPattern().Replace(withoutAccents.ToString(), "-").Trim('-');
    }
}
