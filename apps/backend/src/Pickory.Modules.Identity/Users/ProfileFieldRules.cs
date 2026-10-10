using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Users;

// Lớp này chuyển sang `ContactFieldRules` để các chỗ gọi trong Identity không phải đổi.
// Frontend có luật cùng nội dung ở `identity-validation.ts`.
public static class ProfileFieldRules
{
    public const int NameMaxLength = ContactFieldRules.NameMaxLength;
    public const string NameTooLongMessage = ContactFieldRules.NameTooLongMessage;
    public const string NameInvalidCharacterMessage = ContactFieldRules.NameInvalidCharacterMessage;
    public const string PhoneInvalidMessage = ContactFieldRules.PhoneInvalidMessage;

    // Trả `null` khi tên hợp lệ. Tên trống hợp lệ vì nó xóa tên đã lưu.
    public static string? GetNameError(string? name) => ContactFieldRules.GetNameError(name);

    // Trả `null` khi số hợp lệ. Số trống hợp lệ vì nó xóa số đã lưu.
    public static string? GetPhoneError(string? phone) => ContactFieldRules.GetPhoneError(phone);

    // Trả số ở dạng `0xxxxxxxxx`. Số trống hoặc sai định dạng trả `null`, nên gọi `GetPhoneError` trước.
    public static string? NormalizePhone(string? phone) => ContactFieldRules.NormalizePhone(phone);
}
