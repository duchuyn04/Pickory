using System.Text.Json;
using FluentValidation.Results;
using Pickory.Modules.Commerce.Invoices;

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
    public const string ShopField = "shop";
    public const string ShopNameField = "name";
    public const string ShopAddressField = "address";
    public const string ShopPhoneField = "phone";
    public const string ShopTaxCodeField = "taxCode";

    public const int MoneyMaxAmount = 1_000_000_000;
    public const int MaxQuantityPerSkuUpperBound = 99;
    public const int MaxCartLinesUpperBound = 100;
    public const int AccountNumberMinLength = 6;
    public const int AccountNumberMaxLength = 20;
    public const int BankCodeMinLength = 2;
    public const int BankCodeMaxLength = 20;
    public const int ShopNameMaxLength = 120;
    public const int ShopAddressMaxLength = 255;
    public const int ShopPhoneMaxLength = 20;

    private const string BodyMustBeObjectMessage = "Dữ liệu gửi lên phải là một đối tượng JSON.";
    private const string ShopMustBeObjectMessage = "Thông tin shop phải là một đối tượng JSON.";
    private const string UnknownFieldMessage = "Trường này không được hỗ trợ.";
    private const string AccountNumberMessage = "Số tài khoản phải gồm từ 6 đến 20 chữ số.";
    private const string BankCodeMessage = "Mã ngân hàng phải gồm từ 2 đến 20 chữ cái hoặc chữ số.";
    private const string PairedFieldMessage = "Cần nhập cả số tài khoản và mã ngân hàng, hoặc để trống cả hai.";
    private const string ShopNameMessage = "Tên shop không được vượt quá 120 ký tự.";
    private const string ShopAddressMessage = "Địa chỉ shop không được vượt quá 255 ký tự.";
    private const string ShopPhoneMessage = "Số điện thoại shop dài tối đa 20 ký tự và chỉ gồm chữ số, dấu cách, +, ., -.";
    private const string ShopTaxCodeMessage = "Mã số thuế shop phải gồm 10 hoặc 13 chữ số.";
    private const string NulCharacterMessage = "Giá trị không được chứa ký tự NUL.";

    private static readonly string[] KnownFields =
    [
        FreeShippingThresholdField, CodMaxAmountField, MaxQuantityPerSkuField, MaxCartLinesField,
        SepayAccountNumberField, SepayBankCodeField, ShopField,
    ];

    private static readonly string[] KnownShopFields =
    [
        ShopNameField, ShopAddressField, ShopPhoneField, ShopTaxCodeField,
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

        var accountNumber = ReadOptionalText(json, SepayAccountNumberField, SepayAccountNumberField, out var accountNumberProvided, failures);
        var bankCode = ReadOptionalText(json, SepayBankCodeField, SepayBankCodeField, out var bankCodeProvided, failures);

        // Kiểm cặp sau khi đọc cả hai trường, để mỗi ô thiếu nhận đúng một lỗi.
        ReportMissingPair(accountNumberProvided, bankCodeProvided, failures);

        var shop = ReadShop(json, failures);

        return new CommerceSettingsInput(
            freeShippingThreshold,
            codMaxAmount,
            maxQuantityPerSku,
            maxCartLines,
            ValidateAccountNumber(accountNumber, failures),
            ValidateBankCode(bankCode, failures),
            shop);
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
        JsonElement json, string propertyName, string failureKey, out bool provided, List<ValidationFailure> failures)
    {
        // Thiếu trường hoặc `null` đều là chưa nhập.
        if (!json.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            provided = false;
            return null;
        }

        // Số hay đối tượng không được ép thành chuỗi. Chuỗi là kiểu duy nhất hợp lệ.
        if (value.ValueKind != JsonValueKind.String)
        {
            provided = true;
            failures.Add(new ValidationFailure(failureKey, $"{failureKey} phải là chuỗi."));
            return null;
        }

        var text = (value.GetString() ?? string.Empty).Trim();
        // Ký tự mã 0 làm PostgreSQL từ chối cột `text`, nên chặn trước khi lưu.
        if (text.Contains('\0'))
        {
            provided = true;
            failures.Add(new ValidationFailure(failureKey, NulCharacterMessage));
            return null;
        }

        provided = text.Length > 0;
        return provided ? text : null;
    }

    private static CommerceShopInput? ReadShop(JsonElement json, List<ValidationFailure> failures)
    {
        // Thiếu khóa `shop` thì trả `null` để giữ nguyên thông tin shop cũ.
        if (!json.TryGetProperty(ShopField, out var shopValue))
        {
            return null;
        }

        // Khối `shop` khi có mặt phải là đối tượng JSON, kể cả khi giá trị là `null`.
        if (shopValue.ValueKind != JsonValueKind.Object)
        {
            failures.Add(new ValidationFailure(ShopField, ShopMustBeObjectMessage));
            return null;
        }

        // Từ chối trường lạ trong khối `shop` để phát hiện lỗi chính tả ngay khi gửi.
        foreach (var property in shopValue.EnumerateObject())
        {
            if (!KnownShopFields.Contains(property.Name, StringComparer.Ordinal))
            {
                failures.Add(new ValidationFailure($"{ShopField}.{property.Name}", UnknownFieldMessage));
            }
        }

        var name = ReadOptionalText(shopValue, ShopNameField, $"{ShopField}.{ShopNameField}", out _, failures);
        var address = ReadOptionalText(shopValue, ShopAddressField, $"{ShopField}.{ShopAddressField}", out _, failures);
        var phone = ReadOptionalText(shopValue, ShopPhoneField, $"{ShopField}.{ShopPhoneField}", out _, failures);
        var taxCode = ReadOptionalText(shopValue, ShopTaxCodeField, $"{ShopField}.{ShopTaxCodeField}", out _, failures);

        return new CommerceShopInput(
            ValidateShopTextLength(name, ShopNameMaxLength, $"{ShopField}.{ShopNameField}", ShopNameMessage, failures),
            ValidateShopTextLength(address, ShopAddressMaxLength, $"{ShopField}.{ShopAddressField}", ShopAddressMessage, failures),
            ValidateShopPhone(phone, failures),
            ValidateShopTaxCode(taxCode, failures));
    }

    // Giới hạn độ dài tính trên chuỗi đã cắt khoảng trắng ở `ReadOptionalText`.
    private static string? ValidateShopTextLength(
        string? value, int maxLength, string failureKey, string message, List<ValidationFailure> failures)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length > maxLength)
        {
            failures.Add(new ValidationFailure(failureKey, message));
            return null;
        }

        return value;
    }

    // Số điện thoại tối đa 20 ký tự, chỉ nhận chữ số từ 0 đến 9, dấu cách, `+`, `.`, `-`.
    private static string? ValidateShopPhone(string? phone, List<ValidationFailure> failures)
    {
        if (phone is null)
        {
            return null;
        }

        var isValid = phone.Length <= ShopPhoneMaxLength
            && phone.All(character => character is (>= '0' and <= '9') or ' ' or '+' or '.' or '-');
        if (!isValid)
        {
            failures.Add(new ValidationFailure($"{ShopField}.{ShopPhoneField}", ShopPhoneMessage));
            return null;
        }

        return phone;
    }

    // Dùng lại `TaxCodeRule` sau khi đã cắt khoảng trắng để đồng nhất với hóa đơn giá trị gia tăng.
    private static string? ValidateShopTaxCode(string? taxCode, List<ValidationFailure> failures)
    {
        if (taxCode is null)
        {
            return null;
        }

        if (!TaxCodeRule.IsValid(taxCode))
        {
            failures.Add(new ValidationFailure($"{ShopField}.{ShopTaxCodeField}", ShopTaxCodeMessage));
            return null;
        }

        return taxCode;
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
