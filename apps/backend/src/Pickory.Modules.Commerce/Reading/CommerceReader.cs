using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Contracts.Reading;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Orders.Lookup;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Commerce.Reviews;

namespace Pickory.Modules.Commerce.Reading;

// Cổng cung cấp dữ liệu đọc của module Commerce cho các module khác.
public sealed class CommerceReader(CommerceDbContext database) : ICommerceReader
{
    private const int MinOrderLimit = 1;
    private const int MaxOrderLimit = 50;

    // Lấy tóm tắt đánh giá sản phẩm gồm điểm trung bình và phân bố sao.
    public Task<IReadOnlyDictionary<string, RatingSummary>> GetRatingSummaryAsync(
        IReadOnlyList<string> productIds, CancellationToken cancellationToken)
    {
        return VisibleReviewReader.ReadRatingSummariesAsync(database, productIds, cancellationToken);
    }

    // Đọc danh sách đánh giá hiển thị công khai của sản phẩm theo phân trang con trỏ.
    public async Task<ReviewPage> GetVisibleReviewsAsync(
        string productId, int limit, string? cursor, CancellationToken cancellationToken)
    {
        // Kéo số lượng bản ghi về khoảng an toàn để chặn bên gọi đọc quá nhiều dòng trong một lần.
        var clampedLimit = Math.Clamp(limit, 1, ReviewService.MaxLimit);

        DateTime cursorCreatedAt = default;
        var cursorId = string.Empty;
        var hasCursor = cursor is not null;

        // Bên gọi nội bộ chỉ truyền lại con trỏ NextCursor, nên con trỏ sai là lỗi lập trình.
        if (hasCursor && !ReviewCursor.TryDecode(cursor, out cursorCreatedAt, out cursorId))
        {
            throw new ArgumentException("Cursor không hợp lệ.", nameof(cursor));
        }

        // Tái sử dụng hàm truy vấn dùng chung với ReviewService để bảo đảm tính nhất quán.
        var queryResult = await VisibleReviewReader.ReadVisibleReviewsAsync(
            database,
            productId,
            clampedLimit,
            hasCursor ? cursorCreatedAt : null,
            hasCursor ? cursorId : null,
            cancellationToken);

        var items = queryResult.Items
            .Select(row => new VisibleReview(
                row.Id,
                row.Rating,
                row.Body,
                row.AuthorName,
                row.CreatedAt,
                row.UpdatedAt))
            .ToList();

        return new ReviewPage(items, queryResult.NextCursor);
    }

    // Lấy danh sách mã sản phẩm khách hàng đã mua thành công.
    public async Task<IReadOnlyList<string>> GetPurchasedProductIdsAsync(
        string userId, CancellationToken cancellationToken)
    {
        // Mã khách hàng rỗng không thuộc tài khoản nào, nên trả rỗng để không cần truy vấn database.
        if (string.IsNullOrWhiteSpace(userId))
        {
            return [];
        }

        // Lọc các đơn hàng hợp lệ theo luật đơn đã mua và bóc tách mã sản phẩm.
        // Hàng đã trả lại không còn là hàng khách đã mua.
        var productIds = await database.Orders.AsNoTracking()
            .Where(order => order.UserId == userId)
            .Where(PurchasedOrderRules.IsPurchased)
            .SelectMany(order => order.Items.Select(item => item.ProductId))
            .Distinct()
            .ToListAsync(cancellationToken);

        // Sắp xếp theo thứ tự ordinal để bảo đảm kết quả trả về luôn ổn định.
        productIds.Sort(StringComparer.Ordinal);
        return productIds;
    }

    // Lấy danh sách tóm tắt đơn hàng của khách hàng theo thứ tự mới nhất.
    public async Task<IReadOnlyList<OrderSummary>> GetOrdersForCustomerAsync(
        string userId, int limit, CancellationToken cancellationToken)
    {
        // Mã khách hàng rỗng không thuộc tài khoản nào, nên trả rỗng để không cần truy vấn database.
        if (string.IsNullOrWhiteSpace(userId))
        {
            return [];
        }

        var query = database.Orders.AsNoTracking().Where(order => order.UserId == userId);
        return await ListOrderSummariesAsync(query, limit, cancellationToken);
    }

    // Tra cứu chi tiết đơn hàng theo mã đơn.
    public async Task<OrderDetail?> GetOrderByCodeAsync(
        string code, CancellationToken cancellationToken)
    {
        // Chuẩn hóa mã đơn bằng cách cắt khoảng trắng và viết hoa để tra cứu thống nhất.
        var normalizedCode = OrderLookupNormalizer.NormalizeOrderCode(code);
        if (string.IsNullOrEmpty(normalizedCode))
        {
            return null;
        }

        // Đọc thông tin đơn hàng mà không tải toàn bộ entity để giảm tải dữ liệu nhạy cảm.
        var order = await database.Orders.AsNoTracking()
            .Where(entry => entry.Code == normalizedCode)
            .Select(entry => new
            {
                entry.Code,
                entry.UserId,
                entry.AnonymousId,
                entry.Status,
                entry.PaymentStatus,
                entry.PaymentMethod,
                entry.Subtotal,
                entry.ShippingFee,
                entry.ShippingDiscount,
                entry.Total,
                entry.CreatedAt,
                entry.PaymentDeadline,
                Items = entry.Items.Select(item => new
                {
                    item.ProductId,
                    item.SkuId,
                    item.NameSnapshot,
                    item.OptionValues,
                    item.Quantity,
                    item.UnitPrice,
                }).ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return null;
        }

        // Định dạng tên biến thể từ OptionValues và tính thành tiền của từng dòng sản phẩm.
        var lines = order.Items
            .Select(item => new OrderLine(
                item.ProductId,
                item.SkuId,
                item.NameSnapshot,
                OrderItemMapper.FormatVariantName(item.OptionValues),
                item.Quantity,
                item.UnitPrice,
                checked(item.UnitPrice * item.Quantity)))
            .ToList();

        return new OrderDetail(
            order.Code,
            order.UserId,
            order.AnonymousId,
            order.Status,
            order.PaymentStatus,
            order.PaymentMethod,
            order.Subtotal,
            order.ShippingFee,
            order.ShippingDiscount,
            order.Total,
            DateTime.SpecifyKind(order.CreatedAt, DateTimeKind.Utc),
            order.PaymentDeadline.HasValue ? DateTime.SpecifyKind(order.PaymentDeadline.Value, DateTimeKind.Utc) : null,
            lines);
    }

    // Lấy danh sách tóm tắt đơn hàng của khách vãng lai theo mã ẩn danh.
    public async Task<IReadOnlyList<OrderSummary>> GetOrdersForAnonymousAsync(
        string anonymousId, int limit, CancellationToken cancellationToken)
    {
        // Cột mặc định là chuỗi rỗng nên chuỗi rỗng không được phép khớp với đơn nào.
        if (string.IsNullOrWhiteSpace(anonymousId))
        {
            return [];
        }

        var query = database.Orders.AsNoTracking().Where(order => order.AnonymousId == anonymousId);
        return await ListOrderSummariesAsync(query, limit, cancellationToken);
    }

    private static async Task<IReadOnlyList<OrderSummary>> ListOrderSummariesAsync(
        IQueryable<Order> baseQuery, int limit, CancellationToken cancellationToken)
    {
        // Kéo giới hạn số lượng về khoảng an toàn từ 1 đến 50 đơn hàng.
        var clampedLimit = Math.Clamp(limit, MinOrderLimit, MaxOrderLimit);

        // Sắp xếp thời gian tạo giảm dần và Id giảm dần để kết quả luôn xác định.
        var rows = await baseQuery
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Take(clampedLimit)
            .Select(order => new
            {
                order.Code,
                order.Status,
                order.PaymentStatus,
                order.PaymentMethod,
                order.Total,
                ItemCount = order.Items.Sum(item => item.Quantity),
                order.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        // Cột lưu giờ UTC không kèm múi giờ, nên đánh dấu rõ Kind là Utc để không nhầm giờ địa phương.
        return rows
            .Select(row => new OrderSummary(
                row.Code,
                row.Status,
                row.PaymentStatus,
                row.PaymentMethod,
                row.Total,
                row.ItemCount,
                DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)))
            .ToList();
    }
}
