using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Persistence;
using Pickory.Shared.Events;

namespace Pickory.Modules.Identity.Consent;

// Khi khách đăng nhập, gộp lựa chọn consent của mã ẩn danh vào tài khoản. Thiết bị khác của người dùng thấy lựa chọn này.
public sealed class ConsentLinkHandler(IdentityDbContext database) : IDomainEventHandler<UserLinkedEvent>
{
    public async Task HandleAsync(UserLinkedEvent @event, CancellationToken cancellationToken = default)
    {
        var latestGuest = await FindLatestAsync(database.ConsentRecords.Where(record => record.AnonymousId == @event.AnonymousId), cancellationToken);
        var latestUser = await FindLatestAsync(database.ConsentRecords.Where(record => record.UserId == @event.UserId), cancellationToken);

        // Chỉ ghi khi lựa chọn của khách mới hơn. Lựa chọn mới hơn của tài khoản không bị ghi đè.
        if (latestGuest is null || (latestUser is not null && latestGuest.CreatedAt <= latestUser.CreatedAt))
        {
            return;
        }

        // Dùng cùng quy tắc chọn bản mới nhất với bản Node. Ở nhánh này, lựa chọn của khách luôn thắng.
        var resolved = ConsentResolver.ResolveLatest(ToSnapshot(latestGuest), latestUser is null ? null : ToSnapshot(latestUser));

        // Giữ bản chính sách gốc của lựa chọn. Người dùng chưa đồng ý theo bản chính sách hiện hành.
        ConsentRecorder.Add(
            database,
            @event.AnonymousId,
            @event.UserId,
            resolved.Personalization,
            resolved.Marketing,
            latestGuest.PolicyVersion);
        await database.SaveChangesAsync(cancellationToken);
    }

    private static ConsentSnapshot ToSnapshot(ConsentRecord record)
    {
        return new ConsentSnapshot(record.Personalization, record.Marketing, record.CreatedAt);
    }

    private static Task<ConsentRecord?> FindLatestAsync(IQueryable<ConsentRecord> records, CancellationToken cancellationToken)
    {
        return records.AsNoTracking().OrderByDescending(record => record.CreatedAt).FirstOrDefaultAsync(cancellationToken);
    }
}
