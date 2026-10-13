using System.Text.RegularExpressions;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;

namespace Pickory.Shared.Http;

// Từ khóa tìm kiếm của danh sách back-office. `LikePattern` đã escape để module truyền thẳng vào `EF.Functions.ILike`.
public sealed partial record SearchTerm(string Text, string LikePattern)
{
    // Module truyền ký tự này làm tham số `escapeCharacter` của `EF.Functions.ILike`.
    public const string EscapeCharacter = "\\";

    public const int MaxLength = 100;

    private const string ParameterName = "search";

    // Lỗi được thêm vào `failures` và route gom mọi lỗi của query string thành một lỗi `VALIDATION_ERROR`.
    // Trả `null` khi không có từ khóa, để bên gọi bỏ điều kiện tìm.
    public static SearchTerm? Read(IQueryCollection query, List<ValidationFailure> failures)
    {
        if (!query.TryGetValue(ParameterName, out var rawValues))
        {
            return null;
        }

        // Tham số lặp lại là lỗi. Chọn một giá trị âm thầm sẽ cho kết quả khác ý người dùng.
        if (rawValues.Count > 1)
        {
            failures.Add(new ValidationFailure(ParameterName, "Từ khóa tìm kiếm chỉ được gửi một lần."));
            return null;
        }

        // Bỏ khoảng trắng hai đầu và gộp khoảng trắng liền nhau. Ô tìm kiếm có thể dán chuỗi nhiều dòng.
        var text = WhitespaceRun().Replace(rawValues.ToString().Trim(), " ");

        // Chuỗi rỗng sau chuẩn hóa nghĩa là không lọc. Form để trống ô không gây lỗi.
        if (text.Length == 0)
        {
            return null;
        }

        // Giới hạn độ dài sau chuẩn hóa. Một request không dựng mẫu `ILIKE` dài tùy ý.
        if (text.Length > MaxLength)
        {
            failures.Add(new ValidationFailure(ParameterName, $"Từ khóa tìm kiếm không được dài quá {MaxLength} ký tự."));
            return null;
        }

        return new SearchTerm(text, BuildLikePattern(text));
    }

    private static string BuildLikePattern(string text)
    {
        // Escape `\` trước. Nếu escape `%` và `_` trước, dấu `\` vừa thêm sẽ bị escape lần hai.
        var escaped = text
            .Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter, StringComparison.Ordinal)
            .Replace("%", EscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", EscapeCharacter + "_", StringComparison.Ordinal);

        // Hai dấu `%` ở hai đầu cho phép khớp từ khóa ở bất kỳ vị trí nào của cột.
        return "%" + escaped + "%";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
