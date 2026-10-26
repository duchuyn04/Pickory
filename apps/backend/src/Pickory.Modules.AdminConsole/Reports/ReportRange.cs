using System.Globalization;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Pickory.Shared.Calendar;
using Pickory.Shared.Http;

namespace Pickory.Modules.AdminConsole.Reports;

// Đọc `range`, `granularity` và các tham số đi kèm của API báo cáo. Mọi ngày tính theo giờ Việt Nam và tính cả hai đầu.
internal static class ReportRange
{
    // Giới hạn một năm nhuận. Khoảng dài hơn buộc truy vấn quét nhiều dòng dữ liệu hơn mức báo cáo cần.
    public const int MaxDayCount = 366;

    // Ngày sớm nhất được báo cáo. Ngày quá xa làm phép đổi sang UTC ném exception, và route sẽ trả 500 thay vì 400.
    private static readonly DateOnly EarliestReportDate = new(2000, 1, 1);

    // Số tháng lùi lại từ tháng này để `6m` có đúng 6 tháng lịch, gồm cả tháng này.
    private const int SixMonthRangeLookbackMonths = 5;

    private const string DefaultRange = "30d";
    private const string DateFormat = "yyyy-MM-dd";
    private const string MonthFormat = "yyyy-MM";

    // Khi có lỗi, hàm thêm lỗi vào `failures` và trả `null`, để route gom các lỗi thành `VALIDATION_ERROR`.
    // `today` do bên gọi truyền vào, nên kết quả không phụ thuộc đồng hồ.
    public static ReportPeriod? Read(IQueryCollection query, DateOnly today, List<ValidationFailure> failures)
    {
        var failureCountBefore = failures.Count;

        // Đọc cả hai tham số chính trước khi dừng, để một response báo đủ mọi lỗi của query string.
        var granularity = ReadGranularity(query, failures);
        var dayRange = ReadDayRange(query, today, failures);

        // Mọi lỗi đã nằm trong `failures`. Route không được dùng khoảng sai để truy vấn.
        if (failures.Count > failureCountBefore || granularity is null || dayRange is null)
        {
            return null;
        }

        var (from, to) = dayRange.Value;
        return new ReportPeriod(
            from,
            to,
            VietnamCalendar.DayStartUtc(from),
            VietnamCalendar.DayEndExclusiveUtc(to),
            granularity.Value,
            BuildBuckets(from, to, granularity.Value));
    }

    private static ReportGranularity? ReadGranularity(IQueryCollection query, List<ValidationFailure> failures)
    {
        var failureCountBefore = failures.Count;
        var raw = QueryParameter.ReadSingle(query, "granularity", failures);
        if (raw is null)
        {
            // Tham số lặp đã có lỗi riêng. Chỉ dùng giá trị mặc định khi `ReadSingle` không thêm lỗi nào.
            return failures.Count == failureCountBefore ? ReportGranularity.Day : null;
        }

        // So khớp đúng chữ thường. Giá trị lạ bị từ chối thay vì lặng lẽ rơi về `day`.
        switch (raw)
        {
            case "day":
                return ReportGranularity.Day;
            case "week":
                return ReportGranularity.Week;
            case "month":
                return ReportGranularity.Month;
            default:
                failures.Add(new ValidationFailure("granularity", "Độ chia phải là `day`, `week` hoặc `month`."));
                return null;
        }
    }

    private static (DateOnly From, DateOnly To)? ReadDayRange(IQueryCollection query, DateOnly today, List<ValidationFailure> failures)
    {
        var failureCountBefore = failures.Count;
        var range = QueryParameter.ReadSingle(query, "range", failures);
        if (range is null)
        {
            // Tham số lặp đã có lỗi riêng. Thiếu `range` thì dùng khoảng mặc định.
            if (failures.Count > failureCountBefore)
            {
                return null;
            }

            range = DefaultRange;
        }

        var startOfThisMonth = new DateOnly(today.Year, today.Month, 1);
        switch (range)
        {
            case "today":
                return (today, today);
            case "7d":
                return (today.AddDays(-6), today);
            case "30d":
                return (today.AddDays(-29), today);
            case "this_month":
                return (startOfThisMonth, today);
            case "6m":
                return (startOfThisMonth.AddMonths(-SixMonthRangeLookbackMonths), today);
            case "month":
                return ReadMonthRange(query, today, failures);
            case "custom":
                return ReadCustomRange(query, today, failures);
            default:
                failures.Add(new ValidationFailure("range", "Khoảng thời gian phải là `today`, `7d`, `30d`, `this_month`, `month`, `6m` hoặc `custom`."));
                return null;
        }
    }

    private static (DateOnly From, DateOnly To)? ReadMonthRange(IQueryCollection query, DateOnly today, List<ValidationFailure> failures)
    {
        var failureCountBefore = failures.Count;
        var raw = QueryParameter.ReadSingle(query, "month", failures);
        if (raw is null)
        {
            // Tham số lặp đã có lỗi riêng. Chỉ báo "thiếu" khi `ReadSingle` không thêm lỗi nào.
            if (failures.Count == failureCountBefore)
            {
                failures.Add(new ValidationFailure("month", "Tham số `month` là bắt buộc khi `range=month`."));
            }

            return null;
        }

        // Ghép thêm ngày 1 rồi parse đúng định dạng. Dạng `2026-9` hoặc `2026-09-01` bị từ chối.
        if (!DateOnly.TryParseExact(raw + "-01", DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startOfMonth))
        {
            failures.Add(new ValidationFailure("month", "Tháng của `month` phải có dạng YYYY-MM."));
            return null;
        }

        // Tháng quá xa trong quá khứ không có dữ liệu. Chặn trước khi tính biên UTC để không ném exception.
        if (startOfMonth < EarliestReportDate)
        {
            failures.Add(new ValidationFailure("month", $"Tháng của `month` không được trước {EarliestReportDate.ToString(MonthFormat, CultureInfo.InvariantCulture)}."));
            return null;
        }

        // Tháng tương lai chưa có dữ liệu. Báo lỗi thay vì trả báo cáo rỗng khó hiểu.
        var startOfThisMonth = new DateOnly(today.Year, today.Month, 1);
        if (startOfMonth > startOfThisMonth)
        {
            failures.Add(new ValidationFailure("month", "Tháng của `month` không được sau tháng hiện tại."));
            return null;
        }

        // Tháng đang chạy chỉ có dữ liệu tới hôm nay. Tháng đã qua lấy đủ đến ngày cuối tháng.
        var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);
        return (startOfMonth, startOfMonth == startOfThisMonth ? today : endOfMonth);
    }

    private static (DateOnly From, DateOnly To)? ReadCustomRange(IQueryCollection query, DateOnly today, List<ValidationFailure> failures)
    {
        // Đọc cả hai ngày trước khi dừng, để một response báo đủ mọi lỗi của query string.
        var from = ReadRequiredDate(query, "from", failures);
        var to = ReadRequiredDate(query, "to", failures);
        if (from is null || to is null)
        {
            return null;
        }

        // Ngày quá xa trong quá khứ làm phép đổi sang UTC ném exception. Kiểm cả hai ngày để một response báo đủ lỗi.
        var hasOutOfBoundsDate = false;
        if (from < EarliestReportDate)
        {
            failures.Add(new ValidationFailure("from", $"Ngày bắt đầu không được trước {EarliestReportDate.ToString(DateFormat, CultureInfo.InvariantCulture)}."));
            hasOutOfBoundsDate = true;
        }

        // Ngày sau hôm nay chưa có dữ liệu. Cũng chặn được ngày cuối của lịch, nơi phép cộng ngày ném exception.
        if (to > today)
        {
            failures.Add(new ValidationFailure("to", "Ngày kết thúc không được sau hôm nay."));
            hasOutOfBoundsDate = true;
        }

        // Dừng sau khi đã gom đủ lỗi biên. Các bước kiểm sau cần hai ngày hợp lệ.
        if (hasOutOfBoundsDate)
        {
            return null;
        }

        // Khoảng ngược không có ngày nào để cộng. Báo lỗi thay vì trả báo cáo rỗng khó hiểu.
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

        return (from.Value, to.Value);
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
                failures.Add(new ValidationFailure(name, $"Tham số `{name}` là bắt buộc khi `range=custom`."));
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

    private static List<ReportBucket> BuildBuckets(DateOnly from, DateOnly to, ReportGranularity granularity)
    {
        var buckets = new List<ReportBucket>();
        var cursor = from;
        while (cursor <= to)
        {
            var (key, naturalEnd) = DescribeBucketContaining(cursor, granularity);

            // Nhóm cuối bị cắt ở `to`. Báo cáo không có dữ liệu sau ngày kết thúc của khoảng.
            var bucketEnd = naturalEnd < to ? naturalEnd : to;

            // Nhóm đầu bắt đầu ở `cursor` chứ không ở đầu tuần hay đầu tháng, nên nhóm bị cắt ở ngày `from`.
            buckets.Add(new ReportBucket(
                key,
                cursor,
                bucketEnd,
                VietnamCalendar.DayStartUtc(cursor),
                VietnamCalendar.DayEndExclusiveUtc(bucketEnd)));

            // Nhóm kế tiếp bắt đầu ngay ngày sau. Các nhóm vì thế liền nhau, không chồng và không hở.
            cursor = bucketEnd.AddDays(1);
        }

        return buckets;
    }

    // Trả khóa của nhóm tự nhiên chứa `day` và ngày cuối của nhóm đó, chưa cắt theo khoảng báo cáo.
    private static (string Key, DateOnly NaturalEnd) DescribeBucketContaining(DateOnly day, ReportGranularity granularity)
    {
        switch (granularity)
        {
            case ReportGranularity.Week:
                // `DayOfWeek` đếm Chủ nhật là 0. Phép dịch này đưa thứ Hai về 0 để tuần bắt đầu thứ Hai.
                var daysSinceMonday = ((int)day.DayOfWeek + 6) % 7;
                var monday = day.AddDays(-daysSinceMonday);
                return (monday.ToString(DateFormat, CultureInfo.InvariantCulture), monday.AddDays(6));
            case ReportGranularity.Month:
                var firstOfMonth = new DateOnly(day.Year, day.Month, 1);
                return (firstOfMonth.ToString(MonthFormat, CultureInfo.InvariantCulture), firstOfMonth.AddMonths(1).AddDays(-1));
            default:
                return (day.ToString(DateFormat, CultureInfo.InvariantCulture), day);
        }
    }
}
