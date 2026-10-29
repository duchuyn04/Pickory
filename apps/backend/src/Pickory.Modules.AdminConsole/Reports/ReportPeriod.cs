namespace Pickory.Modules.AdminConsole.Reports;

// Độ chia của một báo cáo. Mỗi giá trị tạo ra một loại nhóm thời gian khác nhau.
internal enum ReportGranularity
{
    Day,
    Week,
    Month,
}

// Một nhóm thời gian của báo cáo, tính theo ngày giờ Việt Nam và gồm cả hai đầu `From`, `To`.
// Truy vấn lọc `>= StartUtc` và `< EndExclusiveUtc`.
internal sealed record ReportBucket(
    string Key,
    DateOnly From,
    DateOnly To,
    DateTime StartUtc,
    DateTime EndExclusiveUtc);

// Khoảng thời gian đã kiểm của một báo cáo, cùng danh sách nhóm sắp tăng dần và liền nhau.
internal sealed record ReportPeriod(
    DateOnly From,
    DateOnly To,
    DateTime StartUtc,
    DateTime EndExclusiveUtc,
    ReportGranularity Granularity,
    IReadOnlyList<ReportBucket> Buckets);
