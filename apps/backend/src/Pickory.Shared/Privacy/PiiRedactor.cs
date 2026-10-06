using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pickory.Shared.Privacy;

// Ẩn dữ liệu cá nhân trước khi text rời hệ thống, giống `shared/privacy.ts` của bản Node.
// Đây là lớp chặn cuối, nên module gọi vẫn không được đưa tên, địa chỉ hay ID người dùng vào prompt.
public static partial class PiiRedactor
{
    public const string HiddenValue = "[đã ẩn]";

    // Mẫu chỉ nhận chữ số ASCII `[0-9]`. `\d` của .NET nhận cả chữ số Unicode, khác regex của bản Node.
    [GeneratedRegex(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}.-]+\.[\p{L}]{2,}")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:\+84|0084|84|0)[ .-]?[35789](?:[ .-]?[0-9]){8}(?![0-9])")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(
        @"((?:số\s+tài\s+khoản|tài\s+khoản|stk|account(?:\s+number)?)\s*[:#-]?\s*)((?:[0-9][ -]?){7,18}[0-9])(?![0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LabelledAccountPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])[0-9](?:[ -]?[0-9]){11,18}(?![\p{L}\p{N}])")]
    private static partial Regex LongNumberPattern();

    public static string Redact(string text)
    {
        // Ẩn email trước vì địa chỉ email có thể chứa chuỗi số giống số điện thoại.
        var withoutEmails = EmailPattern().Replace(text, HiddenValue);
        // Chỉ nhận đầu số di động Việt Nam đủ độ dài. Giá hoặc mã sản phẩm ngắn vẫn giữ nguyên.
        var withoutPhones = PhonePattern().Replace(withoutEmails, HiddenValue);
        // Nhãn tài khoản cho phép ẩn số ngắn. Số tiền không có nhãn ngân hàng vẫn giữ nguyên.
        var withoutLabelledAccounts = LabelledAccountPattern().Replace(withoutPhones, $"$1{HiddenValue}");
        // Chuỗi 12 đến 19 chữ số là số thẻ hoặc số tài khoản dài, kể cả khi có dấu cách phân nhóm.
        return LongNumberPattern().Replace(withoutLabelledAccounts, HiddenValue);
    }

    // Ẩn theo cây JSON và trả cây mới. Cây gốc không đổi, nên tool cục bộ vẫn nhận input gốc.
    public static JsonNode? RedactValue(JsonNode? value)
    {
        return value switch
        {
            null => null,
            JsonObject jsonObject => RedactObject(jsonObject),
            JsonArray jsonArray => new JsonArray(jsonArray.Select(RedactValue).ToArray()),
            JsonValue jsonValue => RedactScalar(jsonValue),
            _ => value.DeepClone(),
        };
    }

    private static JsonObject RedactObject(JsonObject jsonObject)
    {
        var result = new JsonObject();
        foreach (var (key, entry) in jsonObject)
        {
            // Khóa cũng có thể là email hoặc số điện thoại. Hai khóa trùng sau khi ẩn thì khóa sau thắng, giống bản Node.
            result[Redact(key)] = RedactValue(entry);
        }

        return result;
    }

    private static JsonNode RedactScalar(JsonValue jsonValue)
    {
        var kind = jsonValue.GetValueKind();
        if (kind == JsonValueKind.String)
        {
            return JsonValue.Create(Redact(jsonValue.GetValue<string>()));
        }

        if (kind == JsonValueKind.Number)
        {
            // Số nhạy cảm đổi thành chuỗi trước khi serialize. Thay regex trên JSON đã serialize sẽ làm hỏng cú pháp.
            var numberText = jsonValue.ToJsonString();
            var redactedText = Redact(numberText);
            return redactedText == numberText ? jsonValue.DeepClone() : JsonValue.Create(redactedText);
        }

        return jsonValue.DeepClone();
    }
}
