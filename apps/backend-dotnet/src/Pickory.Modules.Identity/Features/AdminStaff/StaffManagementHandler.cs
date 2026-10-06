using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Audit;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.EmailTokens;
using Pickory.Modules.Identity.Emails;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Configuration;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.Identity.Features.AdminStaff;

// Danh sách, mời, gán role, khóa và mở khóa nhân viên cho trang quản trị.
public sealed class StaffManagementHandler(
    IdentityDbContext database,
    IEmailSender emailSender,
    AppConfig config,
    TimeProvider timeProvider)
{
    private const string StaffNotFoundMessage = "Không tìm thấy tài khoản nhân viên.";

    public async Task<IReadOnlyList<StaffListItemResponse>> ListAsync(CancellationToken cancellationToken)
    {
        // Nhân viên mới nhất lên đầu, giống bản Node.
        var staff = await database.Users
            .AsNoTracking()
            .Include(user => user.Roles)
            .ThenInclude(userRole => userRole.Role)
            .Where(user => user.Kind == UserKind.Staff)
            .OrderByDescending(user => user.CreatedAt)
            .ToListAsync(cancellationToken);

        // Chọn từng trường thay vì trả cả entity. Hash mật khẩu không bao giờ rời API.
        return staff
            .Select(user => new StaffListItemResponse(
                user.Id,
                user.Email,
                user.Name,
                user.Phone,
                user.EmailVerified,
                UserSummaryResponse.StaffKind,
                user.LockedAt,
                user.CreatedAt,
                user.Roles.Select(userRole => userRole.Role!.Code).ToList()))
            .ToList();
    }

    public async Task<InvitedStaffResponse> InviteAsync(Actor actor, InviteStaffRequest request, CancellationToken cancellationToken)
    {
        // Chuẩn hóa email trước khi tra cứu. Email viết hoa và viết thường thuộc cùng một tài khoản.
        var email = UserInputNormalizer.NormalizeEmail(request.Email!);
        // Khử role trùng. Số role tìm được phải so khớp đúng số role yêu cầu ở bước sau.
        var roleCodes = request.RoleCodes!.Distinct(StringComparer.Ordinal).ToList();

        // Tài khoản khách và tài khoản nhân viên tách riêng. Nhân viên không mang role `customer`.
        if (roleCodes.Contains(DefaultRoles.CustomerCode))
        {
            throw new AppException("INVALID_ROLE", 400, "Không thể gán role customer cho tài khoản nhân viên.");
        }

        // Admin có mọi quyền. Chỉ admin mới tạo thêm được admin.
        if (roleCodes.Contains(DefaultRoles.AdminCode) && !PrivilegeGuard.IsAdmin(actor))
        {
            throw new AppException("FORBIDDEN", 403, "Chỉ quản trị viên (admin) mới có quyền mời tài khoản làm admin.");
        }

        // Người gọi là admin đã đăng nhập, không phải người lạ. Hai trường hợp nhận hai thông báo rõ ràng.
        var existingUser = await database.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Email == email, cancellationToken);
        if (existingUser is not null)
        {
            var message = existingUser.Kind == UserKind.Customer
                ? "Email này đã thuộc tài khoản khách hàng, không thể mời làm nhân viên."
                : "Email này đã được sử dụng cho một nhân viên khác.";
            throw new AppException("EMAIL_ALREADY_EXISTS", 400, message);
        }

        var roles = await LoadRolesAsync(roleCodes, cancellationToken);
        if (roles.Count != roleCodes.Count)
        {
            throw new AppException("INVALID_ROLE", 400, "Một hoặc nhiều mã role không tồn tại trong hệ thống.");
        }

        // Người không phải admin chỉ mời vào role có quyền họ đang có.
        // Nếu không, họ mời một email do chính họ kiểm soát vào role mạnh hơn.
        PrivilegeGuard.EnsureActorHolds(
            actor,
            PermissionsOf(roles),
            permission => $"Bạn không thể mời nhân viên với role chứa quyền \"{permission}\" vì chính bạn không sở hữu quyền này.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Tài khoản, token và nhật ký lưu trong một giao dịch. Không có nhân viên nào được mời mà thiếu token hoặc dấu vết.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var staff = new User
        {
            Email = email,
            Name = request.Name!.Trim(),
            // Nhân viên chưa có mật khẩu và chưa xác thực email. Nhân viên tự đặt mật khẩu qua link mời.
            EmailVerified = false,
            Kind = UserKind.Staff,
            UpdatedAt = now,
        };
        foreach (var role in roles)
        {
            staff.Roles.Add(new UserRole { User = staff, RoleId = role.Id });
        }

        database.Users.Add(staff);
        var inviteToken = EmailTokenIssuer.Add(database, staff.Id, EmailTokenPurpose.Invite, EmailTokenIssuer.InviteLifetime, now);
        // Nhật ký chỉ có trạng thái sau vì tài khoản chưa tồn tại trước thao tác này.
        AuditRecorder.Add(database, actor.UserId!, "staff.invite", "user", staff.Id, before: null, after: new { email, name = staff.Name, roles = roleCodes });
        await database.SaveChangesAsync(cancellationToken);

        // Gửi thư trước khi xác nhận giao dịch, giống bản Node. Gửi thư lỗi thì không có tài khoản nào được tạo.
        await emailSender.SendAsync(
            new EmailMessage(
                staff.Email,
                "Lời mời tham gia quản trị Pickory",
                $"Chào bạn, bạn đã được mời tham gia đội ngũ quản trị Pickory. Vui lòng thiết lập mật khẩu tại liên kết sau (hạn 72 giờ): {config.WebOrigin}/reset-password?token={inviteToken}",
                new Dictionary<string, string> { ["token"] = inviteToken, ["purpose"] = "invite" }),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new InvitedStaffResponse(staff.Id, staff.Email, staff.Name, roles.Select(role => role.Code).ToList());
    }

    public async Task<AssignedRolesResponse> AssignRolesAsync(Actor actor, string targetUserId, IReadOnlyList<string> requestedRoleCodes, CancellationToken cancellationToken)
    {
        var roleCodes = requestedRoleCodes.Distinct(StringComparer.Ordinal).ToList();

        // Không ai tự sửa role của chính mình, kể cả admin. Người dùng không tự nâng quyền hoặc tự gỡ ràng buộc.
        if (actor.UserId == targetUserId)
        {
            throw new AppException("CANNOT_MODIFY_SELF", 400, "Không thể tự sửa đổi role của chính mình.");
        }

        // Tài khoản khách và tài khoản nhân viên tách riêng. Nhân viên không mang role `customer`.
        if (roleCodes.Contains(DefaultRoles.CustomerCode))
        {
            throw new AppException("INVALID_ROLE", 400, "Không thể gán role customer cho nhân viên.");
        }

        // Admin có mọi quyền. Chỉ admin mới gán được role admin.
        if (roleCodes.Contains(DefaultRoles.AdminCode) && !PrivilegeGuard.IsAdmin(actor))
        {
            throw new AppException("FORBIDDEN", 403, "Chỉ quản trị viên (admin) mới có quyền gán role admin.");
        }

        var newRoles = await LoadRolesAsync(roleCodes, cancellationToken);
        if (newRoles.Count != roleCodes.Count)
        {
            throw new AppException("INVALID_ROLE", 400, "Một hoặc nhiều mã role không hợp lệ.");
        }

        // Người không phải admin chỉ gán được role có quyền họ đang có.
        PrivilegeGuard.EnsureActorHolds(
            actor,
            PermissionsOf(newRoles),
            permission => $"Bạn không thể gán role chứa quyền \"{permission}\" vì chính bạn không sở hữu quyền này.");

        return await SerializableTransactionRunner.RunAsync(database, async () =>
        {
            // Đọc nhân viên trong giao dịch. Dữ liệu dùng để kiểm tra và dữ liệu bị ghi thuộc cùng một thời điểm.
            var target = await LoadStaffAsync(targetUserId, cancellationToken);
            var currentRoleCodes = target.Roles.Select(userRole => userRole.Role!.Code).ToList();
            var isCurrentlyAdmin = currentRoleCodes.Contains(DefaultRoles.AdminCode);

            // Chỉ admin mới sửa được role của admin khác. Người thường không hạ quyền được admin.
            if (isCurrentlyAdmin && !PrivilegeGuard.IsAdmin(actor))
            {
                throw new AppException("FORBIDDEN", 403, "Chỉ quản trị viên (admin) mới có quyền sửa đổi quyền của một admin khác.");
            }

            EnsureActorCanManageTarget(actor, target);

            // Hệ thống luôn còn người quản trị. Admin đang bị khóa đã không được tính là admin hoạt động.
            if (isCurrentlyAdmin && !roleCodes.Contains(DefaultRoles.AdminCode) && target.LockedAt is null
                && await CountActiveAdminsAsync(cancellationToken) <= 1)
            {
                throw new AppException("LAST_ADMIN_PROTECTED", 400, "Không thể gỡ quyền admin của quản trị viên cuối cùng còn hoạt động.");
            }

            // Thay toàn bộ role thay vì so từng role. Cách này đơn giản hơn và cho cùng kết quả.
            database.UserRoles.RemoveRange(target.Roles);
            foreach (var role in newRoles)
            {
                database.UserRoles.Add(new UserRole { UserId = target.Id, RoleId = role.Id });
            }

            // Nhật ký ghi role trước và sau, nên người xem biết role đổi từ gì sang gì.
            AuditRecorder.Add(
                database, actor.UserId!, "staff.roles_update", "user", target.Id, before: new { roles = currentRoleCodes }, after: new { roles = roleCodes });
            await database.SaveChangesAsync(cancellationToken);

            // Nhân viên không còn role nào thì không có lý do giữ phiên. Đổi sang role khác không cần xóa phiên.
            if (newRoles.Count == 0)
            {
                await database.Sessions.Where(session => session.UserId == target.Id).ExecuteDeleteAsync(cancellationToken);
            }

            return new AssignedRolesResponse(target.Id, roleCodes);
        }, cancellationToken);
    }

    public async Task LockAsync(Actor actor, string targetUserId, CancellationToken cancellationToken)
    {
        // Không ai tự khóa chính mình. Người dùng không vô tình mất quyền truy cập.
        if (actor.UserId == targetUserId)
        {
            throw new AppException("CANNOT_MODIFY_SELF", 400, "Không thể tự khóa tài khoản của chính mình.");
        }

        await SerializableTransactionRunner.RunAsync(database, async () =>
        {
            var target = await LoadStaffAsync(targetUserId, cancellationToken);
            var isCurrentlyAdmin = target.Roles.Any(userRole => userRole.Role!.Code == DefaultRoles.AdminCode);

            // Chỉ admin mới khóa được admin khác.
            if (isCurrentlyAdmin && !PrivilegeGuard.IsAdmin(actor))
            {
                throw new AppException("FORBIDDEN", 403, "Chỉ quản trị viên (admin) mới có quyền khóa tài khoản của một admin.");
            }

            // Kiểm tra quyền trước nhánh "đã khóa". Người không đủ quyền không dò được trạng thái khóa.
            EnsureActorCanManageTarget(actor, target);

            // Đã khóa thì không ghi thêm nhật ký và không đổi thời điểm khóa. Gọi lại nhiều lần cho cùng kết quả.
            if (target.LockedAt is not null)
            {
                return;
            }

            // Khóa admin cuối cùng làm hệ thống mất người quản trị.
            if (isCurrentlyAdmin && await CountActiveAdminsAsync(cancellationToken) <= 1)
            {
                throw new AppException("LAST_ADMIN_PROTECTED", 400, "Không thể khóa quản trị viên cuối cùng còn hoạt động.");
            }

            // DB và nhật ký dùng chung một thời điểm, nên hai nơi khớp nhau.
            var now = timeProvider.GetUtcNow().UtcDateTime;
            target.LockedAt = now;
            target.UpdatedAt = now;
            AuditRecorder.Add(database, actor.UserId!, "staff.lock", "user", target.Id, before: new { lockedAt = (DateTime?)null }, after: new { lockedAt = now });
            await database.SaveChangesAsync(cancellationToken);

            // Xóa phiên để người bị khóa mất quyền ngay, không phải chờ phiên 12 giờ hết hạn.
            await database.Sessions.Where(session => session.UserId == target.Id).ExecuteDeleteAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task UnlockAsync(Actor actor, string targetUserId, CancellationToken cancellationToken)
    {
        await SerializableTransactionRunner.RunAsync(database, async () =>
        {
            var target = await LoadStaffAsync(targetUserId, cancellationToken);

            // Chỉ admin mới mở khóa được admin khác, cùng lý do với khóa.
            if (target.Roles.Any(userRole => userRole.Role!.Code == DefaultRoles.AdminCode) && !PrivilegeGuard.IsAdmin(actor))
            {
                throw new AppException("FORBIDDEN", 403, "Chỉ quản trị viên (admin) mới có quyền mở khóa tài khoản của một admin.");
            }

            EnsureActorCanManageTarget(actor, target);

            // Không cần xóa phiên. Lúc khóa, API đã xóa mọi phiên của nhân viên này.
            var previousLockedAt = target.LockedAt;
            target.LockedAt = null;
            target.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
            AuditRecorder.Add(database, actor.UserId!, "staff.unlock", "user", target.Id, before: new { lockedAt = previousLockedAt }, after: new { lockedAt = (DateTime?)null });
            await database.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    private async Task<List<Role>> LoadRolesAsync(IReadOnlyList<string> roleCodes, CancellationToken cancellationToken)
    {
        // Lấy kèm quyền của từng role cho bước chống leo thang quyền.
        return await database.Roles
            .AsNoTracking()
            .Include(role => role.Permissions)
            .Where(role => roleCodes.Contains(role.Code))
            .ToListAsync(cancellationToken);
    }

    private async Task<User> LoadStaffAsync(string targetUserId, CancellationToken cancellationToken)
    {
        var target = await database.Users
            .Include(user => user.Roles)
            .ThenInclude(userRole => userRole.Role!)
            .ThenInclude(role => role.Permissions)
            .SingleOrDefaultAsync(user => user.Id == targetUserId, cancellationToken);

        // Chỉ nhân viên mới có role để quản lý. Id của khách cũng nhận lỗi không tìm thấy.
        if (target is null || target.Kind != UserKind.Staff)
        {
            throw new AppException("USER_NOT_FOUND", 404, StaffNotFoundMessage);
        }

        return target;
    }

    // Admin bị khóa hoặc bị xóa không còn là admin hoạt động.
    private Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken)
    {
        return database.Users.CountAsync(
            user => user.Kind == UserKind.Staff
                && user.LockedAt == null
                && user.DeletedAt == null
                && user.Roles.Any(userRole => userRole.Role!.Code == DefaultRoles.AdminCode),
            cancellationToken);
    }

    // Người không phải admin chỉ quản lý nhân viên có quyền nằm trong quyền của mình.
    // Quyền được sắp xếp, nên thông báo lỗi luôn nêu cùng một quyền bị thiếu.
    private static void EnsureActorCanManageTarget(Actor actor, User target)
    {
        PrivilegeGuard.EnsureActorHolds(
            actor,
            target.Roles.SelectMany(userRole => userRole.Role!.Permissions.Select(rolePermission => rolePermission.Permission)).Order(StringComparer.Ordinal),
            permission => $"Bạn không có quyền quản lý nhân viên sở hữu quyền \"{permission}\".");
    }

    // Sắp xếp quyền vì DB không bảo đảm thứ tự dòng. Thông báo lỗi luôn nêu cùng một quyền bị thiếu.
    private static IEnumerable<string> PermissionsOf(IEnumerable<Role> roles)
    {
        return roles.SelectMany(role => role.Permissions.Select(rolePermission => rolePermission.Permission)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
    }
}
