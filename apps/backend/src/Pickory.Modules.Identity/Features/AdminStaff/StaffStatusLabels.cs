namespace Pickory.Modules.Identity.Features.AdminStaff;

// Nhãn trạng thái nhân viên. Bộ lọc `status` và trường `status` của mỗi dòng dùng chung ba chuỗi này, nên hai luật không lệch nhau.
public static class StaffStatusLabels
{
    public const string Active = "active";
    public const string Invited = "invited";
    public const string Locked = "locked";
}
