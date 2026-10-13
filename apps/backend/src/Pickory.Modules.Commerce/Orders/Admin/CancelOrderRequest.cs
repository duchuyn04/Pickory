using System.Text.Json;
using FluentValidation.Results;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Đọc body của `POST /api/admin/orders/{code}/cancellation`, chỉ có một trường `reason`.
// Binder mặc định nhận kiểu sai và bỏ qua trường lạ, nên lớp này đọc JSON thủ công theo luật chặt.
internal static class CancelOrderRequest
{
    public const string ReasonField = "reason";
    public const int ReasonMaxLength = 500;

    private const string UnknownFieldMessage = "Trường này không được hỗ trợ.";
    private const string ReasonRequiredMessage = "Lý do hủy đơn là bắt buộc.";
    private const string ReasonTooLongMessage = "Lý do hủy đơn không được dài quá 500 ký tự.";

    // Thêm mọi lỗi vào `failures` rồi trả lý do đã cắt khoảng trắng. Nơi gọi phải dừng khi `failures` không rỗng.
    public static string Read(JsonElement? body, List<ValidationFailure> failures)
    {
        // Thiếu body hoặc body không phải đối tượng thì không có lý do để đọc.
        if (body is not { ValueKind: JsonValueKind.Object } json)
        {
            failures.Add(new ValidationFailure(ReasonField, ReasonRequiredMessage));
            return string.Empty;
        }

        // Trường lạ bị từ chối, để client gõ sai tên trường nhận lỗi thay vì bị bỏ qua âm thầm.
        foreach (var property in json.EnumerateObject())
        {
            if (!string.Equals(property.Name, ReasonField, StringComparison.Ordinal))
            {
                failures.Add(new ValidationFailure(property.Name, UnknownFieldMessage));
            }
        }

        // Chỉ nhận chuỗi. Số, đối tượng hay `null` đều coi như thiếu lý do.
        if (!json.TryGetProperty(ReasonField, out var reasonElement) || reasonElement.ValueKind != JsonValueKind.String)
        {
            failures.Add(new ValidationFailure(ReasonField, ReasonRequiredMessage));
            return string.Empty;
        }

        // Cắt khoảng trắng hai đầu trước khi kiểm, để lý do chỉ có khoảng trắng bị coi là trống.
        var reason = reasonElement.GetString()!.Trim();
        if (reason.Length == 0)
        {
            failures.Add(new ValidationFailure(ReasonField, ReasonRequiredMessage));
            return string.Empty;
        }

        if (reason.Length > ReasonMaxLength)
        {
            failures.Add(new ValidationFailure(ReasonField, ReasonTooLongMessage));
            return string.Empty;
        }

        return reason;
    }
}
