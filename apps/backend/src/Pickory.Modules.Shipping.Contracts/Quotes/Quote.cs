namespace Pickory.Modules.Shipping.Contracts.Quotes;

// Kết quả báo phí, `Fee` tính bằng đồng. `ExpectedDeliveryDate` là `null` khi GHN không trả được ngày giao hoặc `Source` khác `Ghn`.
public sealed record Quote(int Fee, int ServiceTypeId, DateOnly? ExpectedDeliveryDate, QuoteSource Source);
