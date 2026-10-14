using System.Globalization;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Commerce.Reviews;

// Đọc, viết và sửa đánh giá sản phẩm. Điểm trung bình không đi vào `catalog`, nên route danh sách trả kèm tóm tắt.
internal sealed class ReviewService(
    CommerceDbContext database,
    ICatalogReader catalog,
    TimeProvider clock,
    IEventDispatcher dispatcher)
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 50;
    public const int EditWindowDays = 30;
    public const string NotPurchasedReason = "NOT_PURCHASED";
    public const string AlreadyReviewedReason = "ALREADY_REVIEWED";

    private const string UniqueReviewConstraint = "reviews_productId_userId_key";
    private const string ProductNotFoundMessage = "Không tìm thấy sản phẩm.";
    private const string NotAllowedMessage = "Chỉ khách đã nhận hàng mới đánh giá được sản phẩm này.";
    private const string AlreadyExistsMessage = "Bạn đã đánh giá sản phẩm này. Hãy sửa đánh giá hiện có.";
    private const string NotFoundMessage = "Bạn chưa đánh giá sản phẩm này.";
    private const string EditExpiredMessage = "Đã quá 30 ngày kể từ khi viết, không thể sửa đánh giá.";
    private const string CursorMessage = "Cursor không hợp lệ.";

    public async Task<ProductReviewListResponse> ListAsync(
        string productId, int limit, string? cursor, CancellationToken cancellationToken)
    {
        await EnsureProductExistsAsync(productId, cancellationToken);

        // Đọc cursor trước khi chạm DB bằng truy vấn trang. Cursor sai trả 400 thay vì lỗi 500.
        DateTime cursorCreatedAt = default;
        var cursorId = string.Empty;
        var hasCursor = cursor is not null;
        if (hasCursor && !ReviewCursor.TryDecode(cursor, out cursorCreatedAt, out cursorId))
        {
            throw new ValidationException([new ValidationFailure("cursor", CursorMessage)]);
        }

        var summary = await BuildSummaryAsync(productId, cancellationToken);

        // Chỉ đánh giá đang hiện được liệt kê. Tóm tắt và trang dùng cùng điều kiện nên số liệu khớp nhau.
        var visible = database.Reviews.AsNoTracking()
            .Where(review => review.ProductId == productId && review.HiddenAt == null);

        // Cursor trỏ tới dòng cuối của trang trước. Dòng cùng thời điểm được chia nốt theo `Id` để không lặp hay mất dòng.
        if (hasCursor)
        {
            visible = visible.Where(review =>
                review.CreatedAt < cursorCreatedAt
                || (review.CreatedAt == cursorCreatedAt && review.Id.CompareTo(cursorId) > 0));
        }

        // Lấy thêm một dòng để biết còn trang sau mà không cần đếm cả bảng.
        var rows = await visible
            .OrderByDescending(review => review.CreatedAt)
            .ThenBy(review => review.Id)
            .Take(limit + 1)
            .Select(review => new
            {
                review.Id, review.Rating, review.Body, review.UserId, review.CreatedAt, review.UpdatedAt,
                // Tên người nhận nằm trong `Contact` của đơn gắn với đánh giá, vì identity không có hàm đọc tên cho module khác.
                Contact = review.Order!.Contact,
            })
            .ToListAsync(cancellationToken);

        var pageRows = rows.Take(limit).ToList();
        string? nextCursor = null;
        if (rows.Count > limit)
        {
            var last = pageRows[^1];
            nextCursor = ReviewCursor.Encode(last.CreatedAt, last.Id);
        }

        var items = pageRows
            .Select(row => new PublicReviewResponse(
                row.Id,
                row.Rating,
                row.Body,
                // Đánh giá đã ẩn danh không còn gắn với người nào, nên hiện nhãn chung dù đơn vẫn giữ tên.
                row.UserId is null ? ReviewAuthorName.AnonymousLabel : ReviewAuthorName.Shorten(ReadRecipientName(row.Contact)),
                AsUtc(row.CreatedAt),
                AsUtc(row.UpdatedAt)))
            .ToList();

        return new ProductReviewListResponse(summary, items, nextCursor);
    }

    public async Task<MyReviewStatusResponse> GetMineAsync(string productId, string userId, CancellationToken cancellationToken)
    {
        await EnsureProductExistsAsync(productId, cancellationToken);

        var review = await database.Reviews.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ProductId == productId && row.UserId == userId, cancellationToken);
        if (review is not null)
        {
            // Đã đánh giá thì không viết thêm được. Đánh giá bị ẩn vẫn trả về để khách biết trạng thái.
            return new MyReviewStatusResponse(false, AlreadyReviewedReason, ToOwnResponse(review));
        }

        var latestOrderId = await FindLatestDeliveredOrderIdAsync(productId, userId, cancellationToken);
        return latestOrderId is null
            ? new MyReviewStatusResponse(false, NotPurchasedReason, null)
            : new MyReviewStatusResponse(true, null, null);
    }

    public async Task<OwnReviewResponse> CreateAsync(
        string productId, string userId, ReviewInput input, CancellationToken cancellationToken)
    {
        await EnsureProductExistsAsync(productId, cancellationToken);

        // Đã có đánh giá thì báo 409 trước khi xét đơn hàng, để khách biết cần sửa chứ không phải viết lại.
        var alreadyReviewed = await database.Reviews
            .AnyAsync(row => row.ProductId == productId && row.UserId == userId, cancellationToken);
        if (alreadyReviewed)
        {
            throw new AppException("REVIEW_ALREADY_EXISTS", 409, AlreadyExistsMessage);
        }

        // Chỉ đơn `delivered` của chính khách, có dòng hàng của sản phẩm, mới cho phép viết. Đánh giá gắn với đơn mới nhất.
        var orderId = await FindLatestDeliveredOrderIdAsync(productId, userId, cancellationToken);
        if (orderId is null)
        {
            throw new AppException("REVIEW_NOT_ALLOWED", 403, NotAllowedMessage);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var review = new Review
        {
            ProductId = productId,
            UserId = userId,
            OrderId = orderId,
            Rating = input.Rating,
            Body = input.Body,
            CreatedAt = now,
            UpdatedAt = now,
        };
        database.Reviews.Add(review);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        // Hai request đồng thời cùng qua bước kiểm tra ở trên. Khóa duy nhất chặn request thua, và trả cùng 409.
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UniqueReviewConstraint })
        {
            throw new AppException("REVIEW_ALREADY_EXISTS", 409, AlreadyExistsMessage);
        }

        // Phát sự kiện sau khi dữ liệu đã lưu, để người nghe không nhận sự kiện về đánh giá chưa tồn tại.
        await PublishChangeAsync(review, ReviewChange.Created, cancellationToken);
        return ToOwnResponse(review);
    }

    public async Task<OwnReviewResponse> UpdateAsync(
        string productId, string userId, ReviewPatch patch, CancellationToken cancellationToken)
    {
        await EnsureProductExistsAsync(productId, cancellationToken);

        // Điều kiện `UserId` nằm trong truy vấn. Khách chỉ sửa được đánh giá của chính mình.
        var review = await database.Reviews
            .SingleOrDefaultAsync(row => row.ProductId == productId && row.UserId == userId, cancellationToken)
            ?? throw new AppException("REVIEW_NOT_FOUND", 404, NotFoundMessage);

        // Hạn sửa tính từ lúc viết, không từ lần sửa gần nhất. Giờ đọc từ `TimeProvider` để test đặt được thời gian.
        var now = clock.GetUtcNow().UtcDateTime;
        if (now > review.CreatedAt.AddDays(EditWindowDays))
        {
            throw new AppException("REVIEW_EDIT_EXPIRED", 409, EditExpiredMessage);
        }

        if (patch.Rating is { } rating)
        {
            review.Rating = rating;
        }

        if (patch.HasBody)
        {
            review.Body = patch.Body;
        }

        // `HiddenAt` không bị chạm tới: khách sửa không làm đánh giá bị ẩn hiện lại. Quyền ẩn hiện thuộc kiểm duyệt.
        review.UpdatedAt = now;
        await database.SaveChangesAsync(cancellationToken);

        await PublishChangeAsync(review, ReviewChange.Updated, cancellationToken);
        return ToOwnResponse(review);
    }

    private async Task EnsureProductExistsAsync(string productId, CancellationToken cancellationToken)
    {
        // Dùng chung nguồn sự thật với trang sản phẩm. Mã không có thì không có đánh giá nào để đọc hay viết.
        var product = await catalog.GetProductAsync(productId, cancellationToken);
        if (product is null)
        {
            throw new AppException("PRODUCT_NOT_FOUND", 404, ProductNotFoundMessage);
        }
    }

    private async Task<string?> FindLatestDeliveredOrderIdAsync(
        string productId, string userId, CancellationToken cancellationToken)
    {
        return await database.Orders.AsNoTracking()
            .Where(order => order.UserId == userId
                && order.Status == OrderStatus.Delivered
                && order.Items.Any(item => item.ProductId == productId))
            .OrderByDescending(order => order.CreatedAt)
            .Select(order => order.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ProductReviewSummaryResponse> BuildSummaryAsync(string productId, CancellationToken cancellationToken)
    {
        // Một truy vấn nhóm theo số sao trên đánh giá đang hiện. Số lượng và điểm trung bình tính từ cùng kết quả.
        var counts = await database.Reviews.AsNoTracking()
            .Where(review => review.ProductId == productId && review.HiddenAt == null)
            .GroupBy(review => review.Rating)
            .Select(group => new { Rating = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        // Luôn đủ năm khóa, kể cả mức sao chưa có đánh giá, để UI vẽ biểu đồ không phải đoán khóa thiếu.
        var distribution = Enumerable.Range(ReviewRequests.MinRating, ReviewRequests.MaxRating)
            .ToDictionary(
                star => star.ToString(CultureInfo.InvariantCulture),
                star => counts.Where(row => row.Rating == star).Sum(row => row.Count));

        var total = distribution.Values.Sum();
        if (total == 0)
        {
            return new ProductReviewSummaryResponse(null, 0, distribution);
        }

        var weightedSum = counts.Sum(row => row.Rating * row.Count);
        // Làm tròn một chữ số thập phân theo kiểu thông thường (0,5 làm tròn lên), không theo kiểu làm tròn về số chẵn.
        var average = Math.Round((double)weightedSum / total, 1, MidpointRounding.AwayFromZero);
        return new ProductReviewSummaryResponse(average, total, distribution);
    }

    private async Task PublishChangeAsync(Review review, ReviewChange change, CancellationToken cancellationToken)
    {
        var events = new DomainEventCollector(dispatcher);
        events.Emit(new ReviewChangedEvent(review.ProductId, review.Id, change));
        await events.FlushAsync(cancellationToken);
    }

    private static string? ReadRecipientName(JsonDocument contact)
    {
        // Đơn cũ hoặc đơn nhập tay có thể thiếu tên. Khi đó hàm rút gọn trả nhãn chung.
        return contact.RootElement.ValueKind == JsonValueKind.Object
            && contact.RootElement.TryGetProperty("name", out var name)
            && name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : null;
    }

    private static OwnReviewResponse ToOwnResponse(Review review)
    {
        var createdAt = AsUtc(review.CreatedAt);
        return new OwnReviewResponse(
            review.Id,
            review.Rating,
            review.Body,
            review.HiddenAt is not null,
            createdAt,
            AsUtc(review.UpdatedAt),
            createdAt.AddDays(EditWindowDays));
    }

    // Cột lưu giờ UTC không kèm múi giờ. Đánh dấu rõ UTC để JSON có hậu tố `Z` và client không hiểu nhầm giờ địa phương.
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
