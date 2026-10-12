using FluentValidation;
using FluentValidation.Results;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Commerce.Orders;

// Luật của header `Idempotency-Key` ở route đặt đơn.
public static class IdempotencyKeyRules
{
    public const string HeaderName = "Idempotency-Key";
    public const string KeyField = "idempotencyKey";
    public const int MinLength = 16;
    public const int MaxLength = 100;

    private const string RequiredMessage = "Thiếu header Idempotency-Key.";
    private const string FormatMessage = "Idempotency-Key gồm 16 đến 100 ký tự: chữ cái ASCII, chữ số, dấu gạch ngang hoặc gạch dưới.";

    // Trả khóa hợp lệ. Thiếu khóa ném 400 `IDEMPOTENCY_KEY_REQUIRED`, khóa sai định dạng ném lỗi kiểm dữ liệu.
    public static string Read(string? headerValue)
    {
        // Thiếu khóa là lỗi riêng. Client phải biết mình quên header, không phải gửi sai giá trị.
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            throw new AppException("IDEMPOTENCY_KEY_REQUIRED", 400, RequiredMessage);
        }

        // Không cắt khoảng trắng. Khóa có khoảng trắng bị từ chối, vì client khác nhau có thể cắt khác nhau và sinh hai khóa cho một lần đặt.
        if (headerValue.Length < MinLength || headerValue.Length > MaxLength || !headerValue.All(IsAllowedCharacter))
        {
            throw new ValidationException([new ValidationFailure(KeyField, FormatMessage)]);
        }

        return headerValue;
    }

    // Chỉ nhận ASCII. `char.IsLetterOrDigit` nhận cả chữ có dấu, mà khóa này nằm trong cột unique và log.
    private static bool IsAllowedCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
}
