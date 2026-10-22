namespace Pickory.Modules.SupportDesk.Entities;

// Trạng thái trực hiện tại của một nhân viên. Mỗi nhân viên có đúng một dòng, nên `StaffId` là khóa chính.
internal sealed class StaffPresence
{
    public string StaffId { get; set; } = string.Empty;
    public StaffPresenceStatus Status { get; set; }
    public DateTime LastSeenAt { get; set; }
}
