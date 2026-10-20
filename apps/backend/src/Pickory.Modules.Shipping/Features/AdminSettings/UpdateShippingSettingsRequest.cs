using System.Globalization;
using System.Text.Json;
using FluentValidation.Results;
using Pickory.Modules.Shipping.Settings;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Shipping.Features.AdminSettings;

// Đọc body của `PUT /api/admin/shipping/settings` theo luật chặt.
// Binder mặc định bỏ qua trường lạ và nhận số dạng chuỗi, nên lớp này đọc JSON thủ công.
internal static class UpdateShippingSettingsRequest
{
    public const string SenderNameField = "senderName";
    public const string SenderPhoneField = "senderPhone";
    public const string ProvinceCodeField = "provinceCode";
    public const string WardCodeField = "wardCode";
    public const string LineField = "line";
    public const string DefaultServiceTypeIdField = "defaultServiceTypeId";

    public const int LineMaxLength = 200;

    // GHN dùng 2 cho hàng nhẹ và 5 cho hàng nặng. Số khác không phải loại dịch vụ hợp lệ.
    public static readonly int[] AllowedServiceTypeIds = [2, 5];

    private static readonly string[] KnownFields =
        [SenderNameField, SenderPhoneField, ProvinceCodeField, WardCodeField, LineField, DefaultServiceTypeIdField];

    // Thêm mọi lỗi vào `failures` rồi trả giá trị đọc được. Nơi gọi phải dừng khi `failures` không rỗng.
    public static ShippingSettingsInput Read(JsonElement? body, List<ValidationFailure> failures)
    {
        // Body không phải đối tượng JSON thì không có trường nào để đọc tiếp.
        if (body is not { ValueKind: JsonValueKind.Object } json)
        {
            failures.Add(new ValidationFailure(string.Empty, "Dữ liệu gửi lên phải là một đối tượng JSON."));
            return new ShippingSettingsInput(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, null);
        }

        // Trường lạ bị từ chối. Cấu hình không nhận dữ liệu ngoài ý muốn, kể cả tên tỉnh hay tên phường do client tự gửi.
        foreach (var property in json.EnumerateObject())
        {
            if (!KnownFields.Contains(property.Name, StringComparer.Ordinal))
            {
                failures.Add(new ValidationFailure(property.Name, "Trường này không được hỗ trợ."));
            }
        }

        return new ShippingSettingsInput(
            ReadSenderName(json, failures),
            ReadSenderPhone(json, failures),
            ReadRequiredText(json, ProvinceCodeField, "Cần chọn tỉnh/thành.", failures),
            ReadRequiredText(json, WardCodeField, "Cần chọn phường/xã.", failures),
            ReadLine(json, failures),
            ReadServiceTypeId(json, failures));
    }

    private static string ReadSenderName(JsonElement json, List<ValidationFailure> failures)
    {
        var name = ReadRequiredText(json, SenderNameField, "Cần nhập tên người gửi.", failures);
        // Tên trống đã có lỗi bắt buộc. Không chạy thêm luật tên để tránh hai lỗi cho một trường.
        if (name.Length == 0)
        {
            return name;
        }

        // Dùng chung luật tên với hồ sơ người dùng. Tên gửi GHN không chứa ký tự điều khiển và không quá dài.
        var nameError = ContactFieldRules.GetNameError(name);
        if (nameError is not null)
        {
            failures.Add(new ValidationFailure(SenderNameField, nameError));
        }

        return name;
    }

    private static string ReadSenderPhone(JsonElement json, List<ValidationFailure> failures)
    {
        var phone = ReadRequiredText(json, SenderPhoneField, "Cần nhập số điện thoại người gửi.", failures);
        if (phone.Length == 0)
        {
            return phone;
        }

        // Luật số điện thoại chấp nhận số trống, nên bước trên kiểm bắt buộc và bước này kiểm định dạng.
        var normalizedPhone = ContactFieldRules.NormalizePhone(phone);
        if (normalizedPhone is null)
        {
            failures.Add(new ValidationFailure(SenderPhoneField, ContactFieldRules.PhoneInvalidMessage));
            return phone;
        }

        // GHN và đơn hàng cần một dạng số duy nhất `0xxxxxxxxx`.
        return normalizedPhone;
    }

    private static string ReadLine(JsonElement json, List<ValidationFailure> failures)
    {
        var line = ReadRequiredText(json, LineField, "Cần nhập địa chỉ chi tiết.", failures);
        if (line.Length == 0)
        {
            return line;
        }

        // Ký tự điều khiển (kể cả xuống dòng) làm hỏng nhãn vận đơn và log. Kiểm trước độ dài để lỗi chỉ ra nguyên nhân chính.
        if (line.Any(character => character <= '\u001F' || character == '\u007F'))
        {
            failures.Add(new ValidationFailure(LineField, "Địa chỉ chi tiết chứa ký tự không hợp lệ."));
            return line;
        }

        // Đếm theo ký tự người đọc thấy, giống luật tên. Chữ có dấu dạng tổ hợp vẫn tính là một ký tự.
        if (new StringInfo(line).LengthInTextElements > LineMaxLength)
        {
            failures.Add(new ValidationFailure(LineField, $"Địa chỉ chi tiết tối đa {LineMaxLength} ký tự."));
        }

        return line;
    }

    // Trả chuỗi đã cắt khoảng trắng. Trả chuỗi rỗng và ghi lỗi khi thiếu, sai kiểu hoặc chỉ có khoảng trắng.
    private static string ReadRequiredText(JsonElement json, string field, string missingMessage, List<ValidationFailure> failures)
    {
        // Thiếu trường hoặc `null` đều là chưa nhập.
        if (!json.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            failures.Add(new ValidationFailure(field, missingMessage));
            return string.Empty;
        }

        // Số hay đối tượng không được ép thành chuỗi. Chuỗi là kiểu duy nhất hợp lệ.
        if (value.ValueKind != JsonValueKind.String)
        {
            failures.Add(new ValidationFailure(field, $"{field} phải là chuỗi."));
            return string.Empty;
        }

        var text = (value.GetString() ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            failures.Add(new ValidationFailure(field, missingMessage));
        }

        return text;
    }

    private static int? ReadServiceTypeId(JsonElement json, List<ValidationFailure> failures)
    {
        // Thiếu trường hoặc `null` nghĩa là chưa chọn gói. SHIP-05 khi đó dùng loại 2.
        if (!json.TryGetProperty(DefaultServiceTypeIdField, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        // Chuỗi "2" bị từ chối. Số lẻ như 2.5 không qua `TryGetInt32`.
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var serviceTypeId)
            || !AllowedServiceTypeIds.Contains(serviceTypeId))
        {
            failures.Add(new ValidationFailure(
                DefaultServiceTypeIdField, "Gói dịch vụ mặc định phải là 2, 5 hoặc để trống."));
            return null;
        }

        return serviceTypeId;
    }
}
