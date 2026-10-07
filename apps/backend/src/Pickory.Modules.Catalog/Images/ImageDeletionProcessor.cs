using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Persistence;

namespace Pickory.Modules.Catalog.Images;

// Bộ chạy job xóa ảnh đến hạn trên kho ảnh. Lỗi thì job được hẹn giờ thử lại, quá số lần thì dừng.
public sealed class ImageDeletionProcessor(
    CatalogDbContext database,
    IImageStorage imageStorage,
    TimeProvider timeProvider,
    ILogger<ImageDeletionProcessor> logger)
{
    public const string PendingStatus = "pending";
    public const string DoneStatus = "done";
    public const string FailedStatus = "failed";

    // Giới hạn độ dài lỗi lưu vào DB. Thông báo lỗi dài từ mạng không làm phình bảng job.
    private const int MaxErrorLength = 500;

    public async Task<int> ProcessDueJobsAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Chỉ lấy job chờ đã đến giờ thử. Job xong hoặc đã dừng không bao giờ chạy lại.
        var dueJobs = await database.ImageDeletionJobs
            .Where(job => job.Status == PendingStatus && job.NextRetryAt <= now)
            .OrderBy(job => job.NextRetryAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var job in dueJobs)
        {
            await ProcessAsync(job, now, cancellationToken);
            // Lưu sau từng job, để lỗi ở job sau không làm mất kết quả của job trước.
            await database.SaveChangesAsync(cancellationToken);
        }

        return dueJobs.Count;
    }

    private async Task ProcessAsync(ImageDeletionJob job, DateTime now, CancellationToken cancellationToken)
    {
        try
        {
            await imageStorage.DeleteAsync(job.PublicId, cancellationToken);
            job.Status = DoneStatus;
            job.LastError = null;
            job.UpdatedAt = now;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Hủy do API dừng không phải lỗi của Cloudinary, nên không bị tính là một lần thử.
            RecordFailure(job, exception, now);
        }
    }

    private void RecordFailure(ImageDeletionJob job, Exception exception, DateTime now)
    {
        job.Attempts += 1;
        job.LastError = exception.Message.Length > MaxErrorLength ? exception.Message[..MaxErrorLength] : exception.Message;
        job.UpdatedAt = now;

        // Đủ số lần tối đa thì dừng hẳn và ghi log lỗi, để người vận hành dọn ảnh bằng tay.
        if (job.Attempts >= job.MaxAttempts)
        {
            job.Status = FailedStatus;
            logger.LogError(
                "Dừng xóa ảnh {PublicId} sau {Attempts} lần thử. Lỗi cuối: {LastError}",
                job.PublicId, job.Attempts, job.LastError);
            return;
        }

        // Chưa đủ số lần thì hẹn giờ thử lại theo khoảng chờ tăng dần.
        job.NextRetryAt = now + ImageDeletionBackoff.DelayAfter(job.Attempts);
        logger.LogWarning(
            "Xóa ảnh {PublicId} lỗi lần {Attempts}, thử lại lúc {NextRetryAt}.",
            job.PublicId, job.Attempts, job.NextRetryAt);
    }
}
