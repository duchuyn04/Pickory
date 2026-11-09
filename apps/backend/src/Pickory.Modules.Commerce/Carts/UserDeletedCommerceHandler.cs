using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Shared.Events;

namespace Pickory.Modules.Commerce.Carts;

// Khi khách xóa tài khoản, xóa giỏ và sổ địa chỉ của người đó, và ẩn danh đánh giá cùng đơn hàng.
// Đánh giá và đơn giữ nguyên nội dung, chỉ mất liên kết tới tài khoản.
public sealed class UserDeletedCommerceHandler(CommerceDbContext database) : IDomainEventHandler<UserDeletedEvent>
{
    public async Task HandleAsync(UserDeletedEvent @event, CancellationToken cancellationToken = default)
    {
        // Mọi thay đổi chung một transaction. Không để lại giỏ mà đã mất địa chỉ, hoặc đơn đã ẩn danh mà đánh giá còn tên.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Khóa ngoại `cart_items_cartId_fkey` xóa theo dòng giỏ. Người dùng không có giỏ thì lệnh không xóa dòng nào.
        await database.Carts
            .Where(cart => cart.UserId == @event.UserId)
            .ExecuteDeleteAsync(cancellationToken);

        // Bỏ qua bộ lọc xóa mềm. Xóa tài khoản phải xóa cả địa chỉ khách đã tự xóa trước đó để không giữ dữ liệu cá nhân.
        await database.SavedAddresses
            .IgnoreQueryFilters()
            .Where(address => address.UserId == @event.UserId)
            .ExecuteDeleteAsync(cancellationToken);

        // Đánh giá giữ nguyên nội dung và trạng thái, chỉ bỏ liên kết tới người viết.
        // Đánh giá không còn `UserId` hiện tên chung "Khách hàng" ở trang sản phẩm và hàng chờ.
        await database.Reviews
            .Where(review => review.UserId == @event.UserId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(review => review.UserId, (string?)null), cancellationToken);

        // Đơn hàng giữ nguyên cho nghĩa vụ kế toán, kể cả thông tin liên hệ, chỉ bỏ liên kết tới tài khoản.
        // Đơn thành đơn vãng lai nên link xem đơn trong email của khách vẫn mở được.
        await database.Orders
            .Where(order => order.UserId == @event.UserId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.UserId, (string?)null), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
