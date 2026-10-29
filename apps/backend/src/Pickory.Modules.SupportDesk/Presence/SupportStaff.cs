using Microsoft.AspNetCore.Http;
using Pickory.Modules.Identity.Contracts.Actors;

namespace Pickory.Modules.SupportDesk.Presence;

// Đọc mã nhân viên từ người gọi. Mã này là khóa của dòng `staff_presence`.
internal static class SupportStaff
{
    public static string RequireStaffId(HttpContext context)
    {
        // Route đã qua `RequirePermission` nên người gọi luôn đăng nhập. Thiếu mã ở đây là lỗi cấu hình route, phải dừng request.
        return context.GetActor().UserId
            ?? throw new InvalidOperationException("Route CSKH được gọi mà người gọi chưa đăng nhập.");
    }
}
