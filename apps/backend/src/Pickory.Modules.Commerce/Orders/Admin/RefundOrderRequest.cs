using System.Text.Json;
using FluentValidation.Results;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Đọc body của `PUT /api/admin/orders/{code}/refund`, chỉ có một trường `transactionRef`.
// Binder mặc định nhận kiểu sai và bỏ qua trường lạ, nên lớp này đọc JSON thủ công theo luật chặt.
internal static class RefundOrderRequest
{
    public const string TransactionRefField = "transactionRef";
    public const int TransactionRefMaxLength = 100;

    private const string UnknownFieldMessage = "Trường này không được hỗ trợ.";
    private const string TransactionRefRequiredMessage = "Mã giao dịch hoàn tiền là bắt buộc.";
    private const string TransactionRefTooLongMessage = "Mã giao dịch hoàn tiền không được dài quá 100 ký tự.";

    // Thêm mọi lỗi vào `failures` rồi trả mã đã cắt khoảng trắng. Nơi gọi phải dừng khi `failures` không rỗng.
    public static string Read(JsonElement? body, List<ValidationFailure> failures)
    {
        // Thiếu body hoặc body không phải đối tượng thì không có mã để đọc.
        if (body is not { ValueKind: JsonValueKind.Object } json)
        {
            failures.Add(new ValidationFailure(TransactionRefField, TransactionRefRequiredMessage));
            return string.Empty;
        }

        // Trường lạ bị từ chối, để client gõ sai tên trường nhận lỗi thay vì bị bỏ qua âm thầm.
        foreach (var property in json.EnumerateObject())
        {
            if (!string.Equals(property.Name, TransactionRefField, StringComparison.Ordinal))
            {
                failures.Add(new ValidationFailure(property.Name, UnknownFieldMessage));
            }
        }

        // Chỉ nhận chuỗi. Số, đối tượng hay `null` đều coi như thiếu mã.
        if (!json.TryGetProperty(TransactionRefField, out var element) || element.ValueKind != JsonValueKind.String)
        {
            failures.Add(new ValidationFailure(TransactionRefField, TransactionRefRequiredMessage));
            return string.Empty;
        }

        // Cắt khoảng trắng hai đầu trước khi kiểm, để mã chỉ có khoảng trắng bị coi là trống.
        var transactionRef = element.GetString()!.Trim();
        if (transactionRef.Length == 0)
        {
            failures.Add(new ValidationFailure(TransactionRefField, TransactionRefRequiredMessage));
            return string.Empty;
        }

        // Giới hạn khớp độ dài cột `refundTransactionRef`, nên mã quá dài không chạm tới DB.
        if (transactionRef.Length > TransactionRefMaxLength)
        {
            failures.Add(new ValidationFailure(TransactionRefField, TransactionRefTooLongMessage));
            return string.Empty;
        }

        return transactionRef;
    }
}
