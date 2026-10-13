using System.Text.Json;
using FluentValidation.Results;

namespace Pickory.Modules.Commerce.Payments.Admin;

// Body của `PATCH /api/admin/payments/transactions/{id}` có đúng một trong hai dạng: `OrderCode` gắn đơn, hoặc `Ignore` bỏ qua giao dịch.
// Binder mặc định nhận kiểu sai và bỏ qua trường lạ, nên lớp này đọc JSON thủ công theo luật chặt.
internal sealed record UpdatePaymentTransactionRequest(string? OrderCode, bool Ignore)
{
    public const string OrderCodeField = "orderCode";
    public const string MatchStatusField = "matchStatus";
    public const string IgnoredValue = "ignored";

    private const string UnknownFieldMessage = "Trường này không được hỗ trợ.";
    private const string EitherFieldRequiredMessage = "Chỉ gửi một trong `orderCode` hoặc `matchStatus`.";
    private const string OrderCodeInvalidMessage = "Mã đơn phải là chuỗi không rỗng.";
    private const string MatchStatusInvalidMessage = "`matchStatus` chỉ nhận giá trị `ignored`.";

    // Thêm mọi lỗi vào `failures`. Nơi gọi phải dừng khi `failures` không rỗng.
    public static UpdatePaymentTransactionRequest Read(JsonElement? body, List<ValidationFailure> failures)
    {
        var empty = new UpdatePaymentTransactionRequest(null, false);

        // Thiếu body hoặc body không phải đối tượng thì không có thao tác nào để đọc.
        if (body is not { ValueKind: JsonValueKind.Object } json)
        {
            failures.Add(new ValidationFailure(OrderCodeField, EitherFieldRequiredMessage));
            return empty;
        }

        // Trường lạ bị từ chối, để client gõ sai tên trường nhận lỗi thay vì bị bỏ qua âm thầm.
        foreach (var property in json.EnumerateObject())
        {
            if (property.Name != OrderCodeField && property.Name != MatchStatusField)
            {
                failures.Add(new ValidationFailure(property.Name, UnknownFieldMessage));
            }
        }

        var hasOrderCode = json.TryGetProperty(OrderCodeField, out var orderCodeElement);
        var hasMatchStatus = json.TryGetProperty(MatchStatusField, out var matchStatusElement);

        // Hai thao tác loại trừ nhau, và phải có một thao tác. Cả hai hoặc không trường nào đều không rõ ý nhân viên.
        if (hasOrderCode == hasMatchStatus)
        {
            failures.Add(new ValidationFailure(OrderCodeField, EitherFieldRequiredMessage));
            return empty;
        }

        if (hasOrderCode)
        {
            // Chỉ nhận chuỗi. Số, đối tượng hay `null` đều coi như mã đơn không hợp lệ.
            if (orderCodeElement.ValueKind != JsonValueKind.String)
            {
                failures.Add(new ValidationFailure(OrderCodeField, OrderCodeInvalidMessage));
                return empty;
            }

            // Mã viết hoa như mã lưu trong DB và `TransferContentParser`. Cắt khoảng trắng hai đầu trước khi kiểm rỗng.
            var orderCode = orderCodeElement.GetString()!.Trim().ToUpperInvariant();
            if (orderCode.Length == 0)
            {
                failures.Add(new ValidationFailure(OrderCodeField, OrderCodeInvalidMessage));
                return empty;
            }

            return new UpdatePaymentTransactionRequest(orderCode, false);
        }

        // Chỉ `ignored` là trạng thái nhân viên được đặt tay. `matched` chỉ có qua việc gắn đơn.
        if (matchStatusElement.ValueKind != JsonValueKind.String
            || !string.Equals(matchStatusElement.GetString(), IgnoredValue, StringComparison.Ordinal))
        {
            failures.Add(new ValidationFailure(MatchStatusField, MatchStatusInvalidMessage));
            return empty;
        }

        return new UpdatePaymentTransactionRequest(null, true);
    }
}
