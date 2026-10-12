using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;

namespace Pickory.Modules.Commerce.Checkout;

// Phương thức thanh toán mà checkout nhận. Giá trị là chuỗi cố định trong body.
public static class CheckoutPaymentMethods
{
    public const string Cod = "cod";
    public const string Sepay = "sepay";
}

// Địa chỉ nhận hàng khách gửi lên. Tên tỉnh và phường không có ở đây vì server tra từ danh mục.
public sealed record CheckoutAddressInput(string ProvinceCode, string WardCode, string Line);

// Giá trị đã kiểm của body `POST /api/checkout/previews`.
public sealed record CheckoutPreviewInput(CheckoutAddressInput Address, string PaymentMethod);

// Đọc body của route preview theo luật chặt. Binder mặc định nhận trường lạ và kiểu sai, nên lớp này đọc JSON thủ công.
public static class CheckoutPreviewRequests
{
    public const string AddressField = "address";
    public const string ProvinceCodeField = "address.provinceCode";
    public const string WardCodeField = "address.wardCode";
    public const string LineField = "address.line";
    public const string PaymentMethodField = "paymentMethod";

    public const int LineMaxLength = 200;

    private const string ProvinceCodeProperty = "provinceCode";
    private const string WardCodeProperty = "wardCode";
    private const string LineProperty = "line";

    private const string BodyMustBeObjectMessage = "Dữ liệu gửi lên phải là một đối tượng JSON.";
    private const string UnknownFieldMessage = "Trường này không được hỗ trợ.";
    private const string AddressMessage = "Cần nhập địa chỉ nhận hàng.";
    private const string ProvinceCodeMessage = "Cần chọn tỉnh hoặc thành phố.";
    private const string WardCodeMessage = "Cần chọn phường hoặc xã.";
    private const string LineMessage = "Địa chỉ cụ thể là chuỗi không rỗng, tối đa 200 ký tự.";
    private const string PaymentMethodMessage = "Phương thức thanh toán phải là cod hoặc sepay.";

    private static readonly string[] KnownBodyFields = [AddressField, PaymentMethodField];
    private static readonly string[] KnownAddressFields = [ProvinceCodeProperty, WardCodeProperty, LineProperty];

    // Ném `ValidationException` gom mọi lỗi của body. Route gọi hàm này trước khi đọc DB hay gọi shipping.
    public static CheckoutPreviewInput Read(JsonElement? body)
    {
        var failures = new List<ValidationFailure>();

        // Body không phải đối tượng JSON thì không có trường nào để đọc tiếp. Lỗi gắn với cả body.
        if (body is not { ValueKind: JsonValueKind.Object } json)
        {
            failures.Add(new ValidationFailure(string.Empty, BodyMustBeObjectMessage));
            throw new ValidationException(failures);
        }

        // Trường lạ bị từ chối. Body không có dòng hàng, nên client gửi `items` hay `total` là sai hợp đồng.
        ReportUnknownFields(json, KnownBodyFields, prefix: string.Empty, failures);

        var address = ReadAddress(json, failures);
        var paymentMethod = ReadPaymentMethod(json, failures);

        // Gom mọi lỗi vào một phản hồi, để UI gán lỗi vào đúng ô.
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return new CheckoutPreviewInput(address, paymentMethod);
    }

    private static CheckoutAddressInput ReadAddress(JsonElement json, List<ValidationFailure> failures)
    {
        var empty = new CheckoutAddressInput(string.Empty, string.Empty, string.Empty);

        // Địa chỉ phải là đối tượng. Chuỗi hay `null` không có mã tỉnh và phường để tra danh mục.
        if (!json.TryGetProperty(AddressField, out var address) || address.ValueKind != JsonValueKind.Object)
        {
            failures.Add(new ValidationFailure(AddressField, AddressMessage));
            return empty;
        }

        ReportUnknownFields(address, KnownAddressFields, prefix: AddressField + ".", failures);

        var provinceCode = ReadCode(address, ProvinceCodeProperty, ProvinceCodeField, ProvinceCodeMessage, failures);
        var wardCode = ReadCode(address, WardCodeProperty, WardCodeField, WardCodeMessage, failures);
        var line = ReadLine(address, failures);
        return new CheckoutAddressInput(provinceCode, wardCode, line);
    }

    private static string ReadCode(
        JsonElement address, string property, string field, string message, List<ValidationFailure> failures)
    {
        // Mã là chuỗi không rỗng. Số JSON bị từ chối, vì danh mục lưu mã dạng chuỗi và client phải gửi đúng kiểu.
        if (address.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString()))
        {
            return value.GetString()!.Trim();
        }

        failures.Add(new ValidationFailure(field, message));
        return string.Empty;
    }

    private static string ReadLine(JsonElement address, List<ValidationFailure> failures)
    {
        if (address.TryGetProperty(LineProperty, out var value) && value.ValueKind == JsonValueKind.String)
        {
            // Cắt khoảng trắng trước khi kiểm độ dài, vì giá trị gửi tới GHN là chuỗi đã cắt.
            var line = value.GetString()!.Trim();
            if (line.Length > 0 && line.Length <= LineMaxLength)
            {
                return line;
            }
        }

        failures.Add(new ValidationFailure(LineField, LineMessage));
        return string.Empty;
    }

    private static string ReadPaymentMethod(JsonElement json, List<ValidationFailure> failures)
    {
        // So khớp đúng chữ thường. Giá trị `COD` hay `momo` không đại diện cho phương thức nào của shop.
        if (json.TryGetProperty(PaymentMethodField, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var method = value.GetString();
            if (method is CheckoutPaymentMethods.Cod or CheckoutPaymentMethods.Sepay)
            {
                return method;
            }
        }

        failures.Add(new ValidationFailure(PaymentMethodField, PaymentMethodMessage));
        return string.Empty;
    }

    private static void ReportUnknownFields(
        JsonElement json, string[] knownFields, string prefix, List<ValidationFailure> failures)
    {
        foreach (var property in json.EnumerateObject())
        {
            if (!knownFields.Contains(property.Name, StringComparer.Ordinal))
            {
                failures.Add(new ValidationFailure(prefix + property.Name, UnknownFieldMessage));
            }
        }
    }
}
