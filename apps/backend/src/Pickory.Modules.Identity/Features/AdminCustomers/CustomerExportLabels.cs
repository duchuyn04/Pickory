namespace Pickory.Modules.Identity.Features.AdminCustomers;

// Nhãn tiếng Việt của trạng thái khách trong file xuất. Nhãn đặt ở một nơi để mọi nơi gọi `ForLockedAt` dùng cùng một chữ.
internal static class CustomerExportLabels
{
    public const string ActiveLabel = "Hoạt động";
    public const string LockedLabel = "Đã khóa";

    // Bảng không có cột trạng thái. Khách có `LockedAt` là đã khóa, còn lại là hoạt động.
    public static string ForLockedAt(DateTime? lockedAt) => lockedAt is null ? ActiveLabel : LockedLabel;
}
