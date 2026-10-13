namespace Pickory.Modules.Commerce.Payments.Admin;

// Tóm tắt đơn gắn với giao dịch. Chỉ gồm trường cần để nhân viên đối soát, không có thông tin liên hệ của khách.
public sealed record AdminPaymentOrderSummaryResponse(
    string Code,
    string Status,
    string PaymentStatus,
    int Total,
    IReadOnlyList<string> Flags);

// Một giao dịch SePay cho nhân viên, không có cột `raw` vì payload gốc chứa dữ liệu ngân hàng.
// `Order` là `null` khi giao dịch chưa gắn đơn.
public sealed record AdminPaymentTransactionResponse(
    string Id,
    string ProviderTxId,
    int Amount,
    string Content,
    string MatchStatus,
    DateTime ReceivedAt,
    AdminPaymentOrderSummaryResponse? Order);
