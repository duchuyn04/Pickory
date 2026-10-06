using Pickory.Modules.Identity.Contracts.Authorization;

namespace Pickory.Modules.Identity.Actors;

// Tính quyền hiệu lực của một người từ các role người đó giữ.
public static class ActorPermissions
{
    public const string AdminRoleCode = "admin";

    public static IReadOnlyList<string> Merge(IReadOnlyCollection<string> roleCodes, IEnumerable<string> storedPermissions)
    {
        // Admin có mọi quyền trong danh mục. Quyền mới thêm vào code có hiệu lực cho admin ngay.
        if (roleCodes.Contains(AdminRoleCode, StringComparer.Ordinal))
        {
            return Permissions.All;
        }

        // Giữ thứ tự gặp đầu tiên như `Set` của bản Node. Mỗi quyền chỉ xuất hiện một lần.
        var mergedPermissions = new List<string>();
        foreach (var permission in storedPermissions)
        {
            // Quyền đã bị xóa khỏi code có thể còn trong DB. Quyền đó không được có hiệu lực.
            if (!Permissions.IsKnown(permission) || mergedPermissions.Contains(permission, StringComparer.Ordinal))
            {
                continue;
            }

            mergedPermissions.Add(permission);
        }

        return mergedPermissions;
    }
}
