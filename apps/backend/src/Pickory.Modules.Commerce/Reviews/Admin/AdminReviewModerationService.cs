using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Reviews.Admin;

// Hàng chờ kiểm duyệt và thao tác ẩn, hiện đánh giá của nhân viên.
// Mỗi thao tác ghi chạy trong một transaction: khóa dòng đánh giá, đổi trạng thái, ghi audit, rồi commit.
internal sealed class AdminReviewModerationService(
    CommerceDbContext database,
    ICatalogReader catalog,
    IAuditLogWriter auditLogWriter,
    TimeProvider clock,
    IEventDispatcher dispatcher)
{
    public const string HideAuditAction = "commerce.review.hide";
    public const string ShowAuditAction = "commerce.review.show";
    public const string AuditTargetType = "review";

    // Từ số báo cáo này trở lên, đánh giá đứng trước mọi đánh giá khác trong hàng chờ.
    public const int PriorityReportThreshold = 3;

    private const string NotFoundMessage = "Không tìm thấy đánh giá.";
    private const string StateUnchangedMessage = "Đánh giá đã ở trạng thái này.";

    public async Task<PagedResponse<AdminReviewResponse>> GetListAsync(
        AdminReviewListQuery filter, CancellationToken cancellationToken)
    {
        var reviews = database.Reviews.AsNoTracking();

        // Bộ lọc trạng thái chỉ thêm điều kiện khi có tham số. Không truyền thì hàng chờ có cả hai nhóm.
        if (filter.Status == AdminReviewListQuery.VisibleStatus)
        {
            reviews = reviews.Where(review => review.HiddenAt == null);
        }
        else if (filter.Status == AdminReviewListQuery.HiddenStatus)
        {
            reviews = reviews.Where(review => review.HiddenAt != null);
        }

        // Đếm cùng điều kiện lọc. Giao diện biết có bao nhiêu trang.
        var total = await reviews.CountAsync(cancellationToken);

        // Nhóm từ ngưỡng báo cáo đứng đầu theo số báo cáo giảm dần, phần còn lại mới nhất trước.
        // Sắp thêm theo `Id` để các dòng cùng thời điểm không lặp hay sót khi phân trang.
        var pageIds = await reviews
            .OrderByDescending(review => review.ReportCount >= PriorityReportThreshold)
            .ThenByDescending(review => review.ReportCount >= PriorityReportThreshold ? review.ReportCount : 0)
            .ThenByDescending(review => review.CreatedAt)
            .ThenBy(review => review.Id)
            .Skip(filter.Page.Skip)
            .Take(filter.Page.Limit)
            .Select(review => review.Id)
            .ToListAsync(cancellationToken);

        // Truy vấn riêng đọc nội dung các dòng, vì phép nối sau `LIMIT` làm SQL ngoài mất `ORDER BY`.
        // Tên người viết lấy từ `Contact` của đơn, vì identity không có hàm đọc tên cho module khác.
        var rowsById = await reviews
            .Where(review => pageIds.Contains(review.Id))
            .Select(review => new ReviewRow(
                review.Id, review.ProductId, review.UserId, review.Rating, review.Body, review.ReportCount,
                review.HiddenAt, review.HiddenReason, review.CreatedAt, review.UpdatedAt, review.Order!.Contact))
            .ToDictionaryAsync(row => row.Id, cancellationToken);

        // Xếp lại theo thứ tự của truy vấn id. Dòng vừa bị xóa giữa hai truy vấn thì bỏ qua.
        var rows = pageIds
            .Where(rowsById.ContainsKey)
            .Select(id => rowsById[id])
            .ToList();

        // Đọc tên sản phẩm một lần cho cả trang, không gọi catalog từng dòng.
        var productNames = await ReadProductNamesAsync(rows.Select(row => row.ProductId), cancellationToken);
        var items = rows.Select(row => ToResponse(row, productNames)).ToList();
        return new PagedResponse<AdminReviewResponse>(items, total, filter.Page.Page, filter.Page.Limit);
    }

    public async Task<AdminReviewResponse> SetHiddenAsync(
        string reviewId, ReviewModerationRequest request, string actorId, CancellationToken cancellationToken)
    {
        // Transaction giữ đổi trạng thái và audit cùng thành công hoặc cùng quay lui.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Khóa dòng bằng SELECT FOR UPDATE để hai request cùng đánh giá xếp hàng, và request sau thấy trạng thái đã commit.
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "id" FROM reviews WHERE "id" = {reviewId} FOR UPDATE""", cancellationToken);

        // Đọc sau khi giữ khóa để thấy trạng thái mà thao tác trước vừa commit.
        var review = await database.Reviews
            .Include(row => row.Order)
            .SingleOrDefaultAsync(row => row.Id == reviewId, cancellationToken)
            ?? throw new AppException("REVIEW_NOT_FOUND", 404, NotFoundMessage);

        // Đặt lại đúng trạng thái đang có là thao tác thừa. Từ chối để audit không có dòng không đổi gì.
        var wasHidden = review.HiddenAt is not null;
        if (wasHidden == request.Hidden)
        {
            throw new AppException("REVIEW_STATE_UNCHANGED", 409, StateUnchangedMessage);
        }

        var before = new { hidden = wasHidden, hiddenReason = review.HiddenReason };

        if (request.Hidden)
        {
            // Ẩn ghi giờ và mã lý do ngay trên dòng, để nhân viên khác thấy vì sao đánh giá bị ẩn.
            review.HiddenAt = clock.GetUtcNow().UtcDateTime;
            review.HiddenReason = request.Reason;
        }
        else
        {
            // Hiện lại xóa giờ và lý do ẩn. `ReportCount` và các dòng báo cáo giữ nguyên làm lịch sử.
            review.HiddenAt = null;
            review.HiddenReason = null;
        }

        await database.SaveChangesAsync(cancellationToken);

        // Ghi đúng một dòng audit trong cùng transaction, không kèm nội dung đánh giá.
        // Lý do hiện lại chỉ có trong audit, vì cột `HiddenReason` về `null`.
        object after = request.Hidden
            ? new { hidden = true, hiddenReason = review.HiddenReason }
            : new { hidden = false, hiddenReason = (string?)null, reason = request.Reason };
        var entry = new AuditLogEntry(
            actorId,
            request.Hidden ? HideAuditAction : ShowAuditAction,
            AuditTargetType,
            review.Id,
            before,
            after);
        await auditLogWriter.WriteAsync(entry, transaction.GetDbTransaction(), cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        // Phát sự kiện sau commit, để người nghe không thấy thay đổi đã quay lui.
        var events = new DomainEventCollector(dispatcher);
        events.Emit(new ReviewChangedEvent(
            review.ProductId, review.Id, request.Hidden ? ReviewChange.Hidden : ReviewChange.Shown));
        await events.FlushAsync(cancellationToken);

        var productNames = await ReadProductNamesAsync([review.ProductId], cancellationToken);
        return ToResponse(
            new ReviewRow(
                review.Id, review.ProductId, review.UserId, review.Rating, review.Body, review.ReportCount,
                review.HiddenAt, review.HiddenReason, review.CreatedAt, review.UpdatedAt, review.Order!.Contact),
            productNames);
    }

    private async Task<Dictionary<string, string>> ReadProductNamesAsync(
        IEnumerable<string> productIds, CancellationToken cancellationToken)
    {
        var distinctIds = productIds.Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return [];
        }

        // Catalog bỏ sản phẩm không còn, nên id thiếu trong kết quả cho `ProductName` là `null`.
        var products = await catalog.GetProductsAsync(distinctIds, cancellationToken);
        return products.ToDictionary(product => product.Id, product => product.Name);
    }

    private static AdminReviewResponse ToResponse(ReviewRow row, Dictionary<string, string> productNames)
    {
        return new AdminReviewResponse(
            row.Id,
            row.ProductId,
            productNames.GetValueOrDefault(row.ProductId),
            row.Rating,
            row.Body,
            ReviewAuthorName.ForReview(row.UserId, row.Contact),
            row.ReportCount,
            row.HiddenAt is not null,
            row.HiddenReason,
            row.HiddenAt is { } hiddenAt ? AsUtc(hiddenAt) : null,
            AsUtc(row.CreatedAt),
            AsUtc(row.UpdatedAt));
    }

    // Cột lưu giờ UTC không kèm múi giờ. Đánh dấu rõ UTC để JSON có hậu tố `Z`.
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record ReviewRow(
        string Id,
        string ProductId,
        string? UserId,
        int Rating,
        string? Body,
        int ReportCount,
        DateTime? HiddenAt,
        string? HiddenReason,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        JsonDocument Contact);
}
