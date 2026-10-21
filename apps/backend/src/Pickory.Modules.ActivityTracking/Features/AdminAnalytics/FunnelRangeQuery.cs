using System.Globalization;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Pickory.Modules.ActivityTracking.Contracts.Metrics;
using Pickory.Shared.Http;

namespace Pickory.Modules.ActivityTracking.Features.AdminAnalytics;

// Đọc và kiểm khoảng ngày `from` đến `to` của API phễu. Cả hai ngày tính theo giờ Việt Nam và tính cả hai đầu.
internal static class FunnelRangeQuery
{
    // Giới hạn một năm nhuận. Khoảng dài hơn buộc truy vấn quét nhiều dòng `daily_counters` hơn mức báo cáo cần.
    public const int MaxDayCount = 366;

    private const string DateFormat = "yyyy-MM-dd";

    // Hàm thêm lỗi vào `failures`, và route gom chúng thành một lỗi `VALIDATION_ERROR`.
    // Hàm trả `null` khi có lỗi, để route không gọi `IActivityMetrics` với khoảng sai.
    public static DateRange? Read(IQueryCollection query, List<ValidationFailure> failures)
    {
        // Đọc cả hai tham số trước khi dừng, để một response báo đủ mọi lỗi của query string.
        var from = ReadRequiredDate(query, "from", failures);
        var to = ReadRequiredDate(query, "to", failures);
        if (from is null || to is null)
        {
            return null;
        }

        // Khoảng ngược không có ngày nào để cộng. Báo lỗi thay vì trả phễu rỗng khó hiểu.
        if (from > to)
        {
            failures.Add(new ValidationFailure("from", "Ngày bắt đầu không được sau ngày kết thúc."));
            return null;
        }

        // Số ngày tính cả hai đầu, nên khoảng một ngày có `from` bằng `to` là 1 ngày.
        var dayCount = to.Value.DayNumber - from.Value.DayNumber + 1;
        if (dayCount > MaxDayCount)
        {
            failures.Add(new ValidationFailure("to", $"Khoảng ngày không được dài hơn {MaxDayCount} ngày."));
            return null;
        }

        return new DateRange(from.Value, to.Value);
    }

    private static DateOnly? ReadRequiredDate(IQueryCollection query, string name, List<ValidationFailure> failures)
    {
        var failureCountBefore = failures.Count;
        var raw = QueryParameter.ReadSingle(query, name, failures);
        if (raw is null)
        {
            // Tham số lặp đã có lỗi riêng. Chỉ báo "thiếu" khi `ReadSingle` không thêm lỗi nào.
            if (failures.Count == failureCountBefore)
            {
                failures.Add(new ValidationFailure(name, $"Tham số `{name}` là bắt buộc."));
            }

            return null;
        }

        // Chỉ nhận đúng `yyyy-MM-dd`. Dạng có giờ hoặc múi giờ bị từ chối để ngày luôn hiểu theo giờ Việt Nam.
        if (!DateOnly.TryParseExact(raw, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            failures.Add(new ValidationFailure(name, $"Ngày của `{name}` phải có dạng yyyy-MM-dd."));
            return null;
        }

        return date;
    }
}
