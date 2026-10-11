using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Commerce.Settings;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Commerce.Carts;

// Chủ của giỏ. Khách đã đăng nhập dùng `UserId`, khách vãng lai dùng `AnonymousId`.
public sealed record CartOwner(string? UserId, string AnonymousId);

// Nghiệp vụ giỏ hàng: xem, thêm, đổi số lượng và xóa. Giỏ không lưu giá và không giữ hàng.
public sealed class CartService(
    CommerceDbContext database,
    ICatalogReader catalog,
    CommerceSettingsProvider settings,
    IEventDispatcher dispatcher,
    TimeProvider clock)
{
    public async Task<CartResponse> GetAsync(CartOwner owner, CancellationToken cancellationToken)
    {
        var limits = await settings.GetLimitsAsync(cancellationToken);

        // Giỏ chưa tồn tại thì trả giỏ rỗng và không ghi DB. Khách chỉ xem giỏ không tạo dòng rác.
        var cartId = await FindCartIdAsync(owner, cancellationToken);
        if (cartId is null)
        {
            return CartResponseBuilder.Build([], new Dictionary<string, SkuSnapshot>(), limits);
        }

        var lines = await database.CartItems.AsNoTracking()
            .Where(item => item.CartId == cartId)
            .Select(item => new CartLineInput(item.SkuId, item.Quantity))
            .ToListAsync(cancellationToken);
        return await BuildResponseAsync(lines, limits, cancellationToken);
    }

    public async Task<CartResponse> AddItemAsync(CartOwner owner, AddCartItemInput input, CancellationToken cancellationToken)
    {
        var limits = await settings.GetLimitsAsync(cancellationToken);

        // Tra catalog trước khi mở transaction. Giữ khóa giỏ trong lúc chờ module khác là không cần thiết.
        var sku = (await catalog.GetSkusAsync([input.SkuId], cancellationToken)).FirstOrDefault();
        switch (CartRules.CheckAddable(sku))
        {
            // Sản phẩm nháp và SKU lạ cùng trả 404, để API không lộ sản phẩm chưa công khai.
            case AddCheck.NotFound:
                throw new AppException("SKU_NOT_FOUND", 404, "Không tìm thấy sản phẩm này.");
            case AddCheck.Unavailable:
                throw new AppException("SKU_UNAVAILABLE", 422, "Sản phẩm này hiện không thể mua.");
        }

        // Số lượng đã vượt giới hạn thì từ chối trước khi tạo giỏ. Request lỗi không để lại giỏ trống.
        ThrowIfQuantityExceeded(input.Quantity, limits);

        var events = new DomainEventCollector(dispatcher);
        var lines = await RunLockedAsync(owner, createIfMissing: true, cart =>
        {
            var existing = cart.Items.SingleOrDefault(item => item.SkuId == input.SkuId);

            if (existing is null)
            {
                // Kiểm số dòng sau khi giữ khóa. Hai request thêm SKU khác nhau cùng lúc không cùng vượt giới hạn.
                if (CartRules.ExceedsLineLimit(cart.Items.Count + 1, limits.MaxCartLines))
                {
                    throw new AppException(
                        "CART_LIMIT_EXCEEDED", 422, $"Giỏ hàng chỉ chứa tối đa {limits.MaxCartLines} sản phẩm khác nhau.");
                }

                database.CartItems.Add(new CartItem { CartId = cart.Id, SkuId = input.SkuId, Quantity = input.Quantity });
            }
            else
            {
                // Cộng dồn số lượng và kiểm tổng sau khi giữ khóa. Tổng vượt giới hạn thì giữ nguyên dòng cũ.
                var total = checked(existing.Quantity + input.Quantity);
                ThrowIfQuantityExceeded(total, limits);
                existing.Quantity = total;
            }

            // Sự kiện mang số lượng vừa thêm, không phải tổng trong giỏ. Collector chỉ phát sau khi commit.
            events.Emit(new CartItemAddedEvent(input.SkuId, input.Quantity, owner.UserId, owner.AnonymousId, input.Source));
            return Task.CompletedTask;
        }, cancellationToken);

        // Phát sau commit. Handler của module khác không nhận sự kiện về dữ liệu chưa lưu.
        await events.FlushAsync(cancellationToken);
        return await BuildResponseAsync(lines, limits, cancellationToken);
    }

    public async Task<CartResponse> SetQuantityAsync(
        CartOwner owner, string skuId, int quantity, CancellationToken cancellationToken)
    {
        var limits = await settings.GetLimitsAsync(cancellationToken);

        var lines = await RunLockedAsync(owner, createIfMissing: false, cart =>
        {
            // Tra dòng trước khi kiểm giới hạn. SKU không có trong giỏ trả 404 dù số lượng mới có hợp lệ hay không.
            var existing = FindItemOrThrow(cart, skuId);

            // Số lượng mới thay hẳn số cũ, nên chỉ cần so trực tiếp với giới hạn. Vượt thì giữ nguyên dòng cũ.
            ThrowIfQuantityExceeded(quantity, limits);
            existing.Quantity = quantity;
            return Task.CompletedTask;
        }, cancellationToken);

        return await BuildResponseAsync(lines, limits, cancellationToken);
    }

    public async Task<CartResponse> RemoveItemAsync(CartOwner owner, string skuId, CancellationToken cancellationToken)
    {
        var limits = await settings.GetLimitsAsync(cancellationToken);

        var events = new DomainEventCollector(dispatcher);
        var lines = await RunLockedAsync(owner, createIfMissing: false, cart =>
        {
            var existing = FindItemOrThrow(cart, skuId);
            database.CartItems.Remove(existing);

            // Chỉ lệnh xóa thành công mới phát sự kiện. Collector giữ sự kiện tới sau commit.
            events.Emit(new CartItemRemovedEvent(skuId, owner.UserId, owner.AnonymousId));
            return Task.CompletedTask;
        }, cancellationToken);

        await events.FlushAsync(cancellationToken);
        return await BuildResponseAsync(lines, limits, cancellationToken);
    }

    // Chạy thao tác ghi trên giỏ của `owner` trong một transaction có khóa dòng giỏ. Trả các dòng sau khi commit.
    private async Task<IReadOnlyList<CartLineInput>> RunLockedAsync(
        CartOwner owner, bool createIfMissing, Func<Cart, Task> mutate, CancellationToken cancellationToken)
    {
        // Mọi lỗi nghiệp vụ ném từ `mutate` hủy transaction khi dispose. Dữ liệu và sự kiện không bị giữ lại.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        var cartId = createIfMissing
            ? await EnsureCartAsync(owner, cancellationToken)
            : await FindCartIdAsync(owner, cancellationToken);
        if (cartId is null)
        {
            throw CartItemNotFound();
        }

        // Khóa dòng giỏ cho tới khi commit. Các request ghi cùng giỏ chạy lần lượt, nên khóa chính `(cartId, skuId)` không bao giờ trùng.
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "id" FROM carts WHERE "id" = {cartId} FOR UPDATE""", cancellationToken);

        // Đọc dòng giỏ sau khi giữ khóa. Request chờ khóa thấy dữ liệu mà request trước đã commit.
        var cart = await database.Carts.Include(value => value.Items)
            .SingleAsync(value => value.Id == cartId, cancellationToken);

        await mutate(cart);

        // Đổi thời điểm cập nhật để job dọn giỏ cũ sau này biết giỏ còn hoạt động.
        cart.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return cart.Items.Select(item => new CartLineInput(item.SkuId, item.Quantity)).ToList();
    }

    // Tạo giỏ nếu chưa có rồi trả id. `ON CONFLICT DO NOTHING` bỏ qua cả hai ràng buộc unique, nên hai request đầu tiên không tạo hai giỏ.
    private async Task<string> EnsureCartAsync(CartOwner owner, CancellationToken cancellationToken)
    {
        // Giỏ của tài khoản để `AnonymousId` là null. COM-05 gộp giỏ vãng lai vào giỏ tài khoản khi đăng nhập.
        var anonymousId = owner.UserId is null ? owner.AnonymousId : null;

        // Cột `updatedAt` là `timestamp` không múi giờ. Khai báo kiểu tường minh để Npgsql không gửi `timestamptz` theo múi giờ phiên.
        var updatedAt = new NpgsqlParameter("updatedAt", NpgsqlDbType.Timestamp)
        {
            Value = DateTime.SpecifyKind(clock.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified),
        };
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO carts ("id", "anonymousId", "userId", "updatedAt")
            VALUES (@id, @anonymousId, @userId, @updatedAt)
            ON CONFLICT DO NOTHING
            """,
            [
                new NpgsqlParameter("id", Guid.NewGuid().ToString()),
                new NpgsqlParameter("anonymousId", NpgsqlDbType.Text) { Value = (object?)anonymousId ?? DBNull.Value },
                new NpgsqlParameter("userId", NpgsqlDbType.Text) { Value = (object?)owner.UserId ?? DBNull.Value },
                updatedAt,
            ],
            cancellationToken);

        // Sau lệnh trên giỏ chắc chắn tồn tại, do request này tạo hoặc request đồng thời đã tạo.
        return await FindCartIdAsync(owner, cancellationToken)
            ?? throw new InvalidOperationException("Giỏ hàng phải tồn tại sau khi tạo.");
    }

    private Task<string?> FindCartIdAsync(CartOwner owner, CancellationToken cancellationToken)
    {
        // Khách đã đăng nhập chỉ thấy giỏ theo `UserId`. Giỏ vãng lai cũ chờ COM-05 gộp.
        if (owner.UserId is not null)
        {
            return database.Carts.AsNoTracking()
                .Where(cart => cart.UserId == owner.UserId)
                .Select(cart => (string?)cart.Id)
                .SingleOrDefaultAsync(cancellationToken);
        }

        // Điều kiện `UserId` null giữ giỏ tài khoản không lọt vào kết quả của khách vãng lai.
        return database.Carts.AsNoTracking()
            .Where(cart => cart.AnonymousId == owner.AnonymousId && cart.UserId == null)
            .Select(cart => (string?)cart.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<CartResponse> BuildResponseAsync(
        IReadOnlyList<CartLineInput> lines, CommerceLimits limits, CancellationToken cancellationToken)
    {
        // Giá và trạng thái lấy mới từ catalog ở mỗi lần dựng giỏ. Giỏ không lưu giá.
        var skuIds = lines.Select(line => line.SkuId).ToList();
        var snapshots = skuIds.Count == 0
            ? []
            : await catalog.GetSkusAsync(skuIds, cancellationToken);
        return CartResponseBuilder.Build(lines, snapshots.ToDictionary(snapshot => snapshot.SkuId), limits);
    }

    private static CartItem FindItemOrThrow(Cart cart, string skuId)
    {
        return cart.Items.SingleOrDefault(item => item.SkuId == skuId) ?? throw CartItemNotFound();
    }

    private static AppException CartItemNotFound() =>
        new("CART_ITEM_NOT_FOUND", 404, "Sản phẩm này không có trong giỏ hàng.");

    private static void ThrowIfQuantityExceeded(int quantity, CommerceLimits limits)
    {
        if (CartRules.ExceedsQuantityLimit(quantity, limits.MaxQuantityPerSku))
        {
            throw new AppException(
                "CART_LIMIT_EXCEEDED", 422, $"Mỗi sản phẩm chỉ được thêm tối đa {limits.MaxQuantityPerSku} vào giỏ hàng.");
        }
    }
}
