using System.Text.Json;
using FluentValidation.Results;
using Pickory.Modules.Shipping.Contracts.Parcels;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Đọc body tùy chọn của `POST /api/admin/orders/{code}/shipment`, chỉ có đối tượng `parcel`.
// Binder mặc định nhận kiểu sai và bỏ qua trường lạ, nên lớp này đọc JSON thủ công theo luật chặt.
internal static class CreateShipmentBody
{
    public const string ParcelField = "parcel";

    private const string UnknownFieldMessage = "Trường này không được hỗ trợ.";
    private const string BodyMustBeObjectMessage = "Nội dung yêu cầu phải là một đối tượng JSON.";
    private const string ParcelMustBeObjectMessage = "Kiện hàng phải là một đối tượng có đủ cân nặng và ba kích thước.";
    private const string PositiveIntegerMessage = "Giá trị phải là số nguyên dương.";

    private static readonly string[] ParcelProperties = ["weightGrams", "lengthCm", "widthCm", "heightCm"];

    // Thêm mọi lỗi vào `failures`. Trả `null` khi không có `parcel`, và nơi gọi tự dựng kiện từ dòng hàng.
    public static Parcel? Read(JsonElement? body, List<ValidationFailure> failures)
    {
        // Thiếu body là hợp lệ. Binder đưa body trống về phần tử `Undefined`, và khi đó dùng kiện dựng từ dòng hàng.
        if (body is null or { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null })
        {
            return null;
        }

        if (body.Value.ValueKind != JsonValueKind.Object)
        {
            failures.Add(new ValidationFailure(ParcelField, BodyMustBeObjectMessage));
            return null;
        }

        var json = body.Value;

        // Trường lạ bị từ chối, để client gõ sai tên trường nhận lỗi thay vì bị bỏ qua âm thầm.
        foreach (var property in json.EnumerateObject())
        {
            if (!string.Equals(property.Name, ParcelField, StringComparison.Ordinal))
            {
                failures.Add(new ValidationFailure(property.Name, UnknownFieldMessage));
            }
        }

        if (!json.TryGetProperty(ParcelField, out var parcelElement))
        {
            return null;
        }

        // `null` cũng bị từ chối. Muốn dùng kiện mặc định thì bỏ hẳn trường `parcel`.
        if (parcelElement.ValueKind != JsonValueKind.Object)
        {
            failures.Add(new ValidationFailure(ParcelField, ParcelMustBeObjectMessage));
            return null;
        }

        return ReadParcel(parcelElement, failures);
    }

    private static Parcel? ReadParcel(JsonElement parcelElement, List<ValidationFailure> failures)
    {
        var failureCountBefore = failures.Count;

        // Trường lạ trong `parcel` bị từ chối vì cùng lý do với trường lạ ở mức ngoài.
        foreach (var property in parcelElement.EnumerateObject())
        {
            if (!ParcelProperties.Contains(property.Name, StringComparer.Ordinal))
            {
                failures.Add(new ValidationFailure($"{ParcelField}.{property.Name}", UnknownFieldMessage));
            }
        }

        var weightGrams = ReadPositiveInteger(parcelElement, ParcelProperties[0], failures);
        var lengthCm = ReadPositiveInteger(parcelElement, ParcelProperties[1], failures);
        var widthCm = ReadPositiveInteger(parcelElement, ParcelProperties[2], failures);
        var heightCm = ReadPositiveInteger(parcelElement, ParcelProperties[3], failures);

        // Có lỗi thì không dựng kiện, để nơi gọi dừng ở bước kiểm trước khi chạm đơn.
        if (failures.Count > failureCountBefore)
        {
            return null;
        }

        return new Parcel(weightGrams, lengthCm, widthCm, heightCm);
    }

    private static int ReadPositiveInteger(JsonElement parcelElement, string propertyName, List<ValidationFailure> failures)
    {
        var field = $"{ParcelField}.{propertyName}";

        // Chỉ nhận số JSON, nên chuỗi như "500" bị từ chối.
        // `TryGetInt32` từ chối số thập phân và số vượt `int`, nên giá trị đi tiếp luôn nằm trong kiểu của `Parcel`.
        if (!parcelElement.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var number)
            || number <= 0)
        {
            failures.Add(new ValidationFailure(field, PositiveIntegerMessage));
            return 0;
        }

        return number;
    }
}
