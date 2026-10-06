using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;

namespace Pickory.Modules.Identity.Features.UpdateProfile;

// Body của `PATCH /api/me`. Route đọc JSON thủ công vì record thường không phân biệt được trường bị thiếu với `null`.
public sealed record UpdateProfileRequest(ProfileFieldUpdate Name, ProfileFieldUpdate Phone)
{
    public static UpdateProfileRequest FromJson(JsonElement? body)
    {
        // Body phải là đối tượng JSON. Lỗi gắn với đường dẫn `body`, giống thông báo của Zod ở bản Node.
        if (body is not { ValueKind: JsonValueKind.Object } jsonObject)
        {
            throw new ValidationException([new ValidationFailure(string.Empty, "Dữ liệu gửi lên phải là một đối tượng JSON.")]);
        }

        var failures = new List<ValidationFailure>();
        var name = ReadField(jsonObject, "name", failures);
        var phone = ReadField(jsonObject, "phone", failures);
        // Báo mọi trường sai trong một lỗi. Người dùng sửa được tất cả trong một lần gửi.
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return new UpdateProfileRequest(name, phone);
    }

    private static ProfileFieldUpdate ReadField(JsonElement jsonObject, string propertyName, List<ValidationFailure> failures)
    {
        if (!jsonObject.TryGetProperty(propertyName, out var value))
        {
            return ProfileFieldUpdate.Missing;
        }

        // Chỉ chuỗi hoặc `null` hợp lệ. Kiểu khác nhận lỗi validate thay vì lỗi 500.
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return new ProfileFieldUpdate(IsProvided: true, value.GetString());
            case JsonValueKind.Null:
                return new ProfileFieldUpdate(IsProvided: true, Value: null);
            default:
                failures.Add(new ValidationFailure(propertyName, "Giá trị phải là chuỗi hoặc null."));
                return ProfileFieldUpdate.Missing;
        }
    }
}
