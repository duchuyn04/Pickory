namespace Pickory.Modules.SupportDesk.Scheduling;

// Hàm thuần ước tính số phút khách phải chờ. Hàm không đọc DB, nên bên gọi tự truyền số liệu đã đo.
internal static class WaitEstimator
{
    // Chưa có dữ liệu 7 ngày thì chưa biết một phiên kéo dài bao lâu. Giá trị mặc định tránh trả số 0 vô nghĩa.
    public static readonly TimeSpan DefaultWaitPerSession = TimeSpan.FromMinutes(5);

    private const int MinimumEstimateMinutes = 1;

    public static int Estimate(TimeSpan? averageWait, int waitingCount, int availableStaff)
    {
        // Số phiên đang chờ không thể âm. Báo lỗi ngay để tin báo cho khách không hiện số vô lý.
        if (waitingCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(waitingCount), waitingCount, $"Số phiên đang chờ không được âm, nhưng nhận được {waitingCount}.");
        }

        // Thời gian chờ trung bình âm là số liệu đo sai. Báo lỗi để khách không thấy số phút vô lý.
        if (averageWait < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(averageWait), averageWait, "Thời gian chờ trung bình không được âm.");
        }

        // Chưa có dữ liệu thì dùng giá trị mặc định cho mỗi phiên.
        var waitPerSession = averageWait ?? DefaultWaitPerSession;

        // Không có nhân viên sẵn sàng thì tính như một người. Phép chia không bao giờ chia cho 0.
        var effectiveStaff = Math.Max(availableStaff, 1);

        // Khách mới đứng sau mọi phiên đang chờ, nên số phiên cần phục vụ là số phiên chờ cộng 1.
        long sessionsToServe = (long)waitingCount + 1;

        // Dùng `decimal` và nhân trước khi chia. Cách này tránh sai số của `double` làm kết quả đúng 2 phút bị làm tròn lên thành 3.
        decimal totalTicks = (decimal)waitPerSession.Ticks * sessionsToServe;
        decimal estimatedMinutes = totalTicks / (TimeSpan.TicksPerMinute * (decimal)effectiveStaff);

        // Làm tròn lên để không hứa với khách thời gian ngắn hơn thực tế.
        var roundedUpMinutes = (int)Math.Ceiling(estimatedMinutes);

        // Tin "khoảng 0 phút" làm khách hiểu nhầm là có người trả lời ngay.
        return Math.Max(roundedUpMinutes, MinimumEstimateMinutes);
    }
}
