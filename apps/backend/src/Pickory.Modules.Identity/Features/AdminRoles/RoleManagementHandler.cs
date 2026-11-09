using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Audit;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Identity.Roles;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Identity.Features.AdminRoles;

// Tạo, sửa, xóa role và đọc danh sách role cho trang quản trị.
public sealed partial class RoleManagementHandler(IdentityDbContext database, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<RoleResponse>> ListAsync(CancellationToken cancellationToken)
    {
        // Theo thứ tự tạo để role tạo sẵn luôn ở trên. `Code` chỉ giữ thứ tự cố định khi hai role trùng thời điểm tạo.
        var roles = await database.Roles
            .AsNoTracking()
            .Include(role => role.Permissions)
            .Include(role => role.Users)
            .OrderBy(role => role.CreatedAt)
            .ThenBy(role => role.Code)
            .ToListAsync(cancellationToken);
        return roles.Select(ToResponse).ToList();
    }

    public async Task<RoleResponse> CreateAsync(Actor actor, CreateRoleRequest request, CancellationToken cancellationToken)
    {
        // Mã chữ thường để `Kho` và `kho` không thành hai role.
        var code = request.Code!.Trim().ToLowerInvariant();
        // Quyền gửi hai lần sẽ trùng khóa chính `(roleId, permission)`. Khử trùng trước để không lỗi 500.
        var permissions = (request.Permissions ?? []).Distinct(StringComparer.Ordinal).ToList();

        // Mã role chỉ có ký tự an toàn. Mã này dùng được trong URL.
        if (!RoleCodePattern().IsMatch(code))
        {
            throw new AppException("INVALID_INPUT", 400, "Mã role chỉ được chứa chữ thường, số, gạch dưới và gạch nối.");
        }

        EnsurePermissionsAreGrantable(actor, permissions);

        // Kiểm tra trùng mã trước. Lỗi 400 dễ hiểu hơn lỗi ràng buộc duy nhất của DB.
        if (await database.Roles.AnyAsync(role => role.Code == code, cancellationToken))
        {
            throw new AppException("ROLE_ALREADY_EXISTS", 400, $"Mã role \"{code}\" đã tồn tại.");
        }

        // Role tạo qua giao diện luôn là role thường. Chỉ role tạo sẵn mới là role hệ thống.
        var role = new Role { Code = code, Name = request.Name!.Trim(), IsSystem = false };
        foreach (var permission in permissions)
        {
            role.Permissions.Add(new RolePermission { Permission = permission });
        }

        database.Roles.Add(role);
        // Role và nhật ký lưu trong cùng một lần. Không có role nào được tạo mà thiếu dấu vết.
        AuditRecorder.Add(database, actor.UserId!, "role.create", "role", role.Id, before: null, after: new { code, name = role.Name, permissions });
        await database.SaveChangesAsync(cancellationToken);
        return ToResponse(role);
    }

    public async Task<RoleResponse> UpdateAsync(Actor actor, string roleId, UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await LoadRoleAsync(roleId, cancellationToken);

        // Role hệ thống giữ nguyên. Quy tắc phân quyền cốt lõi không bị phá.
        if (role.IsSystem)
        {
            throw new AppException("FORBIDDEN", 403, "Không được phép sửa đổi role hệ thống.");
        }

        // Người không phải admin không sửa role mình đang giữ. Họ không tự thêm quyền cho chính mình.
        if (!PrivilegeGuard.IsAdmin(actor) && actor.Roles.Contains(role.Code, StringComparer.Ordinal))
        {
            throw new AppException("CANNOT_MODIFY_SELF_ROLE", 403, "Bạn không thể sửa đổi role mà chính mình đang giữ.");
        }

        var newPermissions = request.Permissions?.Distinct(StringComparer.Ordinal).ToList();
        // Chỉ kiểm tra quyền khi client gửi danh sách quyền. Không gửi nghĩa là chỉ đổi tên.
        if (newPermissions is not null)
        {
            EnsurePermissionsAreGrantable(actor, newPermissions);
        }

        // Giữ trạng thái trước khi sửa. Nhật ký cần cả trạng thái trước và sau.
        var before = new { name = role.Name, permissions = role.Permissions.Select(value => value.Permission).ToList() };

        // Thay toàn bộ quyền thay vì so từng quyền. Cách này đơn giản hơn và cho cùng kết quả.
        if (newPermissions is not null)
        {
            database.RolePermissions.RemoveRange(role.Permissions);
            foreach (var permission in newPermissions)
            {
                database.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = permission });
            }
        }

        if (request.Name is not null)
        {
            role.Name = request.Name.Trim();
        }

        var afterPermissions = (newPermissions ?? before.permissions).Order(StringComparer.Ordinal).ToList();
        // Quyền mới có hiệu lực ngay cho mọi người giữ role. Actor middleware nạp lại quyền ở mỗi request.
        AuditRecorder.Add(database, actor.UserId!, "role.update", "role", role.Id, before, after: new { name = role.Name, permissions = afterPermissions });
        await database.SaveChangesAsync(cancellationToken);

        return new RoleResponse(role.Id, role.Code, role.Name, role.IsSystem, afterPermissions, role.Users.Count);
    }

    public async Task DeleteAsync(Actor actor, string roleId, CancellationToken cancellationToken)
    {
        var role = await LoadRoleAsync(roleId, cancellationToken);

        // Nhiều quy tắc dựa vào role hệ thống, ví dụ admin có mọi quyền. Role hệ thống không xóa được.
        if (role.IsSystem)
        {
            throw new AppException("FORBIDDEN", 403, "Không được phép xóa role hệ thống.");
        }

        // Xóa role đang có người giữ làm họ mất quyền mà không ai biết lý do. Admin phải gỡ role trước.
        if (role.Users.Count > 0)
        {
            throw new AppException("ROLE_IN_USE", 400, $"Không thể xóa role đang có {role.Users.Count} người dùng giữ. Vui lòng gỡ role trước.");
        }

        // Đánh dấu xóa mềm thay vì xóa dòng. Nhật ký kiểm toán và dữ liệu cũ vẫn giữ tham chiếu role.
        var now = timeProvider.GetUtcNow().UtcDateTime;
        role.DeletedAt = now;
        // Nhật ký chỉ có trạng thái trước vì role đã xóa không còn hiển thị trạng thái hiện tại.
        AuditRecorder.Add(
            database,
            actor.UserId!,
            "role.delete",
            "role",
            role.Id,
            before: new { code = role.Code, name = role.Name, permissions = role.Permissions.Select(value => value.Permission).ToList() },
            after: null);
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task<Role> LoadRoleAsync(string roleId, CancellationToken cancellationToken)
    {
        return await database.Roles
            .Include(role => role.Permissions)
            .Include(role => role.Users)
            .SingleOrDefaultAsync(role => role.Id == roleId, cancellationToken)
            ?? throw new AppException("ROLE_NOT_FOUND", 404, "Không tìm thấy role.");
    }

    private static void EnsurePermissionsAreGrantable(Actor actor, IReadOnlyList<string> permissions)
    {
        // API không tin quyền do client gửi. Chỉ quyền có trong danh mục của code mới hợp lệ.
        var unknownPermission = permissions.FirstOrDefault(permission => !Permissions.IsKnown(permission));
        if (unknownPermission is not null)
        {
            throw new AppException("INVALID_PERMISSION", 400, $"Quyền \"{unknownPermission}\" không thuộc danh mục quyền của hệ thống.");
        }

        // Người không phải admin chỉ cấp được quyền mình đang có. Họ không tự nâng quyền qua một role mới.
        PrivilegeGuard.EnsureActorHolds(actor, permissions, permission => $"Bạn không thể cấp quyền \"{permission}\" vì chính bạn không sở hữu quyền này.");
    }

    private static RoleResponse ToResponse(Role role)
    {
        var permissions = role.Permissions.Select(value => value.Permission).Order(StringComparer.Ordinal).ToList();
        return new RoleResponse(role.Id, role.Code, role.Name, role.IsSystem, permissions, role.Users.Count);
    }

    [GeneratedRegex("^[a-z0-9_-]+$")]
    private static partial Regex RoleCodePattern();
}
