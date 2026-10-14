using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Commerce.Reviews;

// Khách báo cáo đánh giá vi phạm. Mỗi khách báo cáo một đánh giá tối đa một lần, và `ReportCount` tăng cùng transaction.
internal sealed class ReviewReportService(CommerceDbContext database, TimeProvider clock)
{
    private const string UniqueReportConstraint = "review_reports_reviewId_userId_key";
    private const string NotFoundMessage = "Không tìm thấy đánh giá.";
    private const string OwnReviewMessage = "Bạn không thể báo cáo đánh giá của chính mình.";
    private const string AlreadyReportedMessage = "Bạn đã báo cáo đánh giá này.";

    public async Task<ReviewReportResponse> ReportAsync(string reviewId, string userId, CancellationToken cancellationToken)
    {
        // Đánh giá đang ẩn trả 404 như đánh giá không tồn tại, vì khách không còn thấy nội dung để báo cáo.
        var review = await database.Reviews.AsNoTracking()
            .Where(row => row.Id == reviewId && row.HiddenAt == null)
            .Select(row => new { row.Id, row.UserId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new AppException("REVIEW_NOT_FOUND", 404, NotFoundMessage);

        // Tác giả không tự báo cáo được, vì báo cáo của chính mình làm đánh giá lên đầu hàng chờ kiểm duyệt.
        if (review.UserId == userId)
        {
            throw new AppException("CANNOT_REPORT_OWN_REVIEW", 409, OwnReviewMessage);
        }

        // Thêm báo cáo và tăng bộ đếm cùng một transaction. Không để lại báo cáo mà bộ đếm chưa tăng, hoặc ngược lại.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var report = new ReviewReport
        {
            ReviewId = review.Id,
            UserId = userId,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        database.ReviewReports.Add(report);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        // Khóa duy nhất `(reviewId, userId)` là nơi chặn báo cáo trùng, kể cả hai request đồng thời.
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UniqueReportConstraint })
        {
            throw new AppException("REVIEW_ALREADY_REPORTED", 409, AlreadyReportedMessage);
        }

        // Tăng bằng một lệnh cập nhật ngay trong DB, không đọc rồi ghi lại, để hai báo cáo khác khách không ghi đè nhau.
        await database.Reviews
            .Where(row => row.Id == review.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ReportCount, row => row.ReportCount + 1), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new ReviewReportResponse(review.Id, DateTime.SpecifyKind(report.CreatedAt, DateTimeKind.Utc));
    }
}
