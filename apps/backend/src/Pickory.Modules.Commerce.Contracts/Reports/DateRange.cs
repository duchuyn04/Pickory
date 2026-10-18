namespace Pickory.Modules.Commerce.Contracts.Reports;

// Khoảng ngày lịch giờ Việt Nam (UTC+7), gồm cả hai đầu.
public sealed record DateRange(DateOnly From, DateOnly To);
