using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.AccountLock;

// Đọc body của `PATCH` khóa hoặc mở khóa tài khoản. Binder mặc định bỏ qua trường lạ và đổi kiểu ngầm, nên lớp này đọc JSON thủ công.
internal static class AccountLockRequest
{
    private const string LockedField = "locked";

    // Trả `true` để khóa và `false` để mở khóa. Mọi lỗi nằm trong một `ValidationException` để người gọi sửa một lần.
    public static bool ReadLocked(JsonElement? body)
    {
        // Body không phải đối tượng JSON thì không có trường nào để đọc.
        if (body is not { ValueKind: JsonValueKind.Object } json)
        {
            throw new ValidationException([new ValidationFailure(string.Empty, "Dữ liệu gửi lên phải là một đối tượng JSON.")]);
        }

        var failures = new List<ValidationFailure>();

        // Trường lạ bị từ chối. Người gọi không tưởng nhầm rằng trường đó đã được lưu.
        foreach (var property in json.EnumerateObject())
        {
            if (!string.Equals(property.Name, LockedField, StringComparison.Ordinal))
            {
                failures.Add(new ValidationFailure(property.Name, "Trường này không được hỗ trợ."));
            }
        }

        // Thiếu `locked` không được hiểu thành "mở khóa". Body rỗng không được đổi trạng thái tài khoản.
        if (!json.TryGetProperty(LockedField, out var value))
        {
            failures.Add(new ValidationFailure(LockedField, RequestValidation.RequiredMessage));
        }
        else if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            // Chuỗi `"true"`, số và `null` không phải boolean. Từ chối thay vì đoán ý người gọi.
            failures.Add(new ValidationFailure(LockedField, "Giá trị phải là true hoặc false."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return value.GetBoolean();
    }
}
