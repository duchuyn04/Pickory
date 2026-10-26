using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Pickory.Modules.AdminConsole.Reports;
using Xunit;

namespace Pickory.UnitTests.Modules.AdminConsole;

public sealed class ReportRangeTests
{
    // Ngày cố định do test truyền vào. Hàm `ReportRange.Read` không đọc đồng hồ nên kết quả không đổi theo ngày chạy test.
    private static readonly DateOnly Today = new(2026, 10, 26);

    // Ngày muộn hơn `Today`, cho các test cần khoảng `custom` kéo sang tháng 11 mà vẫn không vượt quá hôm nay.
    private static readonly DateOnly LateToday = new(2026, 12, 31);

    private static QueryCollection Query(params (string Name, string[] Values)[] parameters)
    {
        var values = new Dictionary<string, StringValues>();
        foreach (var (name, parameterValues) in parameters)
        {
            values[name] = new StringValues(parameterValues);
        }

        return new QueryCollection(values);
    }

    private static QueryCollection Query(params (string Name, string Value)[] parameters)
    {
        return Query(parameters.Select(p => (p.Name, new[] { p.Value })).ToArray());
    }

    private static ReportPeriod ReadValid(DateOnly today, params (string Name, string Value)[] parameters)
    {
        var failures = new List<ValidationFailure>();
        var period = ReportRange.Read(Query(parameters), today, failures);
        Assert.Empty(failures);
        Assert.NotNull(period);
        return period;
    }

    private static List<ValidationFailure> ReadInvalid(DateOnly today, params (string Name, string Value)[] parameters)
    {
        var failures = new List<ValidationFailure>();
        var period = ReportRange.Read(Query(parameters), today, failures);
        Assert.Null(period);
        Assert.NotEmpty(failures);
        return failures;
    }

    // --- Lát 2: range cố định với độ chia day ---

    [Fact]
    public void Missing_range_and_granularity_use_30d_and_day()
    {
        var period = ReadValid(Today);

        Assert.Equal(new DateOnly(2026, 9, 27), period.From);
        Assert.Equal(Today, period.To);
        Assert.Equal(ReportGranularity.Day, period.Granularity);
        Assert.Equal(30, period.Buckets.Count);
    }

    [Theory]
    [InlineData("today", 2026, 10, 26)]
    [InlineData("7d", 2026, 10, 20)]
    [InlineData("30d", 2026, 9, 27)]
    [InlineData("this_month", 2026, 10, 1)]
    public void Fixed_ranges_end_today_and_start_on_expected_day(string range, int year, int month, int day)
    {
        var period = ReadValid(Today, ("range", range));

        Assert.Equal(new DateOnly(year, month, day), period.From);
        Assert.Equal(Today, period.To);
    }

    [Fact]
    public void Period_starts_at_17_utc_of_previous_day_and_ends_exclusive_after_today()
    {
        var period = ReadValid(Today, ("range", "today"));

        Assert.Equal(new DateTime(2026, 10, 25, 17, 0, 0, DateTimeKind.Utc), period.StartUtc);
        Assert.Equal(new DateTime(2026, 10, 26, 17, 0, 0, DateTimeKind.Utc), period.EndExclusiveUtc);
    }

    [Fact]
    public void Orders_at_2359_and_0000_vietnam_time_fall_into_two_different_day_buckets()
    {
        var period = ReadValid(Today, ("range", "7d"));
        var at2359 = new DateTime(2026, 10, 25, 16, 59, 0, DateTimeKind.Utc);
        var at0000 = new DateTime(2026, 10, 25, 17, 0, 0, DateTimeKind.Utc);

        var bucketOf2359 = period.Buckets.Single(b => b.StartUtc <= at2359 && at2359 < b.EndExclusiveUtc);
        var bucketOf0000 = period.Buckets.Single(b => b.StartUtc <= at0000 && at0000 < b.EndExclusiveUtc);

        Assert.NotEqual(bucketOf2359.Key, bucketOf0000.Key);
        Assert.Equal("2026-10-25", bucketOf2359.Key);
        Assert.Equal("2026-10-26", bucketOf0000.Key);
    }

    [Fact]
    public void Day_buckets_have_one_bucket_per_day_with_date_key()
    {
        var period = ReadValid(Today, ("range", "7d"));

        Assert.Equal(7, period.Buckets.Count);
        Assert.Equal("2026-10-20", period.Buckets[0].Key);
        Assert.Equal("2026-10-26", period.Buckets[6].Key);
        Assert.All(period.Buckets, b => Assert.Equal(b.From, b.To));
    }

    // --- Lát 3: range custom ---

    [Fact]
    public void Custom_range_reads_from_and_to()
    {
        var period = ReadValid(Today, ("range", "custom"), ("from", "2026-10-01"), ("to", "2026-10-05"));

        Assert.Equal(new DateOnly(2026, 10, 1), period.From);
        Assert.Equal(new DateOnly(2026, 10, 5), period.To);
    }

    [Fact]
    public void Custom_range_without_dates_reports_both_parameters()
    {
        var failures = ReadInvalid(Today, ("range", "custom"));

        Assert.Contains(failures, f => f.PropertyName == "from");
        Assert.Contains(failures, f => f.PropertyName == "to");
    }

    [Theory]
    [InlineData("2026-13-01")]
    [InlineData("26/10/2026")]
    [InlineData("2026-10-01T00:00:00Z")]
    public void Custom_range_rejects_wrong_date_format(string badFrom)
    {
        var failures = ReadInvalid(Today, ("range", "custom"), ("from", badFrom), ("to", "2026-10-05"));

        Assert.Contains(failures, f => f.PropertyName == "from");
    }

    [Fact]
    public void Custom_range_rejects_from_after_to()
    {
        var failures = ReadInvalid(Today, ("range", "custom"), ("from", "2026-10-06"), ("to", "2026-10-05"));

        Assert.Contains(failures, f => f.PropertyName == "from");
    }

    [Fact]
    public void Custom_range_accepts_exactly_366_days()
    {
        var period = ReadValid(Today, ("range", "custom"), ("from", "2025-10-26"), ("to", "2026-10-26"));

        Assert.Equal(366, period.Buckets.Count);
    }

    [Fact]
    public void Custom_range_rejects_367_days()
    {
        var failures = ReadInvalid(Today, ("range", "custom"), ("from", "2025-10-25"), ("to", "2026-10-26"));

        Assert.Contains(failures, f => f.PropertyName == "to");
    }

    [Fact]
    public void Custom_dates_are_ignored_when_range_is_not_custom()
    {
        var period = ReadValid(Today, ("range", "7d"), ("from", "khong-phai-ngay"), ("to", "2020-01-01"));

        Assert.Equal(new DateOnly(2026, 10, 20), period.From);
    }

    // --- Lát 4: range month và 6m ---

    [Fact]
    public void Month_range_for_past_month_covers_whole_calendar_month()
    {
        var period = ReadValid(Today, ("range", "month"), ("month", "2026-09"));

        Assert.Equal(new DateOnly(2026, 9, 1), period.From);
        Assert.Equal(new DateOnly(2026, 9, 30), period.To);
    }

    [Fact]
    public void Month_range_for_current_month_ends_today()
    {
        var period = ReadValid(Today, ("range", "month"), ("month", "2026-10"));

        Assert.Equal(new DateOnly(2026, 10, 1), period.From);
        Assert.Equal(Today, period.To);
    }

    [Fact]
    public void Month_range_for_leap_february_has_29_days()
    {
        var period = ReadValid(new DateOnly(2028, 5, 10), ("range", "month"), ("month", "2028-02"));

        Assert.Equal(new DateOnly(2028, 2, 29), period.To);
        Assert.Equal(29, period.Buckets.Count);
    }

    [Fact]
    public void Month_range_rejects_future_month()
    {
        var failures = ReadInvalid(Today, ("range", "month"), ("month", "2026-11"));

        Assert.Contains(failures, f => f.PropertyName == "month");
    }

    [Fact]
    public void Month_range_without_month_reports_month_parameter()
    {
        var failures = ReadInvalid(Today, ("range", "month"));

        Assert.Contains(failures, f => f.PropertyName == "month");
    }

    [Theory]
    [InlineData("2026-9")]
    [InlineData("2026-13")]
    [InlineData("2026-09-01")]
    [InlineData("09/2026")]
    public void Month_range_rejects_wrong_month_format(string badMonth)
    {
        var failures = ReadInvalid(Today, ("range", "month"), ("month", badMonth));

        Assert.Contains(failures, f => f.PropertyName == "month");
    }

    [Fact]
    public void Month_parameter_is_ignored_when_range_is_not_month()
    {
        var period = ReadValid(Today, ("range", "7d"), ("month", "sai"));

        Assert.Equal(new DateOnly(2026, 10, 20), period.From);
    }

    [Fact]
    public void Six_month_range_starts_five_months_before_current_month()
    {
        var period = ReadValid(Today, ("range", "6m"));

        Assert.Equal(new DateOnly(2026, 5, 1), period.From);
        Assert.Equal(Today, period.To);
    }

    [Fact]
    public void Six_month_range_crosses_new_year()
    {
        var period = ReadValid(new DateOnly(2027, 2, 10), ("range", "6m"));

        Assert.Equal(new DateOnly(2026, 9, 1), period.From);
        Assert.Equal(new DateOnly(2027, 2, 10), period.To);
    }

    [Fact]
    public void Six_month_range_with_month_granularity_gives_six_buckets()
    {
        var period = ReadValid(Today, ("range", "6m"), ("granularity", "month"));

        Assert.Equal(6, period.Buckets.Count);
        Assert.Equal("2026-05", period.Buckets[0].Key);
        Assert.Equal("2026-10", period.Buckets[5].Key);
    }

    // --- Lát 5: độ chia week và month ---

    [Fact]
    public void Week_buckets_start_on_monday_and_first_bucket_is_clipped()
    {
        // 2026-10-21 là thứ Tư. Tuần đầu bắt đầu thứ Hai 2026-10-19 nhưng bị cắt ở ngày bắt đầu của khoảng.
        var period = ReadValid(LateToday, ("range", "custom"), ("from", "2026-10-21"), ("to", "2026-11-03"), ("granularity", "week"));

        Assert.Equal(3, period.Buckets.Count);
        Assert.Equal("2026-10-19", period.Buckets[0].Key);
        Assert.Equal(new DateOnly(2026, 10, 21), period.Buckets[0].From);
        Assert.Equal(new DateOnly(2026, 10, 25), period.Buckets[0].To);
        Assert.Equal("2026-10-26", period.Buckets[1].Key);
        Assert.Equal(new DateTime(2026, 10, 25, 17, 0, 0, DateTimeKind.Utc), period.Buckets[1].StartUtc);
        Assert.Equal(new DateTime(2026, 11, 1, 17, 0, 0, DateTimeKind.Utc), period.Buckets[1].EndExclusiveUtc);
        Assert.Equal(new DateOnly(2026, 10, 26), period.Buckets[1].From);
        Assert.Equal(new DateOnly(2026, 11, 1), period.Buckets[1].To);
        Assert.Equal("2026-11-02", period.Buckets[2].Key);
        Assert.Equal(new DateOnly(2026, 11, 3), period.Buckets[2].To);
    }

    [Fact]
    public void Week_bucket_on_sunday_belongs_to_previous_monday()
    {
        // 2026-10-25 là Chủ nhật, nên thuộc tuần bắt đầu thứ Hai 2026-10-19.
        var period = ReadValid(Today, ("range", "custom"), ("from", "2026-10-25"), ("to", "2026-10-25"), ("granularity", "week"));

        Assert.Single(period.Buckets);
        Assert.Equal("2026-10-19", period.Buckets[0].Key);
    }

    [Fact]
    public void Month_buckets_follow_calendar_months_and_are_clipped()
    {
        var period = ReadValid(LateToday, ("range", "custom"), ("from", "2026-09-15"), ("to", "2026-11-10"), ("granularity", "month"));

        Assert.Equal(3, period.Buckets.Count);
        Assert.Equal("2026-09", period.Buckets[0].Key);
        Assert.Equal(new DateOnly(2026, 9, 15), period.Buckets[0].From);
        Assert.Equal(new DateOnly(2026, 9, 30), period.Buckets[0].To);
        Assert.Equal("2026-10", period.Buckets[1].Key);
        Assert.Equal(new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc), period.Buckets[1].StartUtc);
        Assert.Equal(new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc), period.Buckets[1].EndExclusiveUtc);
        Assert.Equal(new DateOnly(2026, 10, 1), period.Buckets[1].From);
        Assert.Equal(new DateOnly(2026, 10, 31), period.Buckets[1].To);
        Assert.Equal("2026-11", period.Buckets[2].Key);
        Assert.Equal(new DateOnly(2026, 11, 10), period.Buckets[2].To);
    }

    [Theory]
    [InlineData("day")]
    [InlineData("week")]
    [InlineData("month")]
    public void Buckets_are_contiguous_without_gap_or_overlap(string granularity)
    {
        var period = ReadValid(Today, ("range", "custom"), ("from", "2026-01-03"), ("to", "2026-10-26"), ("granularity", granularity));

        Assert.Equal(period.StartUtc, period.Buckets[0].StartUtc);
        Assert.Equal(period.EndExclusiveUtc, period.Buckets[^1].EndExclusiveUtc);
        for (var index = 1; index < period.Buckets.Count; index++)
        {
            Assert.Equal(period.Buckets[index - 1].EndExclusiveUtc, period.Buckets[index].StartUtc);
            Assert.Equal(period.Buckets[index - 1].To.AddDays(1), period.Buckets[index].From);
        }
    }

    // --- Lát 6: giá trị lạ và tham số lặp ---

    [Fact]
    public void Unknown_range_reports_range_parameter()
    {
        var failures = ReadInvalid(Today, ("range", "abc"));

        Assert.Contains(failures, f => f.PropertyName == "range");
    }

    [Fact]
    public void Unknown_granularity_reports_granularity_parameter()
    {
        var failures = ReadInvalid(Today, ("granularity", "hour"));

        Assert.Contains(failures, f => f.PropertyName == "granularity");
    }

    [Fact]
    public void Unknown_range_and_granularity_are_reported_in_one_call()
    {
        var failures = ReadInvalid(Today, ("range", "abc"), ("granularity", "hour"));

        Assert.Contains(failures, f => f.PropertyName == "range");
        Assert.Contains(failures, f => f.PropertyName == "granularity");
    }

    [Fact]
    public void Repeated_range_is_rejected()
    {
        var failures = new List<ValidationFailure>();

        var period = ReportRange.Read(Query(("range", new[] { "7d", "30d" })), Today, failures);

        Assert.Null(period);
        Assert.Contains(failures, f => f.PropertyName == "range");
    }

    [Fact]
    public void Repeated_granularity_is_rejected()
    {
        var failures = new List<ValidationFailure>();

        var period = ReportRange.Read(Query(("granularity", new[] { "day", "week" })), Today, failures);

        Assert.Null(period);
        Assert.Contains(failures, f => f.PropertyName == "granularity");
    }

    [Fact]
    public void Empty_values_fall_back_to_defaults()
    {
        var period = ReadValid(Today, ("range", ""), ("granularity", " "));

        Assert.Equal(ReportGranularity.Day, period.Granularity);
        Assert.Equal(new DateOnly(2026, 9, 27), period.From);
    }

    [Fact]
    public void Range_values_are_case_sensitive()
    {
        var failures = ReadInvalid(Today, ("range", "7D"));

        Assert.Contains(failures, f => f.PropertyName == "range");
    }

    // --- Review lượt 1: biên lịch ---

    [Theory]
    [InlineData("0001-01-01", "0001-01-01", "from")]
    [InlineData("9999-12-31", "9999-12-31", "to")]
    [InlineData("2026-10-01", "2026-10-27", "to")]
    public void Custom_range_rejects_dates_outside_report_bounds(string from, string to, string expectedParameter)
    {
        var failures = ReadInvalid(Today, ("range", "custom"), ("from", from), ("to", to));

        Assert.Contains(failures, f => f.PropertyName == expectedParameter);
    }

    [Fact]
    public void Custom_range_with_both_dates_out_of_bounds_reports_both_parameters()
    {
        var failures = ReadInvalid(Today, ("range", "custom"), ("from", "0001-01-01"), ("to", "2026-10-27"));

        Assert.Single(failures, f => f.PropertyName == "from");
        Assert.Single(failures, f => f.PropertyName == "to");
    }

    [Fact]
    public void Custom_range_accepts_earliest_report_date()
    {
        var period = ReadValid(Today, ("range", "custom"), ("from", "2000-01-01"), ("to", "2000-01-31"));

        Assert.Equal(new DateOnly(2000, 1, 1), period.From);
        Assert.Equal(31, period.Buckets.Count);
    }

    [Fact]
    public void Custom_range_accepts_to_equal_today()
    {
        var period = ReadValid(Today, ("range", "custom"), ("from", "2026-10-26"), ("to", "2026-10-26"));

        Assert.Single(period.Buckets);
    }

    [Fact]
    public void Month_range_rejects_month_before_earliest_report_date()
    {
        var failures = ReadInvalid(Today, ("range", "month"), ("month", "0001-01"));

        Assert.Contains(failures, f => f.PropertyName == "month");
    }

    [Fact]
    public void Month_range_rejects_far_future_month()
    {
        var failures = ReadInvalid(Today, ("range", "month"), ("month", "9999-12"));

        Assert.Contains(failures, f => f.PropertyName == "month");
    }

    [Fact]
    public void Month_range_accepts_earliest_report_month()
    {
        var period = ReadValid(Today, ("range", "month"), ("month", "2000-01"));

        Assert.Equal(new DateOnly(2000, 1, 31), period.To);
    }

    // --- Review lượt 1: nhánh lỗi ---

    [Fact]
    public void Month_range_with_empty_month_reports_exactly_one_failure()
    {
        var failures = ReadInvalid(Today, ("range", "month"), ("month", ""));

        Assert.Single(failures);
        Assert.Equal("month", failures[0].PropertyName);
    }

    [Theory]
    [InlineData("month")]
    [InlineData("from")]
    [InlineData("to")]
    public void Repeated_parameter_reports_exactly_one_failure_for_that_parameter(string repeated)
    {
        var parameters = new List<(string Name, string[] Values)>
        {
            ("range", new[] { repeated == "month" ? "month" : "custom" }),
            ("month", new[] { "2026-09" }),
            ("from", new[] { "2026-10-01" }),
            ("to", new[] { "2026-10-05" }),
        };
        var index = parameters.FindIndex(p => p.Name == repeated);
        parameters[index] = (repeated, new[] { parameters[index].Values[0], parameters[index].Values[0] });
        var failures = new List<ValidationFailure>();

        var period = ReportRange.Read(Query(parameters.ToArray()), Today, failures);

        Assert.Null(period);
        Assert.Single(failures);
        Assert.Equal(repeated, failures[0].PropertyName);
    }

    [Fact]
    public void Custom_range_with_only_bad_to_reports_only_to()
    {
        var failures = ReadInvalid(Today, ("range", "custom"), ("from", "2026-10-01"), ("to", "sai"));

        Assert.Single(failures);
        Assert.Equal("to", failures[0].PropertyName);
    }

    [Fact]
    public void Six_month_range_on_january_first_starts_in_august()
    {
        var period = ReadValid(new DateOnly(2027, 1, 1), ("range", "6m"));

        Assert.Equal(new DateOnly(2026, 8, 1), period.From);
        Assert.Equal(new DateOnly(2027, 1, 1), period.To);
    }
}
