using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Shared.Events;

namespace Pickory.Modules.Commerce.Carts;

// Khi khách xóa tài khoản, xóa giỏ và sổ địa chỉ của người đó. Đơn hàng và đánh giá thuộc COM-21 nên không bị đụng tới.
public sealed class UserDeletedCommerceHandler(CommerceDbContext database) : IDomainEventHandler<UserDeletedEvent>
{
    public async Task HandleAsync(UserDeletedEvent @event, CancellationToken cancellationToken = default)
    {
        // Hai lệnh xóa chung một transaction. Không để lại giỏ mà đã mất địa chỉ, hoặc ngược lại.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Khóa ngoại `cart_items_cartId_fkey` xóa theo dòng giỏ. Người dùng không có giỏ thì lệnh không xóa dòng nào.
        await database.Carts
            .Where(cart => cart.UserId == @event.UserId)
            .ExecuteDeleteAsync(cancellationToken);

        await database.SavedAddresses
            .Where(address => address.UserId == @event.UserId)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
