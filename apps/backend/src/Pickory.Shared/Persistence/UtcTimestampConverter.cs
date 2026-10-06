using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Pickory.Shared.Persistence;

public sealed class UtcTimestampConverter : ValueConverter<DateTime, DateTime>
{
    // Gắn UTC (Coordinated Universal Time) khi đọc vì PostgreSQL không lưu múi giờ trong cột.
    public UtcTimestampConverter()
        : base(value => ToDatabase(value), value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
    {
    }

    private static DateTime ToDatabase(DateTime value)
    {
        // Chuyển giờ địa phương về UTC (Coordinated Universal Time) trước khi bỏ múi giờ. Giá trị không xác định đã theo UTC.
        var utcValue = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;

        // Npgsql yêu cầu `Unspecified` cho cột không múi giờ. Giữ nguyên thời điểm UTC khi ghi.
        return DateTime.SpecifyKind(utcValue, DateTimeKind.Unspecified);
    }
}
