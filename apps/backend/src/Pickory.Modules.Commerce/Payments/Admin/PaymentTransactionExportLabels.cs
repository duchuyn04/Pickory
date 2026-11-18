namespace Pickory.Modules.Commerce.Payments.Admin;

// Nhãn tiếng Việt của trạng thái khớp trong file xuất giao dịch. Nhãn đặt ở một nơi để mọi nơi gọi `ForMatchStatus` dùng cùng một bảng.
internal static class PaymentTransactionExportLabels
{
    private static readonly IReadOnlyDictionary<string, string> MatchStatusLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [PaymentMatchStatuses.Unmatched] = "Chưa khớp",
        [PaymentMatchStatuses.Matched] = "Đã khớp",
        [PaymentMatchStatuses.Ignored] = "Đã bỏ qua",
    };

    // Giá trị lạ trong DB giữ nguyên chuỗi. Nhân viên không mất thông tin và file vẫn xuất được.
    public static string ForMatchStatus(string matchStatus) =>
        MatchStatusLabels.TryGetValue(matchStatus, out var label) ? label : matchStatus;
}
