using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Identity.Contracts.Authorization;

// Ba quy tắc chặn request theo actor. Thông báo lỗi giữ nguyên bản Node vì frontend hiển thị trực tiếp.
public static class ActorAuthorization
{
    public static void EnsureAuthenticated(Actor actor)
    {
        // Trả 401 vì API chưa biết người gửi là ai.
        if (!actor.IsAuthenticated)
        {
            throw new AppException("UNAUTHORIZED", 401, "Vui lòng đăng nhập để tiếp tục.");
        }
    }

    public static void EnsureStaff(Actor actor)
    {
        EnsureAuthenticated(actor);
        // Trả 403 vì API đã biết người gửi, nhưng người này không được vào khu vực nhân viên.
        if (actor.Kind != ActorKind.Staff)
        {
            throw new AppException("FORBIDDEN", 403, "Bạn không có quyền truy cập khu vực này.");
        }
    }

    public static void EnsurePermissions(Actor actor, IReadOnlyCollection<string> requiredPermissions)
    {
        EnsureAuthenticated(actor);
        // Danh sách nhiều quyền nghĩa là cần đủ mọi quyền. Thiếu một quyền thì API từ chối.
        foreach (var permission in requiredPermissions)
        {
            if (!actor.HasPermission(permission))
            {
                throw new AppException("FORBIDDEN", 403, "Bạn không có đủ quyền thực hiện thao tác này.");
            }
        }
    }
}
