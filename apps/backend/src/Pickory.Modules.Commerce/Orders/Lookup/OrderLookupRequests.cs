using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Commerce.Orders.Lookup;

// Đọc và kiểm tra định dạng dữ liệu gửi lên route tra cứu đơn.
public static class OrderLookupRequests
{
    public const string OrderCodeField = "orderCode";
    public const string EmailField = "email";

    private const string BodyMustBeObjectMessage = "Dữ liệu gửi lên phải là một đối tượng JSON.";
    private const string OrderCodeRequiredMessage = "Cần nhập mã đơn hàng.";
    private const string OrderCodeInvalidMessage = "Mã đơn hàng không đúng định dạng.";
    private const string EmailRequiredMessage = "Cần nhập email.";
    private const string EmailInvalidMessage = "Email không đúng định dạng.";

    // Đọc body JSON và trả dữ liệu đã chuẩn hóa.
    public static OrderLookupInput Read(JsonElement? body)
    {
        var failures = new List<ValidationFailure>();

        // Body không phải đối tượng JSON thì dừng ngay và gắn lỗi với cả body.
        if (body is not { ValueKind: JsonValueKind.Object } json)
        {
            failures.Add(new ValidationFailure(string.Empty, BodyMustBeObjectMessage));
            throw new ValidationException(failures);
        }

        var orderCode = ReadOrderCode(json, failures);
        var email = ReadEmail(json, failures);

        // Gom mọi lỗi vào một ngoại lệ để UI hiển thị đúng từng ô.
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return new OrderLookupInput(orderCode, email);
    }

    private static string ReadOrderCode(JsonElement json, List<ValidationFailure> failures)
    {
        // Thiếu thuộc tính hoặc giá trị không phải chuỗi thì báo lỗi cần nhập mã đơn.
        if (!json.TryGetProperty(OrderCodeField, out var element) || element.ValueKind != JsonValueKind.String)
        {
            failures.Add(new ValidationFailure(OrderCodeField, OrderCodeRequiredMessage));
            return string.Empty;
        }

        var raw = element.GetString();
        // Chuỗi chỉ có khoảng trắng cũng bị tính là thiếu mã đơn.
        if (string.IsNullOrWhiteSpace(raw))
        {
            failures.Add(new ValidationFailure(OrderCodeField, OrderCodeRequiredMessage));
            return string.Empty;
        }

        // Chuẩn hóa mã đơn bằng cách cắt khoảng trắng và đổi sang chữ hoa.
        var normalized = OrderLookupNormalizer.NormalizeOrderCode(raw);

        // Kiểm tra độ dài, tiền tố và các ký tự trong bảng chữ hợp lệ của mã đơn.
        if (!IsValidOrderCode(normalized))
        {
            failures.Add(new ValidationFailure(OrderCodeField, OrderCodeInvalidMessage));
            return string.Empty;
        }

        return normalized;
    }

    private static string ReadEmail(JsonElement json, List<ValidationFailure> failures)
    {
        // Thiếu thuộc tính hoặc giá trị không phải chuỗi thì báo lỗi cần nhập email.
        if (!json.TryGetProperty(EmailField, out var element) || element.ValueKind != JsonValueKind.String)
        {
            failures.Add(new ValidationFailure(EmailField, EmailRequiredMessage));
            return string.Empty;
        }

        var raw = element.GetString();
        // Chuỗi chỉ có khoảng trắng cũng bị tính là thiếu email.
        if (string.IsNullOrWhiteSpace(raw))
        {
            failures.Add(new ValidationFailure(EmailField, EmailRequiredMessage));
            return string.Empty;
        }

        var trimmed = raw.Trim();
        // Kiểm tra độ dài theo RFC 5321 và cú pháp địa chỉ email hợp lệ.
        if (trimmed.Length > PlaceOrderRequests.EmailMaxLength || !EmailAddressRule.IsValid(trimmed))
        {
            failures.Add(new ValidationFailure(EmailField, EmailInvalidMessage));
            return string.Empty;
        }

        // Chuẩn hóa email về chữ thường cho các bước tiếp theo.
        return OrderLookupNormalizer.NormalizeEmail(trimmed);
    }

    private static bool IsValidOrderCode(string code)
    {
        // Mã đơn bắt buộc có độ dài cố định theo quy định sinh mã.
        if (code.Length != OrderCodeGenerator.CodeLength)
        {
            return false;
        }

        // Mã đơn phải bắt đầu bằng tiền tố quy định.
        if (!code.StartsWith(OrderCodeGenerator.Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        // Mọi ký tự sau tiền tố phải thuộc bảng chữ hợp lệ của hệ thống.
        for (var i = OrderCodeGenerator.Prefix.Length; i < code.Length; i++)
        {
            if (!OrderCodeGenerator.Alphabet.Contains(code[i]))
            {
                return false;
            }
        }

        return true;
    }
}
