using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;

namespace Pickory.Modules.Commerce.Orders;

// Khóa dòng đơn trong transaction đang mở. Hủy, xác nhận và đánh dấu hóa đơn cùng dùng một cách khóa.
internal static class OrderRowLock
{
    // Phải gọi sau khi đã mở transaction. Khóa giữ đến khi transaction kết thúc.
    public static async Task<Order> LoadForUpdateAsync(
        CommerceDbContext database, string orderId, CancellationToken cancellationToken)
    {
        // Khóa dòng bằng SELECT FOR UPDATE để các thao tác cùng đơn xếp hàng, không chạy song song.
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "id" FROM orders WHERE "id" = {orderId} FOR UPDATE""", cancellationToken);

        // Đọc lại sau khi giữ khóa để thấy trạng thái mà thao tác trước vừa commit.
        return await database.Orders
            .Include(order => order.Items)
            .Include(order => order.StatusHistory)
            .SingleAsync(order => order.Id == orderId, cancellationToken);
    }
}
