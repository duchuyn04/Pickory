using Pickory.Modules.SupportDesk.Scheduling;
using Xunit;

namespace Pickory.UnitTests.Modules.SupportDesk.Scheduling;

// SD-01: hàm thuần ước tính số phút chờ của khách theo quyết định 9 trong plan.
public sealed class WaitEstimatorTests
{
    [Fact]
    public void DefaultWaitPerSession_IsFiveMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), WaitEstimator.DefaultWaitPerSession);
    }

    [Theory]
    [InlineData(0, 1, 5)]
    [InlineData(2, 1, 15)]
    public void Estimate_NoSevenDayData_UsesFiveMinutesPerSession(int waitingCount, int availableStaff, int expectedMinutes)
    {
        var minutes = WaitEstimator.Estimate(averageWait: null, waitingCount, availableStaff);

        Assert.Equal(expectedMinutes, minutes);
    }

    // 4 phút nhân (3 + 1) phiên, chia 2 nhân viên cho đúng 8 phút. Thiếu phần "+ 1" thì kết quả là 6.
    [Fact]
    public void Estimate_CountsTheNewCustomerAsOneMoreSessionInTheQueue()
    {
        var minutes = WaitEstimator.Estimate(TimeSpan.FromMinutes(4), waitingCount: 3, availableStaff: 2);

        Assert.Equal(8, minutes);
    }

    // 4 phút nhân 3 phiên, chia 5 nhân viên cho 2,4 phút. Làm tròn lên là 3, làm tròn xuống là 2.
    [Fact]
    public void Estimate_RoundsFractionalMinutesUp()
    {
        var minutes = WaitEstimator.Estimate(TimeSpan.FromMinutes(4), waitingCount: 2, availableStaff: 5);

        Assert.Equal(3, minutes);
    }

    // Không có dữ liệu thì 5 phút nhân 3 phiên, chia 2 nhân viên cho 7,5 phút. Làm tròn lên là 8.
    [Fact]
    public void Estimate_DefaultWaitWithTwoStaff_RoundsSevenAndHalfUpToEight()
    {
        var minutes = WaitEstimator.Estimate(averageWait: null, waitingCount: 2, availableStaff: 2);

        Assert.Equal(8, minutes);
    }

    // 90 giây cho một phiên đầu hàng là 1,5 phút. Phần giây không được bị cắt bỏ trước khi làm tròn.
    [Fact]
    public void Estimate_KeepsSecondsOfAverageWaitBeforeRounding()
    {
        var minutes = WaitEstimator.Estimate(TimeSpan.FromSeconds(90), waitingCount: 0, availableStaff: 1);

        Assert.Equal(2, minutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Estimate_LessThanOneAvailableStaff_CountsAsOneStaffWithoutThrowing(int availableStaff)
    {
        var minutes = WaitEstimator.Estimate(TimeSpan.FromMinutes(4), waitingCount: 2, availableStaff);

        Assert.Equal(12, minutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Estimate_VeryShortAverageWait_NeverReturnsLessThanOneMinute(int averageWaitSeconds)
    {
        var minutes = WaitEstimator.Estimate(TimeSpan.FromSeconds(averageWaitSeconds), waitingCount: 0, availableStaff: 3);

        Assert.Equal(1, minutes);
    }

    [Fact]
    public void Estimate_NegativeWaitingCount_ThrowsForThatParameter()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            WaitEstimator.Estimate(averageWait: null, waitingCount: -1, availableStaff: 1));

        Assert.Equal("waitingCount", exception.ParamName);
    }

    [Fact]
    public void Estimate_NegativeAverageWait_ThrowsForThatParameter()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            WaitEstimator.Estimate(TimeSpan.FromMinutes(-1), waitingCount: 0, availableStaff: 1));

        Assert.Equal("averageWait", exception.ParamName);
    }
}
