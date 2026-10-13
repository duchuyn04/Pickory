using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Orders;

namespace Pickory.Modules.Commerce.Addresses;

// Giá trị đã kiểm của body `POST /api/me/addresses`. `Phone` đã chuẩn hóa về dạng `0xxxxxxxxx`.
internal sealed record SavedAddressInput(
    string Label, string Name, string Phone, CheckoutAddressInput Address, bool IsDefault);

// Giá trị đã kiểm của body `PATCH /api/me/addresses/{id}`. Trường `null` nghĩa là client không gửi trường đó.
internal sealed record SavedAddressPatch(
    string? Label, string? Name, string? Phone, CheckoutAddressInput? Address, bool? IsDefault);

// Đọc body của hai route ghi sổ địa chỉ theo luật chặt. Binder mặc định nhận trường lạ và kiểu sai, nên lớp này đọc JSON thủ công.
internal static class SavedAddressRequests
{
    public const string LabelField = "label";
    public const string NameField = "name";
    public const string PhoneField = "phone";
    public const string AddressField = CheckoutPreviewRequests.AddressField;
    public const string IsDefaultField = "isDefault";

    public const int LabelMaxLength = 50;

    private const string BodyMustBeObjectMessage = "Dữ liệu gửi lên phải là một đối tượng JSON.";
    private const string UnknownFieldMessage = "Trường này không được hỗ trợ.";
    private const string LabelMessage = "Nhãn địa chỉ là chuỗi không rỗng, tối đa 50 ký tự.";
    private const string IsDefaultMessage = "Trường này phải là true hoặc false.";
    private const string EmptyPatchMessage = "Cần gửi ít nhất một trường cần sửa.";

    private static readonly string[] KnownFields = [LabelField, NameField, PhoneField, AddressField, IsDefaultField];

    // Ném `ValidationException` gom mọi lỗi của body. Route gọi hàm này trước khi đọc DB hay tra danh mục.
    public static SavedAddressInput ReadCreate(JsonElement? body)
    {
        var failures = new List<ValidationFailure>();
        var json = RequireObject(body, failures);

        // Cả bốn trường bắt buộc, vì địa chỉ thiếu một trường thì checkout không dùng được để giao hàng.
        var label = ReadLabel(json, failures);
        var name = PlaceOrderRequests.ReadName(json, NameField, failures);
        var phone = PlaceOrderRequests.ReadPhone(json, PhoneField, failures);

        // Địa chỉ dùng đúng hàm của checkout, để sổ và đơn hàng không lệch luật.
        var address = CheckoutPreviewRequests.ReadAddress(json, failures);
        var isDefault = ReadIsDefault(json, failures) ?? false;

        // Gom mọi lỗi vào một phản hồi, để UI gán lỗi vào đúng ô.
        failures.AddRange(FindUnknownFields(json));
        ThrowIfAny(failures);
        return new SavedAddressInput(label, name, phone, address, isDefault);
    }

    // Ném `ValidationException` gom mọi lỗi của body. Chỉ trường có trong body mới được kiểm và đổi.
    public static SavedAddressPatch ReadPatch(JsonElement? body)
    {
        var failures = new List<ValidationFailure>();
        var json = RequireObject(body, failures);

        // Trường có mặt thì phải hợp lệ như lúc tạo. Trường `null` bị từ chối chứ không bị coi là đã bỏ qua.
        var label = json.TryGetProperty(LabelField, out _) ? ReadLabel(json, failures) : null;
        var name = json.TryGetProperty(NameField, out _) ? PlaceOrderRequests.ReadName(json, NameField, failures) : null;
        var phone = json.TryGetProperty(PhoneField, out _) ? PlaceOrderRequests.ReadPhone(json, PhoneField, failures) : null;
        var address = json.TryGetProperty(AddressField, out _) ? CheckoutPreviewRequests.ReadAddress(json, failures) : null;
        var isDefault = json.TryGetProperty(IsDefaultField, out _) ? ReadIsDefault(json, failures) : null;

        failures.AddRange(FindUnknownFields(json));

        // Body không có trường nào để đổi thì request không có ý nghĩa. Từ chối thay vì trả 200 mà không đổi gì.
        var hasKnownField = json.EnumerateObject().Any(property => KnownFields.Contains(property.Name, StringComparer.Ordinal));
        if (!hasKnownField && failures.Count == 0)
        {
            failures.Add(new ValidationFailure(string.Empty, EmptyPatchMessage));
        }

        ThrowIfAny(failures);
        return new SavedAddressPatch(label, name, phone, address, isDefault);
    }

    private static JsonElement RequireObject(JsonElement? body, List<ValidationFailure> failures)
    {
        // Body không phải đối tượng JSON thì không có trường nào để đọc tiếp. Lỗi gắn với cả body.
        if (body is { ValueKind: JsonValueKind.Object } json)
        {
            return json;
        }

        failures.Add(new ValidationFailure(string.Empty, BodyMustBeObjectMessage));
        throw new ValidationException(failures);
    }

    private static string ReadLabel(JsonElement json, List<ValidationFailure> failures)
    {
        if (json.TryGetProperty(LabelField, out var value) && value.ValueKind == JsonValueKind.String)
        {
            // Cắt khoảng trắng trước khi kiểm độ dài. Nhãn toàn khoảng trắng coi như không có nhãn.
            var label = value.GetString()!.Trim();
            if (label.Length > 0 && label.Length <= LabelMaxLength)
            {
                return label;
            }
        }

        failures.Add(new ValidationFailure(LabelField, LabelMessage));
        return string.Empty;
    }

    private static bool? ReadIsDefault(JsonElement json, List<ValidationFailure> failures)
    {
        // Thiếu trường nghĩa là khách không xin đổi cờ mặc định.
        if (!json.TryGetProperty(IsDefaultField, out var value))
        {
            return null;
        }

        // Chỉ nhận `true` hoặc `false` của JSON. Chuỗi như `"yes"` không phải ý định rõ ràng của khách.
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        failures.Add(new ValidationFailure(IsDefaultField, IsDefaultMessage));
        return null;
    }

    // Trường lạ bị từ chối, để client gõ sai tên trường nhận lỗi thay vì bị bỏ qua âm thầm.
    private static IEnumerable<ValidationFailure> FindUnknownFields(JsonElement json)
    {
        return json.EnumerateObject()
            .Where(property => !KnownFields.Contains(property.Name, StringComparer.Ordinal))
            .Select(property => new ValidationFailure(property.Name, UnknownFieldMessage));
    }

    private static void ThrowIfAny(List<ValidationFailure> failures)
    {
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }
}
