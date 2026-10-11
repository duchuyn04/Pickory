namespace Pickory.Modules.Shipping.Ghn;

// Đổi thời điểm giao dự kiến của GHN sang ngày theo giờ Việt Nam.
internal static class GhnLeadTimeDate
{
    // Offset cố định +07:00. Giờ của máy chủ không được ảnh hưởng ngày giao mà khách thấy.
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static DateOnly ToVietnamDate(long unixSeconds)
    {
        // GHN trả số giây Unix (UTC). Cộng offset trước khi lấy ngày để thời điểm gần nửa đêm không lệch một ngày.
        var vietnamTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToOffset(VietnamOffset);
        return DateOnly.FromDateTime(vietnamTime.DateTime);
    }
}
