namespace Pickory.Modules.SupportDesk.Scheduling;

// Hàm thuần kiểm lịch làm việc. Hàm không xét nhân viên sẵn sàng, vì điều kiện đó thuộc bước quyết định tin trong giờ.
internal static class WorkingHoursCalendar
{
    // Việt Nam dùng UTC+7 cố định và không có giờ mùa hè. Offset cố định để kết quả không phụ thuộc múi giờ của máy chủ.
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static bool IsWithinHours(DateTimeOffset utcNow, WorkingHoursSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Đổi sang giờ Việt Nam một lần. Ngày nghỉ và khung giờ cùng dựa trên một mốc, nên không lệch nhau quanh nửa đêm.
        var vietnamMoment = utcNow.ToOffset(VietnamOffset);
        var vietnamDate = DateOnly.FromDateTime(vietnamMoment.DateTime);
        var vietnamTime = TimeOnly.FromDateTime(vietnamMoment.DateTime);

        // Ngày nghỉ là ngoài giờ cả ngày, kể cả lúc nằm giữa khung giờ.
        if (settings.Holidays.Contains(vietnamDate))
        {
            return false;
        }

        // Khung nửa mở: đúng giờ mở cửa là trong giờ, đúng giờ đóng cửa là ngoài giờ.
        return vietnamTime >= settings.OpensAt && vietnamTime < settings.ClosesAt;
    }
}
