using System.Text.Json;
using System.Text.Json.Nodes;
using FluentValidation;

namespace Pickory.Modules.AiGateway.Contracts.Chat;

// Đọc JSON do model sinh thành kiểu C#, thay cho schema Zod của bản Node.
// Input của tool và kết quả `ChatJsonAsync` dùng chung quy tắc đọc này.
public static class AiJsonReader
{
    // Thiếu trường bắt buộc hoặc `null` ở trường không cho null đều là lỗi. Mặc định của .NET bỏ qua hai lỗi này.
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public static AiJsonReadResult<T> Read<T>(JsonNode? json, IValidator<T>? validator)
    {
        T? value;
        try
        {
            value = json.Deserialize<T>(Options);
        }
        catch (JsonException exception)
        {
            // Thông báo của `JsonException` chỉ nêu đường dẫn và kiểu, không chứa giá trị model đã gửi.
            return new AiJsonReadResult<T>(false, default, $"{ToFieldPath(exception.Path)}: {exception.Message}");
        }

        if (value is null)
        {
            return new AiJsonReadResult<T>(false, default, "root: giá trị null không hợp lệ.");
        }

        if (validator is null)
        {
            return new AiJsonReadResult<T>(true, value, null);
        }

        // Validator của module gọi kiểm tra quy tắc nghiệp vụ mà kiểu C# không diễn đạt được.
        var validation = validator.Validate(value);
        if (validation.IsValid)
        {
            return new AiJsonReadResult<T>(true, value, null);
        }

        var details = string.Join("; ", validation.Errors.Select(failure =>
            $"{ToFieldPath(failure.PropertyName)}: {failure.ErrorMessage}"));
        return new AiJsonReadResult<T>(false, default, details);
    }

    // Đổi `$.price` hoặc `Price` thành `price`, giống đường dẫn lỗi của Zod.
    private static string ToFieldPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "$")
        {
            return "root";
        }

        var trimmedPath = path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path;
        var segments = trimmedPath.Split('.')
            .Select(segment => segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]);
        return string.Join('.', segments);
    }
}
