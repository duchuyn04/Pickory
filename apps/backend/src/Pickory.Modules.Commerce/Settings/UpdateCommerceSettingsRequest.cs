using System.Text.Json;
using FluentValidation.Results;

namespace Pickory.Modules.Commerce.Settings;

// Đọc body của `PUT /api/admin/commerce/settings` theo luật chặt, giống route cấu hình shipping.
// Binder mặc định nhận số dạng chuỗi và bỏ qua trường lạ, nên lớp này đọc JSON thủ công.
internal static class UpdateCommerceSettingsRequest
{
    public const string FreeShippingThresholdField = "freeShippingThreshold";
    public const string CodMaxAmountField = "codMaxAmount";
    public const string MaxQuantityPerSkuField = "maxQuantityPerSku";
    public const string MaxCartLinesField = "maxCartLines";
    public const string SepayAccountNumberField = "sepayAccountNumber";
    public const string SepayBankCodeField = "sepayBankCode";

    public const int MoneyMaxAmount = 1_000_000_000;
    public const int MaxQuantityPerSkuUpperBound = 99;
    public const int MaxCartLinesUpperBound = 100;
    public const int AccountNumberMinLength = 6;
    public const int AccountNumberMaxLength = 20;
    public const int BankCodeMinLength = 2;
    public const int BankCodeMaxLength = 20;

    private const string BodyMustBeObjectMessage = "Dữ liệu gửi lên phải là một đối tượng JSON.";
    private const string UnknownFieldMessage = "Trường này không được hỗ trợ.";
    private const string AccountNumberMessage = "Số tài khoản phải gồm từ 6 đến 20 chữ số.";
    private const string BankCodeMessage = "Mã ngân hàng phải gồm từ 2 đến 20 chữ cái hoặc chữ số.";
    private const string PairedFieldMessage = "Cần nhập cả số tài khoản và mã ngân hàng, hoặc để trống cả hai.";

    private static readonly string[] KnownFields =
    [
        FreeShippingThresholdField, CodMaxAmountField, MaxQuantityPerSkuField, MaxCartLinesField,
        SepayAccountNumberField, SepayBankCodeField,
    ];

    // Thêm mọi lỗi vào `failures` rồi trả giá trị đọc được. Nơi gọi phải dừng khi `failures` không rỗng.
    public static CommerceSettingsInput Read(JsonElement? body, List<ValidationFailure> failures)
    {
        // Body không phải đối tượng JSON thì không có trường nào để đọc tiếp.
        if (body is not { ValueKind: JsonValueKind.Object } json)
        {
            failures.Add(new ValidationFailure(string.Empty, BodyMustBeObjectMessage));
            return new CommerceSettingsInput(0, 0, 0, 0, null, null);
        }

        // Trường lạ bị từ chối. Cấu hình không nhận dữ liệu ngoài ý muốn, kể cả khóa webhook do client tự gửi.
        foreach (var property in json.EnumerateObject())
        {
            if (!KnownFields.Contains(property.Name, StringComparer.Ordinal))
            {
                failures.Add(new ValidationFailure(property.Name, UnknownFieldMessage));
            }
        }

        var freeShippingThreshold = ReadInteger(json, FreeShippingThresholdField, 0, MoneyMaxAmount, failures);
        var codMaxAmount = ReadInteger(json, CodMaxAmountField, 0, MoneyMaxAmount, failures);
        var maxQuantityPerSku = ReadInteger(json, MaxQuantityPerSkuField, 1, MaxQuantityPerSkuUpperBound, failures);
        var maxCartLines = ReadInteger(json, MaxCartLinesField, 1, MaxCartLinesUpperBound, failures);

        var accountNumber = ReadOptionalText(json, SepayAccountNumberField, out var accountNumberProvided, failures);
        var bankCode = ReadOptionalText(json, SepayBankCodeField, out var bankCodeProvided, failures);

        // Kiểm cặp sau khi đọc cả hai trường, để mỗi ô thiếu nhận đúng một lỗi.
        ReportMissingPair(accountNumberProvided, bankCodeProvided, failures);

        return new CommerceSettingsInput(
            freeShippingThreshold,
            codMaxAmount,
            maxQuantityPerSku,
            maxCartLines,
            ValidateAccountNumber(accountNumber, failures),
            ValidateBankCode(bankCode, failures));
    }

    // Chỉ nhận số nguyên JSON trong khoảng đóng. Chuỗi "5" và số lẻ 5.5 bị từ chối, vì client phải gửi đúng kiểu.
    private static int ReadInteger(
        JsonElement json, string field, int minimum, int maximum, List<ValidationFailure> failures)
    {
        if (json.TryGetProperty(field, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
            && number >= minimum
            && number <= maximum)
        {
            return number;
        }

        failures.Add(new ValidationFailure(field, $"{field} phải là số nguyên từ {minimum} đến {maximum}."));
        return minimum;
    }

    // Trả chuỗi đã cắt khoảng trắng, hoặc `null` khi thiếu, `null` hay chuỗi trắng.
    // `provided` là `true` khi client gửi giá trị khác trống, kể cả sai kiểu.
    private static string? ReadOptionalText(
        JsonElement json, string field, out bool provided, List<ValidationFailure> failures)
    {
        // Thiếu trường hoặc `null` đều là chưa nhập.
        if (!json.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            provided = false;
            return null;
        }

        // Số hay đối tượng không được ép thành chuỗi. Chuỗi là kiểu duy nhất hợp lệ.
        if (value.ValueKind != JsonValueKind.String)
        {
            provided = true;
            failures.Add(new ValidationFailure(field, $"{field} phải là chuỗi."));
            return null;
        }

        var text = (value.GetString() ?? string.Empty).Trim();
        provided = text.Length > 0;
        return provided ? text : null;
    }

    // Số tài khoản chỉ gồm chữ số ASCII. Chữ số của hệ chữ khác không khớp với số tài khoản ngân hàng.
    private static string? ValidateAccountNumber(string? accountNumber, List<ValidationFailure> failures)
    {
        if (accountNumber is null)
        {
            return null;
        }

        var isValid = accountNumber.Length >= AccountNumberMinLength
            && accountNumber.Length <= AccountNumberMaxLength
            && accountNumber.All(character => character is >= '0' and <= '9');
        if (!isValid)
        {
            failures.Add(new ValidationFailure(SepayAccountNumberField, AccountNumberMessage));
            return null;
        }

        return accountNumber;
    }

    // Mã ngân hàng lưu dạng chữ hoa để so khớp ổn định. Chỉ nhận chữ cái và chữ số ASCII.
    private static string? ValidateBankCode(string? bankCode, List<ValidationFailure> failures)
    {
        if (bankCode is null)
        {
            return null;
        }

        var isValid = bankCode.Length >= BankCodeMinLength
            && bankCode.Length <= BankCodeMaxLength
            && bankCode.All(character => character is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'));
        if (!isValid)
        {
            failures.Add(new ValidationFailure(SepayBankCodeField, BankCodeMessage));
            return null;
        }

        return bankCode.ToUpperInvariant();
    }

    // Hai trường SePay đi cùng nhau. Thiếu một trường thì lỗi nằm ở ô còn thiếu để UI chỉ đúng ô cần nhập.
    private static void ReportMissingPair(bool accountNumberProvided, bool bankCodeProvided, List<ValidationFailure> failures)
    {
        if (accountNumberProvided && !bankCodeProvided)
        {
            failures.Add(new ValidationFailure(SepayBankCodeField, PairedFieldMessage));
        }

        if (bankCodeProvided && !accountNumberProvided)
        {
            failures.Add(new ValidationFailure(SepayAccountNumberField, PairedFieldMessage));
        }
    }
}
