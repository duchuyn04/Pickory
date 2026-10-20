namespace Pickory.Modules.ActivityTracking.Contracts.Metrics;

// Khoảng ngày lịch gồm cả hai đầu. Record không tự kiểm luật khoảng ngày, nên API kiểm trước khi gọi.
public sealed record DateRange(DateOnly From, DateOnly To);
