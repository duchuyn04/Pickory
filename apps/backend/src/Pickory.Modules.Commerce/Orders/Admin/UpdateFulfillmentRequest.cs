using System.Text.Json;
using FluentValidation.Results;
using Pickory.Modules.Commerce.Contracts.Orders;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Kết quả đọc body: trạng thái đích và lý do đã cắt khoảng trắng.
internal readonly record struct FulfillmentUpdate(OrderStatus ToStatus, string Reason);

// Đọc body của `PUT /api/admin/orders/{code}/fulfillment`, có đúng hai trường `toStatus` và `reason`.
// Binder mặc định nhận kiểu sai và bỏ qua trường lạ, nên lớp này đọc JSON thủ công theo luật chặt.
internal static class UpdateFulfillmentRequest
{
    public const string ToStatusField = "toStatus";
    public const string ReasonField = "reason";
    public const int ReasonMaxLength = 500;

    private const string UnknownFieldMessage = "Trường này không được hỗ trợ.";
    private const string ToStatusInvalidMessage = "Chọn một trong các trạng thái: confirmed, shipping, delivered, returned.";
    private const string ReasonInvalidMessage = "Nhập lý do từ 1 đến 500 ký tự.";

    // Bốn trạng thái mà hợp đồng cho phép nhân viên đặt. Trạng thái khác của `OrderStatus` bị từ chối.
    private static readonly OrderStatus[] AcceptedStatuses =
    [
        OrderStatus.Confirmed,
        OrderStatus.Shipping,
        OrderStatus.Delivered,
        OrderStatus.Returned,
    ];

    // Thêm mọi lỗi vào `failures`, mỗi trường lỗi một dòng. Nơi gọi phải dừng khi `failures` không rỗng.
    public static FulfillmentUpdate Read(JsonElement? body, List<ValidationFailure> failures)
    {
        var emptyResult = new FulfillmentUpdate(default, string.Empty);

        // Thiếu body hoặc body không phải đối tượng thì cả hai trường đều thiếu.
        if (body is not { ValueKind: JsonValueKind.Object } json)
        {
            failures.Add(new ValidationFailure(ToStatusField, ToStatusInvalidMessage));
            failures.Add(new ValidationFailure(ReasonField, ReasonInvalidMessage));
            return emptyResult;
        }

        // Trường lạ bị từ chối, để client gõ sai tên trường nhận lỗi thay vì bị bỏ qua âm thầm.
        foreach (var property in json.EnumerateObject())
        {
            var isKnownField = string.Equals(property.Name, ToStatusField, StringComparison.Ordinal)
                || string.Equals(property.Name, ReasonField, StringComparison.Ordinal);
            if (!isKnownField)
            {
                failures.Add(new ValidationFailure(property.Name, UnknownFieldMessage));
            }
        }

        var toStatus = ReadToStatus(json, failures);
        var reason = ReadReason(json, failures);
        return new FulfillmentUpdate(toStatus, reason);
    }

    private static OrderStatus ReadToStatus(JsonElement json, List<ValidationFailure> failures)
    {
        // Chỉ nhận chuỗi. Số, đối tượng hay `null` đều coi như thiếu trạng thái đích.
        if (!json.TryGetProperty(ToStatusField, out var element) || element.ValueKind != JsonValueKind.String)
        {
            failures.Add(new ValidationFailure(ToStatusField, ToStatusInvalidMessage));
            return default;
        }

        // So khớp chính xác với nhãn. Chặn mọi trạng thái ngoài bốn giá trị của hợp đồng.
        var isKnownLabel = OrderLabels.TryParse<OrderStatus>(element.GetString(), out var status);
        if (!isKnownLabel || !AcceptedStatuses.Contains(status))
        {
            failures.Add(new ValidationFailure(ToStatusField, ToStatusInvalidMessage));
            return default;
        }

        return status;
    }

    private static string ReadReason(JsonElement json, List<ValidationFailure> failures)
    {
        // Chỉ nhận chuỗi. Số, đối tượng hay `null` đều coi như thiếu lý do.
        if (!json.TryGetProperty(ReasonField, out var element) || element.ValueKind != JsonValueKind.String)
        {
            failures.Add(new ValidationFailure(ReasonField, ReasonInvalidMessage));
            return string.Empty;
        }

        // Cắt khoảng trắng hai đầu trước khi kiểm độ dài, để lý do chỉ có khoảng trắng bị coi là trống.
        var reason = element.GetString()!.Trim();
        if (reason.Length == 0 || reason.Length > ReasonMaxLength)
        {
            failures.Add(new ValidationFailure(ReasonField, ReasonInvalidMessage));
            return string.Empty;
        }

        return reason;
    }
}
