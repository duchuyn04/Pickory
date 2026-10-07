namespace Pickory.Modules.Catalog.Images;

// Khoảng chờ trước lần thử xóa ảnh tiếp theo, tính theo số lần đã lỗi.
public static class ImageDeletionBackoff
{
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromHours(6);

    public static TimeSpan DelayAfter(int failedAttempts)
    {
        // Hàm chỉ được gọi sau ít nhất một lần lỗi. Giá trị khác là lỗi lập trình.
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempts, 1);
        // Giới hạn số mũ trước khi tính, để số lần lỗi lớn không làm tràn số.
        var exponent = Math.Min(failedAttempts - 1, 20);
        var delay = TimeSpan.FromMinutes(Math.Pow(2, exponent));
        // Trần 6 giờ giữ job được thử lại trong ngày, kể cả khi Cloudinary lỗi lâu.
        return delay < MaximumDelay ? delay : MaximumDelay;
    }
}
