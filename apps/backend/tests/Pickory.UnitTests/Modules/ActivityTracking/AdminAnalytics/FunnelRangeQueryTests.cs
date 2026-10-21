using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Pickory.Modules.ActivityTracking.Contracts.Metrics;
using Pickory.Modules.ActivityTracking.Features.AdminAnalytics;
using Xunit;

namespace Pickory.UnitTests.Modules.ActivityTracking.AdminAnalytics;

// ACT-11: luật kiểm khoảng ngày của API phễu. Test dùng ngày cố định năm 2031 nên kết quả không đổi theo ngày chạy.
public sealed class FunnelRangeQueryTests
{
    [Fact]
    public void Read_ValidRange_ReturnsBothDates()
    {
        var failures = new List<ValidationFailure>();

        var range = FunnelRangeQuery.Read(BuildQuery("2031-05-10", "2031-05-12"), failures);

        Assert.Empty(failures);
        Assert.Equal(new DateRange(new DateOnly(2031, 5, 10), new DateOnly(2031, 5, 12)), range);
    }

    [Fact]
    public void Read_FromEqualsTo_IsAValidOneDayRange()
    {
        var failures = new List<ValidationFailure>();

        var range = FunnelRangeQuery.Read(BuildQuery("2031-05-10", "2031-05-10"), failures);

        Assert.Empty(failures);
        Assert.Equal(new DateRange(new DateOnly(2031, 5, 10), new DateOnly(2031, 5, 10)), range);
    }

    [Fact]
    public void Read_Exactly366DaysIncludingBothEnds_IsValid()
    {
        var failures = new List<ValidationFailure>();

        // Từ 2031-01-01 đến 2032-01-01 là 365 ngày chênh lệch, tức 366 ngày khi tính cả hai đầu.
        var range = FunnelRangeQuery.Read(BuildQuery("2031-01-01", "2032-01-01"), failures);

        Assert.Empty(failures);
        Assert.NotNull(range);
    }

    [Fact]
    public void Read_367DaysIncludingBothEnds_FailsOnTo()
    {
        var failures = new List<ValidationFailure>();

        var range = FunnelRangeQuery.Read(BuildQuery("2031-01-01", "2032-01-02"), failures);

        Assert.Null(range);
        Assert.Equal(["to"], failures.Select(failure => failure.PropertyName));
    }

    [Fact]
    public void Read_FullDateOnlyRange_FailsOnToWithoutOverflow()
    {
        var failures = new List<ValidationFailure>();

        // Hai đầu của `DateOnly` cho số ngày lớn nhất có thể. Phép tính số ngày phải không tràn và vẫn báo lỗi quá dài.
        var range = FunnelRangeQuery.Read(BuildQuery("0001-01-01", "9999-12-31"), failures);

        Assert.Null(range);
        Assert.Equal(["to"], failures.Select(failure => failure.PropertyName));
    }

    [Fact]
    public void Read_FromAfterTo_FailsOnFrom()
    {
        var failures = new List<ValidationFailure>();

        var range = FunnelRangeQuery.Read(BuildQuery("2031-05-13", "2031-05-12"), failures);

        Assert.Null(range);
        Assert.Equal(["from"], failures.Select(failure => failure.PropertyName));
    }

    [Fact]
    public void Read_MissingFrom_FailsOnFrom()
    {
        var failures = new List<ValidationFailure>();

        var range = FunnelRangeQuery.Read(BuildQuery(from: null, to: "2031-05-12"), failures);

        Assert.Null(range);
        Assert.Equal(["from"], failures.Select(failure => failure.PropertyName));
    }

    [Fact]
    public void Read_MissingTo_FailsOnTo()
    {
        var failures = new List<ValidationFailure>();

        var range = FunnelRangeQuery.Read(BuildQuery(from: "2031-05-10", to: null), failures);

        Assert.Null(range);
        Assert.Equal(["to"], failures.Select(failure => failure.PropertyName));
    }

    [Fact]
    public void Read_MissingBoth_ReportsBothInOneCall()
    {
        var failures = new List<ValidationFailure>();

        var range = FunnelRangeQuery.Read(BuildQuery(from: null, to: null), failures);

        Assert.Null(range);
        Assert.Equal(["from", "to"], failures.Select(failure => failure.PropertyName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_BlankValue_CountsAsMissing(string blank)
    {
        var failures = new List<ValidationFailure>();

        var range = FunnelRangeQuery.Read(BuildQuery(from: blank, to: "2031-05-12"), failures);

        Assert.Null(range);
        Assert.Equal(["from"], failures.Select(failure => failure.PropertyName));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("2031-5-9")]
    [InlineData("10/05/2031")]
    [InlineData("2031-05-10T00:00:00")]
    [InlineData("2031-05-10T00:00:00+07:00")]
    [InlineData("2031-02-30")]
    public void Read_WrongDateFormat_FailsOnThatParameterOnly(string badDate)
    {
        var failures = new List<ValidationFailure>();

        var range = FunnelRangeQuery.Read(BuildQuery(from: badDate, to: "2031-05-12"), failures);

        // Một lỗi cho một tham số. Luật `from` sau `to` không chạy khi `from` đã sai, nên không có lỗi thứ hai.
        Assert.Null(range);
        Assert.Equal(["from"], failures.Select(failure => failure.PropertyName));
    }

    [Fact]
    public void Read_RepeatedParameter_FailsOnceWithoutExtraMissingError()
    {
        var failures = new List<ValidationFailure>();
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["from"] = new StringValues(["2031-05-10", "2031-05-11"]),
            ["to"] = "2031-05-12",
        });

        var range = FunnelRangeQuery.Read(query, failures);

        Assert.Null(range);
        Assert.Equal(["from"], failures.Select(failure => failure.PropertyName));
    }

    [Fact]
    public void Read_ReportsEveryParameterErrorTogether()
    {
        var failures = new List<ValidationFailure>();

        var range = FunnelRangeQuery.Read(BuildQuery(from: "abc", to: "xyz"), failures);

        Assert.Null(range);
        Assert.Equal(["from", "to"], failures.Select(failure => failure.PropertyName));
    }

    private static QueryCollection BuildQuery(string? from, string? to)
    {
        var values = new Dictionary<string, StringValues>();
        if (from is not null)
        {
            values["from"] = from;
        }

        if (to is not null)
        {
            values["to"] = to;
        }

        return new QueryCollection(values);
    }
}
