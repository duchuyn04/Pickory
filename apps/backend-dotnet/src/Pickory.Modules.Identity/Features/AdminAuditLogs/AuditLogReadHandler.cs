using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Persistence;
using Pickory.Shared.Http;

namespace Pickory.Modules.Identity.Features.AdminAuditLogs;

// Đọc nhật ký thao tác theo bộ lọc. Không có thao tác sửa hoặc xóa nhật ký.
public sealed class AuditLogReadHandler(IdentityDbContext database)
{
    public async Task<PagedResponse<AuditLogItemResponse>> ListAsync(AuditLogQuery query, CancellationToken cancellationToken)
    {
        // Chỉ thêm điều kiện cho tham số có gửi lên.
        var auditLogs = database.AuditLogs.AsNoTracking();
        if (!string.IsNullOrEmpty(query.ActorId))
        {
            auditLogs = auditLogs.Where(log => log.ActorId == query.ActorId);
        }

        if (!string.IsNullOrEmpty(query.Action))
        {
            auditLogs = auditLogs.Where(log => log.Action == query.Action);
        }

        if (!string.IsNullOrEmpty(query.TargetType))
        {
            auditLogs = auditLogs.Where(log => log.TargetType == query.TargetType);
        }

        // Khoảng thời gian gồm cả hai đầu mút, giống `gte` và `lte` của bản Node.
        if (query.StartDate is { } startDate)
        {
            auditLogs = auditLogs.Where(log => log.CreatedAt >= startDate);
        }

        if (query.EndDate is { } endDate)
        {
            auditLogs = auditLogs.Where(log => log.CreatedAt <= endDate);
        }

        var total = await auditLogs.CountAsync(cancellationToken);
        // Mới nhất lên đầu. Người xem thường quan tâm thao tác gần đây.
        var logs = await auditLogs
            .OrderByDescending(log => log.CreatedAt)
            .Skip(query.Page.Skip)
            .Take(query.Page.Limit)
            .ToListAsync(cancellationToken);

        // Tra email và tên một lần cho cả trang, không truy vấn riêng cho từng dòng.
        var actorIds = logs.Select(log => log.ActorId).Distinct().ToList();
        var actors = await database.Users
            .AsNoTracking()
            .Where(user => actorIds.Contains(user.Id))
            .Select(user => new { user.Id, user.Email, user.Name })
            .ToDictionaryAsync(user => user.Id, cancellationToken);

        var items = logs.Select(log =>
        {
            // Người thực hiện không còn trong DB thì email và tên là null. Giao diện hiển thị phương án dự phòng.
            actors.TryGetValue(log.ActorId, out var actor);
            return new AuditLogItemResponse(
                log.Id,
                log.ActorId,
                actor?.Email,
                actor?.Name,
                log.Action,
                log.TargetType,
                log.TargetId,
                log.Before?.RootElement.Clone(),
                log.After?.RootElement.Clone(),
                log.CreatedAt);
        }).ToList();

        return new PagedResponse<AuditLogItemResponse>(items, total, query.Page.Page, query.Page.Limit);
    }
}
