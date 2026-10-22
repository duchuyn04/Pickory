namespace Pickory.Modules.SupportDesk.Entities;

// Cấu hình CSKH, bảng chỉ có một dòng. Giờ mở và đóng cửa tính theo giờ Việt Nam, ngày nghỉ là các ngày lịch Việt Nam.
internal sealed class SupportSettings
{
    public string Id { get; set; } = string.Empty;
    public TimeOnly OpensAt { get; set; }
    public TimeOnly ClosesAt { get; set; }
    public DateOnly[] Holidays { get; set; } = [];
    public int WaitNudgeMinutes { get; set; }
    public int AutoCloseMinutes { get; set; }
    public DateTime UpdatedAt { get; set; }
}
