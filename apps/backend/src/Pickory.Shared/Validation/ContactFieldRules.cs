using System.Globalization;
using System.Text;

namespace Pickory.Shared.Validation;

// Luật tên và số điện thoại dùng chung cho hồ sơ và địa chỉ lấy hàng, đặt ở `Pickory.Shared` vì Shipping không tham chiếu Identity.
// Frontend có luật cùng nội dung ở `identity-validation.ts`.
public static class ContactFieldRules
{
    public const int NameMaxLength = 100;
    public const string NameTooLongMessage = "Họ và tên tối đa 100 ký tự.";
    public const string NameInvalidCharacterMessage = "Họ và tên chứa ký tự không hợp lệ.";
    public const string PhoneInvalidMessage = "Số điện thoại không đúng định dạng.";

    // Chữ số đầu sau số 0 hoặc `+84`. Các đầu số 1, 2, 4, 6 không phải số di động của Việt Nam.
    private const string AllowedSecondDigits = "35789";
    private const int DigitsAfterPrefix = 9;
    private const string InternationalPrefix = "+84";

    // Trả `null` khi tên hợp lệ. Tên trống hợp lệ vì nó xóa tên đã lưu.
    public static string? GetNameError(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // Ký tự điều khiển phá bố cục thư và log. Kiểm trước độ dài để thông báo chỉ ra lỗi chính.
        if (name.Any(IsControlCharacter))
        {
            return NameInvalidCharacterMessage;
        }

        // Đếm theo ký tự người đọc thấy. Chữ có dấu viết dạng tổ hợp vẫn tính là một ký tự.
        var length = new StringInfo(name.Trim()).LengthInTextElements;
        return length > NameMaxLength ? NameTooLongMessage : null;
    }

    // Trả `null` khi số hợp lệ. Số trống hợp lệ vì nó xóa số đã lưu.
    public static string? GetPhoneError(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        return NormalizePhone(phone) is null ? PhoneInvalidMessage : null;
    }

    // Trả số ở dạng `0xxxxxxxxx`. Số trống hoặc sai định dạng trả `null`, nên gọi `GetPhoneError` trước.
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var compact = RemoveSeparators(phone);
        // Hai dạng hợp lệ có cùng phần đuôi. Dạng `+84` thay số 0 đầu bằng mã nước.
        string tail;
        if (compact.StartsWith(InternationalPrefix, StringComparison.Ordinal))
        {
            tail = compact[InternationalPrefix.Length..];
        }
        else if (compact.StartsWith('0'))
        {
            tail = compact[1..];
        }
        else
        {
            return null;
        }

        if (tail.Length != DigitsAfterPrefix || !tail.All(IsAsciiDigit) || !AllowedSecondDigits.Contains(tail[0]))
        {
            return null;
        }

        // Shipping cần một dạng số duy nhất. Mọi dạng hợp lệ lưu về `0xxxxxxxxx`.
        return "0" + tail;
    }

    // Người dùng hay gõ khoảng trắng, dấu chấm và dấu gạch ngang để chia nhóm số.
    private static string RemoveSeparators(string phone)
    {
        var builder = new StringBuilder(phone.Length);
        foreach (var character in phone)
        {
            if (!char.IsWhiteSpace(character) && character != '.' && character != '-')
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static bool IsControlCharacter(char character) => character <= '\u001F' || character == '\u007F';

    // `char.IsDigit` nhận cả chữ số Ả Rập. Số điện thoại chỉ nhận chữ số ASCII.
    private static bool IsAsciiDigit(char character) => character is >= '0' and <= '9';
}
