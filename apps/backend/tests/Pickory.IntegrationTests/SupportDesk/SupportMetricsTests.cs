using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Contracts.Metrics;
using Pickory.Modules.SupportDesk.Entities;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-13: kiểm tra `ISupportMetrics` qua DI container thật và DB thật.

// Bộ dữ liệu dựng sẵn và bảng tính tay nằm trong khối bên dưới.
/*
Mọi mốc là giờ Việt Nam (UTC+7) của tháng 3/2040: D0 = 9/3, D1 = 10/3, D2 = 11/3, D3 = 12/3, D4 = 13/3.
Khoảng báo cáo là D1 đến D3. Phiên chuyển trong khoảng là phiên có `HandoffAt` thuộc [D1 00:00:00, D4 00:00:00).
Mỗi phiên nằm trong một hội thoại riêng. Phiên nhận lúc `HandoffAt` + 2 phút, nên giá trị tính từ `ClaimedAt` luôn lệch giá trị tính từ `HandoffAt`.

Mã  HandoffAt          Trong giờ  Phản hồi đầu (giây)  Giải quyết (phút)  CSAT
S01 D1 00:00:00        có         10                   5                  5
S02 D1 23:30:00        không      20                   10                 3
S03 D2 09:00:00        có         30                   15                 4
S04 D2 14:00:00        có         40                   20                 chưa chấm
S05 D3 10:00:00        không      50                   25                 5
S06 D3 23:59:59.999    có         không có             tự đóng, chưa nhận chưa chấm
S07 D2 11:00:00        có         60,5                 90,5               2
S08 D0 23:59:59        có         999                  999                1   (ngoài khoảng, ngay trước D1)
S09 D4 00:00:00        có         999                  999                1   (ngoài khoảng, ngay sau D3)

Kết quả mong đợi của khoảng D1 đến D3 (phiên chuyển trong khoảng: S01 đến S07):
- Handoffs = 7.
- HandledInHours = S01 S03 S04 S06 S07 = 5.
- FirstResponseSeconds: 10, 20, 30, 40, 50, 60,5 (6 giá trị, S06 không có).
  p50 là hạng ceil(0,5 x 6) = 3, nên 30. p90 là hạng ceil(0,9 x 6) = 6, nên 60,5.
- HandleMinutes: 5, 10, 15, 20, 25, 90,5 (6 giá trị, S06 chưa nhận nên không tính).
  p50 là hạng 3, nên 15. p90 là hạng 6, nên 90,5.
- Csat: S01 5, S02 3, S03 4, S05 5, S07 2. Tổng 19, số lượt chấm 5, trung bình 3,8.
- Khoảng một ngày: D1 có S01 S02 (2 phiên). D3 có S05 S06 (2 phiên). D4 có S09 (1 phiên).

Yêu cầu gọi lại (giờ Việt Nam):
C1 D1 00:00:00      Pending
C2 D1 23:30:00      Done
C3 D2 10:00:00      Done
C4 D2 12:00:00      Unreachable
C5 D3 23:59:59.999  Done
C6 D3 08:00:00      Pending
C7 D0 23:59:59      Done  (ngoài khoảng)
C8 D4 00:00:00      Done  (ngoài khoảng)
Kết quả mong đợi của khoảng D1 đến D3: CallbacksRequested = 6 (C1 đến C6), CallbacksCompleted = 3 (C2 C3 C5).

Việc đang chờ (test riêng, so với số đo trước khi gieo vì bảng dùng chung với test khác):
- Phiên chưa nhận và chưa đóng tính vào Waiting. Phiên đã nhận chưa đóng và phiên đã đóng không tính.
- Yêu cầu Pending tính vào Callbacks. Yêu cầu Done và Unreachable không tính.

Cách giữ test ổn định: mọi mốc thời gian là hằng số ở năm 2040 và 2041, không dùng `DateTime.UtcNow` trong dữ liệu dựng sẵn.
Mỗi test xóa hội thoại và yêu cầu gọi lại đã gieo trước và sau khi chạy.
*/
[Collection(SupportDeskCollection.Name)]
public sealed class SupportMetricsTests(SupportDeskWebApplicationFactory factory, SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private static readonly DateRange FixtureRange = new(new DateOnly(2040, 3, 10), new DateOnly(2040, 3, 12));

    public async Task InitializeAsync() => await CleanUpAsync();

    public async Task DisposeAsync() => await CleanUpAsync();

    // Xóa dữ liệu của test đã gieo. Cascade từ hội thoại xóa luôn phiên.
    private async Task CleanUpAsync()
    {
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
        await CallbackRequestTestKit.DeleteIssuedCallbackRequestsAsync(database);
    }

    // ---------- Dựng dữ liệu ----------

    // Giờ Việt Nam (UTC+7) của ngày trong tháng 3/2040, đổi sang UTC. Cùng offset cố định với `VietnamCalendar`.
    private static DateTime VietnamTimeToUtc(int day, int hour, int minute = 0, int second = 0, int millisecond = 0) =>
        DateTime.SpecifyKind(
            new DateTime(2040, 3, day, hour, minute, second, millisecond).AddHours(-7), DateTimeKind.Utc);

    // Gieo một phiên đã nhận và đóng. Phiên nhận lúc 2 phút sau `handoffAt`, để giá trị tính từ mốc sai luôn khác giá trị đúng.
    private async Task SeedHandledSessionAsync(
        DateTime handoffAt,
        bool inHours,
        double? firstResponseSeconds,
        double? handleMinutes,
        int? csat)
    {
        var claimedAt = handoffAt.AddMinutes(2);
        await SeedSessionInOwnConversationAsync(
            handoffAt,
            inHours,
            staffId: "metrics-staff",
            claimedAt: claimedAt,
            firstResponseAt: firstResponseSeconds is null ? null : handoffAt.AddSeconds(firstResponseSeconds.Value),
            closedAt: handleMinutes is null ? null : claimedAt.AddMinutes(handleMinutes.Value),
            csat: csat);
    }

    private async Task SeedSessionInOwnConversationAsync(
        DateTime handoffAt,
        bool inHours,
        string? staffId = null,
        DateTime? claimedAt = null,
        DateTime? firstResponseAt = null,
        DateTime? closedAt = null,
        int? csat = null)
    {
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, Guid.NewGuid().ToString(), lastMessageAt: handoffAt);
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, handoffAt, inHours, staffId, claimedAt, closedAt, csat);

        // Mốc phản hồi đầu không có tham số trong dụng cụ gieo, nên ghi bổ sung sau khi gieo phiên.
        if (firstResponseAt is null)
        {
            return;
        }

        await using var context = database.CreateSupportDeskContext();
        var stored = await context.Sessions.FindAsync(session.Id);
        stored!.FirstResponseAt = firstResponseAt;
        await context.SaveChangesAsync();
    }

    private async Task SeedCallbackAsync(DateTime createdAt, CallbackRequestStatus status)
    {
        await CallbackRequestTestKit.SeedCallbackRequestAsync(
            database, Guid.NewGuid().ToString(), status: status, createdAt: createdAt);
    }

    private async Task SeedFixtureAsync()
    {
        await SeedHandledSessionAsync(VietnamTimeToUtc(10, 0), inHours: true, 10, 5, 5);
        await SeedHandledSessionAsync(VietnamTimeToUtc(10, 23, 30), inHours: false, 20, 10, 3);
        await SeedHandledSessionAsync(VietnamTimeToUtc(11, 9), inHours: true, 30, 15, 4);
        await SeedHandledSessionAsync(VietnamTimeToUtc(11, 14), inHours: true, 40, 20, null);
        await SeedHandledSessionAsync(VietnamTimeToUtc(12, 10), inHours: false, 50, 25, 5);
        // S06 tự đóng khi chưa ai nhận: có `ClosedAt`, không có `ClaimedAt`.
        var unclaimedAutoClosedHandoffAt = VietnamTimeToUtc(12, 23, 59, 59, 999);
        await SeedSessionInOwnConversationAsync(unclaimedAutoClosedHandoffAt, inHours: true, closedAt: unclaimedAutoClosedHandoffAt.AddMinutes(30));
        await SeedHandledSessionAsync(VietnamTimeToUtc(11, 11), inHours: true, 60.5, 90.5, 2);
        await SeedHandledSessionAsync(VietnamTimeToUtc(9, 23, 59, 59), inHours: true, 999, 999, 1);
        await SeedHandledSessionAsync(VietnamTimeToUtc(13, 0), inHours: true, 999, 999, 1);

        await SeedCallbackAsync(VietnamTimeToUtc(10, 0), CallbackRequestStatus.Pending);
        await SeedCallbackAsync(VietnamTimeToUtc(10, 23, 30), CallbackRequestStatus.Done);
        await SeedCallbackAsync(VietnamTimeToUtc(11, 10), CallbackRequestStatus.Done);
        await SeedCallbackAsync(VietnamTimeToUtc(11, 12), CallbackRequestStatus.Unreachable);
        await SeedCallbackAsync(VietnamTimeToUtc(12, 23, 59, 59, 999), CallbackRequestStatus.Done);
        await SeedCallbackAsync(VietnamTimeToUtc(12, 8), CallbackRequestStatus.Pending);
        await SeedCallbackAsync(VietnamTimeToUtc(9, 23, 59, 59), CallbackRequestStatus.Done);
        await SeedCallbackAsync(VietnamTimeToUtc(13, 0), CallbackRequestStatus.Done);
    }

    // Lấy cổng từ DI container thật, giống cách `admin-console` lấy cổng này khi chạy thật.
    private async Task<T> WithMetricsAsync<T>(Func<ISupportMetrics, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ISupportMetrics>());
    }

    private Task<SupportMetricsSnapshot> GetAsync(DateRange range) =>
        WithMetricsAsync(metrics => metrics.GetAsync(range, CancellationToken.None));

    // ---------- Số đếm ----------

    [Fact]
    public async Task GetAsync_OnFixture_CountsHandoffsAndHandledInHours()
    {
        await SeedFixtureAsync();

        var snapshot = await GetAsync(FixtureRange);

        Assert.Equal(7, snapshot.Handoffs);
        Assert.Equal(5, snapshot.HandledInHours);
    }

    [Fact]
    public async Task GetAsync_OnFixture_CountsOnlyDoneCallbacksAsCompleted()
    {
        await SeedFixtureAsync();

        var snapshot = await GetAsync(FixtureRange);

        // Yêu cầu `Pending` và `Unreachable` có trong số yêu cầu, nhưng không phải số hoàn thành.
        Assert.Equal(6, snapshot.CallbacksRequested);
        Assert.Equal(3, snapshot.CallbacksCompleted);
    }

    // ---------- Phân vị và CSAT ----------

    [Fact]
    public async Task GetAsync_OnFixture_ComputesFirstResponsePercentilesFromHandoffAt()
    {
        await SeedFixtureAsync();

        var snapshot = await GetAsync(FixtureRange);

        Assert.Equal(30, snapshot.FirstResponseSeconds.P50);
        Assert.Equal(60.5, snapshot.FirstResponseSeconds.P90);
    }

    [Fact]
    public async Task GetAsync_OnFixture_ComputesHandleMinutesFromClaimedAtAndSkipsUnclaimedSessions()
    {
        await SeedFixtureAsync();

        var snapshot = await GetAsync(FixtureRange);

        Assert.Equal(15, snapshot.HandleMinutes.P50);
        Assert.Equal(90.5, snapshot.HandleMinutes.P90);
    }

    [Fact]
    public async Task GetAsync_OnFixture_AveragesCsatOverRatedSessionsOnly()
    {
        await SeedFixtureAsync();

        var snapshot = await GetAsync(FixtureRange);

        Assert.Equal(5, snapshot.Csat.Count);
        Assert.Equal(3.8, snapshot.Csat.Average!.Value, precision: 10);
    }

    // ---------- Biên ngày giờ Việt Nam ----------

    [Fact]
    public async Task GetAsync_SessionAtHalfPastElevenAtNightVietnamTime_BelongsToThatVietnamDay()
    {
        await SeedFixtureAsync();

        // S02 lúc 23:30 giờ Việt Nam của D1 là 16:30 UTC cùng ngày. S01 lúc 00:00:00 của D1 là 17:00 UTC của D0.
        var firstDay = await GetAsync(new DateRange(new DateOnly(2040, 3, 10), new DateOnly(2040, 3, 10)));

        Assert.Equal(2, firstDay.Handoffs);
    }

    [Fact]
    public async Task GetAsync_SessionOneMillisecondBeforeMidnight_BelongsToTheLastDayOfTheRange()
    {
        await SeedFixtureAsync();

        // S06 lúc 23:59:59.999 của D3 thuộc D3. S09 lúc 00:00:00 của D4 thuộc D4 vì cận trên không gồm cận.
        var lastDay = await GetAsync(new DateRange(new DateOnly(2040, 3, 12), new DateOnly(2040, 3, 12)));
        var nextDay = await GetAsync(new DateRange(new DateOnly(2040, 3, 13), new DateOnly(2040, 3, 13)));

        Assert.Equal(2, lastDay.Handoffs);
        Assert.Equal(1, nextDay.Handoffs);
    }

    // ---------- Khoảng ngày ----------

    [Fact]
    public async Task GetAsync_RangeWithoutData_ReturnsZeroCountsAndNullPercentiles()
    {
        await SeedFixtureAsync();

        var snapshot = await GetAsync(new DateRange(new DateOnly(2041, 5, 1), new DateOnly(2041, 5, 3)));

        Assert.Equal(0, snapshot.Handoffs);
        Assert.Equal(0, snapshot.HandledInHours);
        Assert.Equal(0, snapshot.CallbacksRequested);
        Assert.Equal(0, snapshot.CallbacksCompleted);
        Assert.Null(snapshot.FirstResponseSeconds.P50);
        Assert.Null(snapshot.FirstResponseSeconds.P90);
        Assert.Null(snapshot.HandleMinutes.P50);
        Assert.Null(snapshot.HandleMinutes.P90);
        Assert.Null(snapshot.Csat.Average);
        Assert.Equal(0, snapshot.Csat.Count);
    }

    [Fact]
    public async Task GetAsync_FromAfterTo_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => GetAsync(new DateRange(new DateOnly(2040, 3, 12), new DateOnly(2040, 3, 11))));
    }

    [Fact]
    public async Task GetAsync_RangeOf367Days_ThrowsArgumentException()
    {
        // 1/1/2040 đến 1/1/2041 gồm cả hai đầu là 367 ngày vì 2040 là năm nhuận.
        await Assert.ThrowsAsync<ArgumentException>(
            () => GetAsync(new DateRange(new DateOnly(2040, 1, 1), new DateOnly(2041, 1, 1))));
    }

    [Fact]
    public async Task GetAsync_RangeOfExactly366Days_RunsAndCoversTheFixture()
    {
        await SeedFixtureAsync();

        // 1/1/2040 đến 31/12/2040 gồm cả hai đầu là đúng 366 ngày.
        var snapshot = await GetAsync(new DateRange(new DateOnly(2040, 1, 1), new DateOnly(2040, 12, 31)));

        // S08 và S09 nằm trong khoảng dài này, nên có 9 phiên thay vì 7.
        Assert.Equal(9, snapshot.Handoffs);
    }

    // ---------- Việc đang chờ ----------

    [Fact]
    public async Task GetPendingCountsAsync_CountsOnlyUnclaimedOpenSessionsAndPendingCallbacks()
    {
        var before = await WithMetricsAsync(metrics => metrics.GetPendingCountsAsync(CancellationToken.None));
        var handoffAt = new DateTime(2041, 6, 1, 3, 0, 0, DateTimeKind.Utc);

        // Hai phiên đang chờ: chưa đóng và chưa có người nhận.
        await SeedSessionInOwnConversationAsync(handoffAt, inHours: true);
        await SeedSessionInOwnConversationAsync(handoffAt.AddMinutes(1), inHours: false);
        // Phiên đã có người nhận nhưng chưa đóng không còn chờ.
        await SeedSessionInOwnConversationAsync(
            handoffAt, inHours: true, staffId: "metrics-staff", claimedAt: handoffAt.AddMinutes(2));
        // Phiên đã đóng khi chưa nhận không còn chờ.
        await SeedSessionInOwnConversationAsync(handoffAt, inHours: true, closedAt: handoffAt.AddMinutes(30));
        // Một yêu cầu gọi lại còn mở. Hai yêu cầu đã xong hoặc không liên lạc được không tính.
        await SeedCallbackAsync(handoffAt, CallbackRequestStatus.Pending);
        await SeedCallbackAsync(handoffAt, CallbackRequestStatus.Done);
        await SeedCallbackAsync(handoffAt, CallbackRequestStatus.Unreachable);

        var after = await WithMetricsAsync(metrics => metrics.GetPendingCountsAsync(CancellationToken.None));

        Assert.Equal(before.Waiting + 2, after.Waiting);
        Assert.Equal(before.Callbacks + 1, after.Callbacks);
    }

    // ---------- Hợp đồng ----------

    [Fact]
    public void SupportMetricsSnapshot_HasNoDraftUsageRateProperty()
    {
        // SD-12 đã bỏ chỉ số này, nên snapshot không được có lại trường đó.
        var propertyNames = typeof(SupportMetricsSnapshot).GetProperties().Select(property => property.Name);

        Assert.DoesNotContain(propertyNames, name => name.Contains("DraftUsageRate", StringComparison.OrdinalIgnoreCase));
    }
}
