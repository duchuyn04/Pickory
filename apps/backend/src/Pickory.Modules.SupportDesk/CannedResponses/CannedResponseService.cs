using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.SupportDesk.Persistence.Configurations;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.SupportDesk.CannedResponses;

// Đọc và sửa mẫu trả lời nhanh. Route chỉ chặn người thiếu cả hai quyền, còn quyền cụ thể kiểm ở đây theo loại mẫu.
internal sealed class CannedResponseService(
    SupportDeskDbContext database,
    TimeProvider timeProvider,
    IAuditLogWriter auditLogWriter)
{
    private const string CreateAuditAction = "support.canned_response.create";
    private const string UpdateAuditAction = "support.canned_response.update";
    private const string DeleteAuditAction = "support.canned_response.delete";
    private const string AuditTargetType = "support_canned_response";

    public const string ForbiddenMessage = "Bạn không có đủ quyền thực hiện thao tác này.";
    private const string NotFoundMessage = "Không tìm thấy mẫu trả lời nhanh.";
    private const string PersonalShortcutTakenMessage = "Bạn đã có mẫu dùng phím tắt này.";
    private const string SharedShortcutTakenMessage = "Đã có mẫu dùng chung dùng phím tắt này.";

    public async Task<IReadOnlyList<CannedResponseItem>> ListAsync(Actor actor, CancellationToken cancellationToken)
    {
        var ownerId = RequireUserId(actor);
        // Người có `conversations:reply` đọc được mẫu cá nhân của mình. Người chỉ có `support-settings:manage` không thấy mẫu cá nhân, kể cả mẫu cũ của chính mình.
        var canReadPersonal = actor.HasPermission(Permissions.ConversationsReply);
        // Trả mọi mẫu dùng chung, cộng mẫu cá nhân của người gọi khi được phép. Mẫu cá nhân của người khác không bao giờ vào kết quả.
        var rows = await database.CannedResponses.AsNoTracking()
            .Where(row => row.Shared || (canReadPersonal && row.OwnerId == ownerId))
            .ToListAsync(cancellationToken);

        // Sắp theo `shortcut` bằng so sánh thứ tự ký tự. Collation của DB xếp `-` và `_` không ổn định giữa môi trường.
        return rows
            .OrderBy(row => row.Shortcut, StringComparer.Ordinal)
            .ThenBy(row => row.Id, StringComparer.Ordinal)
            .Select(row => CannedResponseItem.From(row, IsEditable(row, actor)))
            .ToList();
    }

    public async Task<CannedResponseItem> CreateAsync(
        Actor actor, string shortcut, string body, bool shared, CancellationToken cancellationToken)
    {
        var userId = RequireUserId(actor);
        // Quyền theo loại mẫu của body. Thiếu quyền trả 403 trước khi chạm DB.
        RequireWritePermission(actor, shared);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var row = new CannedResponse
        {
            // Mẫu dùng chung không thuộc nhân viên nào, nên `OwnerId` để rỗng.
            OwnerId = shared ? null : userId,
            Shortcut = shortcut,
            Body = body,
            Shared = shared,
            CreatedAt = now,
            UpdatedAt = now,
        };
        database.CannedResponses.Add(row);

        await SaveAsync(
            actor, shared, CreateAuditAction, row, before: null, after: ToAuditSnapshot(row), cancellationToken);
        return CannedResponseItem.From(row, editable: true);
    }

    public async Task<CannedResponseItem> UpdateAsync(
        Actor actor, string id, string? shortcut, string? body, CancellationToken cancellationToken)
    {
        var row = await FindWritableAsync(actor, id, cancellationToken);
        var before = ToAuditSnapshot(row);

        // Chỉ ghi trường client gửi. Trường không gửi giữ nguyên giá trị cũ.
        if (shortcut is not null)
        {
            row.Shortcut = shortcut;
        }

        if (body is not null)
        {
            row.Body = body;
        }

        row.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await SaveAsync(actor, row.Shared, UpdateAuditAction, row, before, ToAuditSnapshot(row), cancellationToken);
        return CannedResponseItem.From(row, editable: true);
    }

    public async Task DeleteAsync(Actor actor, string id, CancellationToken cancellationToken)
    {
        var row = await FindWritableAsync(actor, id, cancellationToken);
        var before = ToAuditSnapshot(row);
        database.CannedResponses.Remove(row);

        await SaveAsync(actor, row.Shared, DeleteAuditAction, row, before, after: null, cancellationToken);
    }

    // Tìm mẫu người gọi được phép sửa. Điều kiện `OwnerId` nằm ngay trong truy vấn,
    // nên mẫu cá nhân của người khác và mẫu không tồn tại cùng nhận 404, không lộ sự tồn tại.
    private async Task<CannedResponse> FindWritableAsync(Actor actor, string id, CancellationToken cancellationToken)
    {
        var userId = RequireUserId(actor);
        var row = await database.CannedResponses
            .Where(candidate => candidate.Id == id && (candidate.Shared || candidate.OwnerId == userId))
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            throw new AppException(SupportDeskErrorCodes.CannedResponseNotFound, 404, NotFoundMessage);
        }

        // Quyền kiểm sau khi biết loại mẫu. Mẫu dùng chung cần quyền khác mẫu cá nhân.
        RequireWritePermission(actor, row.Shared);
        return row;
    }

    // Lưu thay đổi. Mẫu dùng chung ghi audit cùng transaction, mẫu cá nhân chỉ lưu dữ liệu.
    private async Task SaveAsync(
        Actor actor,
        bool shared,
        string auditAction,
        CannedResponse row,
        object? before,
        object? after,
        CancellationToken cancellationToken)
    {
        // Mẫu cá nhân không ghi audit, nên một lệnh lưu đã đủ nguyên tử.
        if (!shared)
        {
            await SaveTranslatingConflictAsync(row, shared, cancellationToken);
            return;
        }

        // Dữ liệu và audit chung một transaction. Lỗi giữa chừng không để lại mẫu mới mà thiếu dấu vết.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await SaveTranslatingConflictAsync(row, shared, cancellationToken);
        var entry = new AuditLogEntry(actor.UserId!, auditAction, AuditTargetType, row.Id, before, after);
        await auditLogWriter.WriteAsync(entry, transaction.GetDbTransaction(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // Hai request cùng ghi một `shortcut` sẽ đụng chỉ mục duy nhất của Postgres (`23505`).
    // Request thua nhận 409 thay vì lỗi 500.
    private async Task SaveTranslatingConflictAsync(CannedResponse row, bool shared, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        // Chỉ hai chỉ mục phím tắt mới đổi thành 409. Vi phạm của chỉ mục khác là lỗi thật, nên đi tiếp thành lỗi 500.
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: CannedResponseConfiguration.PersonalShortcutIndexName
                    or CannedResponseConfiguration.SharedShortcutIndexName,
            })
        {
            var message = shared ? SharedShortcutTakenMessage : PersonalShortcutTakenMessage;
            throw new AppException(SupportDeskErrorCodes.CannedShortcutTaken, 409, message);
        }
        // Request khác đã xóa dòng giữa lúc đọc và ghi, nên `UPDATE` hoặc `DELETE` không chạm dòng nào. Kết quả đúng là không có mẫu.
        catch (DbUpdateConcurrencyException)
        {
            throw new AppException(SupportDeskErrorCodes.CannedResponseNotFound, 404, NotFoundMessage);
        }
    }

    // Mẫu cá nhân hiện cho chủ sửa. Mẫu dùng chung chỉ sửa được khi người gọi có `support-settings:manage`.
    private static bool IsEditable(CannedResponse row, Actor actor) =>
        !row.Shared || actor.HasPermission(Permissions.SupportSettingsManage);

    private static void RequireWritePermission(Actor actor, bool shared)
    {
        var permission = shared ? Permissions.SupportSettingsManage : Permissions.ConversationsReply;
        if (!actor.HasPermission(permission))
        {
            throw new AppException(SupportDeskErrorCodes.Forbidden, 403, ForbiddenMessage);
        }
    }

    // Route đã qua bộ lọc đăng nhập nên `UserId` luôn có. Thiếu là lỗi cấu hình route, phải dừng request.
    private static string RequireUserId(Actor actor) =>
        actor.UserId ?? throw new InvalidOperationException("Route mẫu trả lời nhanh được gọi mà người gọi chưa đăng nhập.");

    private static object ToAuditSnapshot(CannedResponse row) =>
        new { shortcut = row.Shortcut, body = row.Body, shared = row.Shared };
}
