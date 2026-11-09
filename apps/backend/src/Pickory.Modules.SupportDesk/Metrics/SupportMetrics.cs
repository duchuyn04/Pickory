using Microsoft.EntityFrameworkCore;
using Pickory.Modules.SupportDesk.Contracts.Metrics;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Shared.Calendar;

namespace Pickory.Modules.SupportDesk.Metrics;

// Cổng số liệu CSKH cho `admin-console`. Mỗi hàm dùng số truy vấn cố định và chỉ trả số gộp, không đọc nội dung chat hay thông tin khách.
internal sealed class SupportMetrics(SupportDeskDbContext database) : ISupportMetrics
{
    // Hai danh sách thời gian được đọc vào bộ nhớ để tính phân vị, nên khoảng bị chặn ở một năm nhuận.
    private const int MaxRangeDays = 366;

    private const double MedianPercentile = 50;
    private const double Percentile90 = 90;

    public async Task<SupportMetricsSnapshot> GetAsync(DateRange range, CancellationToken cancellationToken)
    {
        // Kiểm khoảng ngày trước khi chạm DB để khoảng sai không tốn truy vấn nào.
        ValidateRange(range);
        var (startUtc, endExclusiveUtc) = ToUtcWindow(range);

        var sessionTotals = await ReadSessionTotalsAsync(startUtc, endExclusiveUtc, cancellationToken);
        var firstResponseSeconds = await ReadFirstResponseSecondsAsync(startUtc, endExclusiveUtc, cancellationToken);
        var handleMinutes = await ReadHandleMinutesAsync(startUtc, endExclusiveUtc, cancellationToken);
        var callbackTotals = await ReadCallbackTotalsAsync(startUtc, endExclusiveUtc, cancellationToken);

        return new SupportMetricsSnapshot(
            Handoffs: sessionTotals.Handoffs,
            HandledInHours: sessionTotals.HandledInHours,
            CallbacksRequested: callbackTotals.Requested,
            CallbacksCompleted: callbackTotals.Completed,
            FirstResponseSeconds: Summarize(firstResponseSeconds),
            HandleMinutes: Summarize(handleMinutes),
            Csat: new CsatSummary(sessionTotals.CsatAverage, sessionTotals.CsatCount));
    }

    public async Task<SupportPendingCounts> GetPendingCountsAsync(CancellationToken cancellationToken)
    {
        // Phiên đang chờ là phiên chưa đóng và chưa có người nhận. Điều kiện này giống hàng chờ của nhân viên.
        var waiting = await database.Sessions.AsNoTracking()
            .CountAsync(session => session.StaffId == null && session.ClosedAt == null, cancellationToken);

        // Chỉ trạng thái `Pending` còn việc cho nhân viên. `Done` và `Unreachable` đã có kết quả.
        var callbacks = await database.CallbackRequests.AsNoTracking()
            .CountAsync(request => request.Status == CallbackRequestStatus.Pending, cancellationToken);

        return new SupportPendingCounts(waiting, callbacks);
    }

    private static void ValidateRange(DateRange range)
    {
        // Khoảng ngược không có phiên nào khớp. Đây là lỗi lập trình của bên gọi nên ném lỗi thay vì trả số 0.
        if (range.From > range.To)
        {
            throw new ArgumentException("Ngày bắt đầu không được sau ngày kết thúc.", nameof(range));
        }

        // Cộng thêm một vì khoảng gồm cả hai đầu.
        var dayCount = range.To.DayNumber - range.From.DayNumber + 1;
        if (dayCount > MaxRangeDays)
        {
            throw new ArgumentException($"Khoảng ngày không được dài quá {MaxRangeDays} ngày.", nameof(range));
        }
    }

    private static (DateTime StartUtc, DateTime EndExclusiveUtc) ToUtcWindow(DateRange range)
    {
        // Khoảng gồm cả ngày đầu và ngày cuối theo ngày lịch Việt Nam. Cận dưới gồm cận và cận trên không gồm cận, nên hai ngày liền nhau không bỏ sót hay trùng.
        return (VietnamCalendar.DayStartUtc(range.From), VietnamCalendar.DayEndExclusiveUtc(range.To));
    }

    private static PercentileSummary Summarize(IReadOnlyList<double> values)
    {
        // Danh sách rỗng cho cả hai phân vị là `null`, nên màn quản trị hiện "chưa có dữ liệu" thay vì số 0 sai.
        return new PercentileSummary(
            Percentile.Of(values, MedianPercentile),
            Percentile.Of(values, Percentile90));
    }

    // Phiên chuyển trong khoảng. Mọi số liệu theo phiên đọc từ cùng điều kiện này.
    private IQueryable<SupportSession> HandoffsInWindow(DateTime startUtc, DateTime endExclusiveUtc)
    {
        // Không theo dõi thay đổi vì cổng chỉ đọc.
        return database.Sessions.AsNoTracking()
            .Where(session => session.HandoffAt >= startUtc && session.HandoffAt < endExclusiveUtc);
    }

    private async Task<SessionTotals> ReadSessionTotalsAsync(
        DateTime startUtc, DateTime endExclusiveUtc, CancellationToken cancellationToken)
    {
        // Gộp ở DB bằng một truy vấn. Nhóm theo hằng số để mọi phiên vào một nhóm.
        var totals = await HandoffsInWindow(startUtc, endExclusiveUtc)
            .GroupBy(_ => 1)
            .Select(group => new SessionTotals(
                group.Count(),
                group.Count(session => session.HandoffInHours),
                // Chỉ phiên đã chấm điểm vào số lượt chấm và trung bình.
                group.Count(session => session.Csat != null),
                group.Average(session => session.Csat)))
            .SingleOrDefaultAsync(cancellationToken);

        // Khoảng không có phiên nào thì không có nhóm. Trả số 0 và trung bình `null`, không ném lỗi.
        return totals ?? new SessionTotals(0, 0, 0, null);
    }

    private async Task<IReadOnlyList<double>> ReadFirstResponseSecondsAsync(
        DateTime startUtc, DateTime endExclusiveUtc, CancellationToken cancellationToken)
    {
        // Chỉ phiên đã có người trả lời đầu tiên mới có thời gian phản hồi. Mốc đầu là lúc chuyển phiên, không phải lúc nhận.
        var timestamps = await HandoffsInWindow(startUtc, endExclusiveUtc)
            .Where(session => session.FirstResponseAt != null)
            .Select(session => new { session.HandoffAt, FirstResponseAt = session.FirstResponseAt!.Value })
            .ToListAsync(cancellationToken);

        // Tính ở bộ nhớ để giữ phần lẻ của giây mà không phụ thuộc cách EF dịch phép trừ thời gian.
        return timestamps.Select(row => (row.FirstResponseAt - row.HandoffAt).TotalSeconds).ToList();
    }

    private async Task<IReadOnlyList<double>> ReadHandleMinutesAsync(
        DateTime startUtc, DateTime endExclusiveUtc, CancellationToken cancellationToken)
    {
        // Thời gian giải quyết đo từ lúc nhận tới lúc đóng. Phiên tự đóng khi chưa ai nhận không có số này.
        var timestamps = await HandoffsInWindow(startUtc, endExclusiveUtc)
            .Where(session => session.ClaimedAt != null && session.ClosedAt != null)
            .Select(session => new { ClaimedAt = session.ClaimedAt!.Value, ClosedAt = session.ClosedAt!.Value })
            .ToListAsync(cancellationToken);

        // Tính ở bộ nhớ để giữ phần lẻ của phút.
        return timestamps.Select(row => (row.ClosedAt - row.ClaimedAt).TotalMinutes).ToList();
    }

    private async Task<CallbackTotals> ReadCallbackTotalsAsync(
        DateTime startUtc, DateTime endExclusiveUtc, CancellationToken cancellationToken)
    {
        // Số hoàn thành tính trên cùng tập yêu cầu tạo trong khoảng, nên không bao giờ vượt số yêu cầu.
        var totals = await database.CallbackRequests.AsNoTracking()
            .Where(request => request.CreatedAt >= startUtc && request.CreatedAt < endExclusiveUtc)
            .GroupBy(_ => 1)
            .Select(group => new CallbackTotals(
                group.Count(),
                // Chỉ `Done` là hoàn thành. `Unreachable` là nhân viên gọi mà không liên lạc được.
                group.Count(request => request.Status == CallbackRequestStatus.Done)))
            .SingleOrDefaultAsync(cancellationToken);

        // Khoảng không có yêu cầu nào thì không có nhóm.
        return totals ?? new CallbackTotals(0, 0);
    }

    private sealed record SessionTotals(int Handoffs, int HandledInHours, int CsatCount, double? CsatAverage);

    private sealed record CallbackTotals(int Requested, int Completed);
}
