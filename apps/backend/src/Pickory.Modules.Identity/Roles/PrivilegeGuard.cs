using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Identity.Roles;

// Chống leo thang quyền trong trang quản trị. Người không phải admin chỉ thao tác trong phạm vi quyền của chính mình.
public static class PrivilegeGuard
{
    public static bool IsAdmin(Actor actor)
    {
        return actor.Roles.Contains(DefaultRoles.AdminCode, StringComparer.Ordinal);
    }

    // Admin luôn qua, còn người khác thiếu một quyền thì API từ chối.
    // `buildMessage` nhận tên quyền bị thiếu, vì mỗi thao tác có thông báo riêng giống bản Node.
    public static void EnsureActorHolds(Actor actor, IEnumerable<string> permissions, Func<string, string> buildMessage)
    {
        if (IsAdmin(actor))
        {
            return;
        }

        foreach (var permission in permissions)
        {
            if (!actor.HasPermission(permission))
            {
                throw new AppException("FORBIDDEN", 403, buildMessage(permission));
            }
        }
    }
}
