using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Shared.Events;

namespace Pickory.Modules.Commerce.Orders;

// Khi email của khách được xác thực, gắn các đơn vãng lai cùng email vào tài khoản đó.
// Chỉ chủ email bấm được link xác thực, nên việc gắn đơn không cho người lạ xem đơn của người khác.
public sealed class GuestOrderLinkHandler(CommerceDbContext database) : IDomainEventHandler<EmailVerifiedEvent>
{
    public async Task HandleAsync(EmailVerifiedEvent @event, CancellationToken cancellationToken = default)
    {
        // Một lệnh UPDATE có điều kiện thay cho đọc rồi lưu từng đơn, để hai sự kiện đồng thời không tranh một đơn.
        // Lệnh không đổi `accessTokenHash` và `status`, nên link xem đơn cũ vẫn dùng được.
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE orders
            SET "userId" = {@event.UserId}
            -- Chỉ đơn chưa có chủ. Đơn đã gắn tài khoản khác không đổi, kể cả khi cùng email.
            WHERE "userId" IS NULL
              -- Hai phía cùng qua lower(btrim(...)), nên email khác hoa thường hoặc có dấu cách hai đầu vẫn khớp.
              AND lower(btrim(contact ->> 'email')) = lower(btrim({@event.Email}))
            """,
            cancellationToken);
    }
}
