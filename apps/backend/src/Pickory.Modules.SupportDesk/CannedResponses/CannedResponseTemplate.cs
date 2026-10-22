using System.Text.RegularExpressions;

namespace Pickory.Modules.SupportDesk.CannedResponses;

// Hàm thuần điền biến vào câu trả lời mẫu. Chỉ hai biến `{ten_khach}` và `{ma_don}` được thay.
internal static partial class CannedResponseTemplate
{
    // Chỉ khớp đúng hai tên biến đã hỗ trợ. Chuỗi trong ngoặc nhọn khác không khớp nên giữ nguyên.
    [GeneratedRegex(@"\{(ten_khach|ma_don)\}")]
    private static partial Regex SupportedVariablePattern();

    public static string Render(string body, IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(values);

        // Thay mọi biến trong một lượt duy nhất. Giá trị do khách nhập chứa `{ma_don}` sẽ không bị thay lần hai.
        return SupportedVariablePattern().Replace(body, match => ResolveVariable(match, values));
    }

    private static string ResolveVariable(Match match, IReadOnlyDictionary<string, string?> values)
    {
        var variableName = match.Groups[1].Value;

        // Thiếu khóa, giá trị `null` hoặc rỗng thì giữ nguyên chuỗi biến. Câu mẫu không bị cụt chữ và nhân viên thấy chỗ cần điền.
        if (!values.TryGetValue(variableName, out var value) || string.IsNullOrEmpty(value))
        {
            return match.Value;
        }

        return value;
    }
}
