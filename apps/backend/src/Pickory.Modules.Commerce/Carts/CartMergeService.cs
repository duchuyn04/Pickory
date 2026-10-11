using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Commerce.Settings;

namespace Pickory.Modules.Commerce.Carts;

// Gộp giỏ vãng lai vào giỏ tài khoản khi khách đăng nhập. Gộp không gọi catalog và không phát sự kiện giỏ.
public sealed class CartMergeService(
    CommerceDbContext database,
    CommerceSettingsProvider settings,
    TimeProvider clock)
{
    public async Task MergeGuestCartAsync(string anonymousId, string userId, CancellationToken cancellationToken)
    {
        var limits = await settings.GetLimitsAsync(cancellationToken);

        // Mọi thay đổi nằm trong một transaction. Lỗi giữa chừng không để lại giỏ gộp dở.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Giỏ vãng lai là giỏ có đúng mã ẩn danh và chưa có `UserId`. Không có giỏ này thì không còn gì để gộp.
        var guestCartId = await database.Carts.AsNoTracking()
            .Where(cart => cart.AnonymousId == anonymousId && cart.UserId == null)
            .Select(cart => (string?)cart.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (guestCartId is null)
        {
            return;
        }

        // Giỏ vãng lai rỗng chỉ cần xóa. Không tạo giỏ tài khoản rỗng chỉ vì khách từng mở giỏ.
        var guestHasLines = await database.CartItems.AnyAsync(item => item.CartId == guestCartId, cancellationToken);
        if (guestHasLines)
        {
            // Bảo đảm giỏ tài khoản tồn tại trước khi khóa. Hai lần gộp đồng thời không tạo hai giỏ, vì `ON CONFLICT DO NOTHING`.
            await CartInserter.InsertIfMissingAsync(database, clock, userId, anonymousId: null, cancellationToken);
        }

        var accountCartId = await database.Carts.AsNoTracking()
            .Where(cart => cart.UserId == userId)
            .Select(cart => (string?)cart.Id)
            .SingleOrDefaultAsync(cancellationToken);

        // Khóa cả hai giỏ bằng một câu lệnh sắp theo `id`. Thứ tự khóa cố định nên hai lần gộp đồng thời không deadlock.
        string[] cartIdsToLock = accountCartId is null ? [guestCartId] : [guestCartId, accountCartId];
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "id" FROM carts WHERE "id" = ANY ({cartIdsToLock}) ORDER BY "id" FOR UPDATE""",
            cancellationToken);

        // Đọc lại giỏ vãng lai sau khi giữ khóa. Lần gộp khác đã xong trước đó thì giỏ này không còn, và lần này không làm gì.
        var guestCart = await database.Carts.Include(cart => cart.Items)
            .SingleOrDefaultAsync(
                cart => cart.Id == guestCartId && cart.AnonymousId == anonymousId && cart.UserId == null,
                cancellationToken);
        if (guestCart is null)
        {
            return;
        }

        if (guestCart.Items.Count == 0 || accountCartId is null)
        {
            // Đến đây giỏ rỗng, hoặc giỏ vừa được thêm dòng sau lần kiểm đầu mà chưa có giỏ tài khoản. Dòng mới sẽ gộp ở lần đăng nhập sau.
            if (guestCart.Items.Count == 0)
            {
                database.Carts.Remove(guestCart);
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            return;
        }

        var accountCart = await database.Carts.Include(cart => cart.Items)
            .SingleAsync(cart => cart.Id == accountCartId, cancellationToken);

        // Luật gộp là hàm thuần. Kết quả là toàn bộ dòng của giỏ tài khoản sau khi gộp.
        var mergedLines = CartMergeRules.Merge(
            ToLines(accountCart),
            ToLines(guestCart),
            limits.MaxQuantityPerSku,
            limits.MaxCartLines);
        ApplyLines(accountCart, mergedLines);

        // Đổi thời điểm cập nhật để job dọn giỏ cũ sau này biết giỏ còn hoạt động.
        accountCart.UpdatedAt = clock.GetUtcNow().UtcDateTime;

        // Xóa giỏ vãng lai cùng dòng của nó. Lần nhận sự kiện lặp lại sẽ không còn gì để cộng.
        database.Carts.Remove(guestCart);

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static List<CartLineInput> ToLines(Cart cart) =>
        cart.Items.Select(item => new CartLineInput(item.SkuId, item.Quantity)).ToList();

    private void ApplyLines(Cart accountCart, IReadOnlyList<CartLineInput> mergedLines)
    {
        foreach (var line in mergedLines)
        {
            var existing = accountCart.Items.SingleOrDefault(item => item.SkuId == line.SkuId);
            if (existing is null)
            {
                database.CartItems.Add(new CartItem { CartId = accountCart.Id, SkuId = line.SkuId, Quantity = line.Quantity });
                continue;
            }

            existing.Quantity = line.Quantity;
        }
    }
}
