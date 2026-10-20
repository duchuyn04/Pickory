namespace Pickory.Modules.ActivityTracking.Entities;

// Số lượt của một chỉ số trong một ngày. Bảng chỉ lưu số gộp, nên không có cột nào xác định khách.
internal sealed class DailyCounter
{
    public DateOnly Date { get; set; }
    public string Metric { get; set; } = string.Empty;
    public long Value { get; set; }
}
