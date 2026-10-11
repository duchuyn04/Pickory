using System.Text.Json;

namespace Pickory.Modules.Commerce.Entities;

// Giao dịch tiền vào do nhà cung cấp báo, lưu thô trước khi khớp đơn.
// `ProviderTxId` là khóa chống ghi nhận trùng, và `MatchStatus` nhận `matched`, `unmatched` hoặc `ignored`.
public sealed class PaymentTransaction
{
    public string Id { get; set; } = null!;
    public string Provider { get; set; } = string.Empty;
    public string ProviderTxId { get; set; } = string.Empty;
    public int Amount { get; set; }
    public string Content { get; set; } = string.Empty;
    public JsonDocument Raw { get; set; } = null!;
    public string? OrderId { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }

    public Order? Order { get; set; }
}
