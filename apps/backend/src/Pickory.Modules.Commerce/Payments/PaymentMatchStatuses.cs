namespace Pickory.Modules.Commerce.Payments;

// Ba giá trị của `payment_transactions.matchStatus`. Giá trị đã nằm trong dữ liệu cũ của DB, nên không được đổi.
internal static class PaymentMatchStatuses
{
    // Giao dịch đã gắn vào một đơn.
    public const string Matched = "matched";

    // Giao dịch mới nhận, chưa gắn đơn và chưa bị bỏ qua.
    public const string Unmatched = "unmatched";

    // Giao dịch không phải tiền khách chuyển vào, hoặc nhân viên đã đánh dấu bỏ qua.
    public const string Ignored = "ignored";
}
