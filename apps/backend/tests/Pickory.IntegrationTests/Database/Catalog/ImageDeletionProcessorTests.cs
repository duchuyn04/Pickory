using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Images;
using Xunit;

namespace Pickory.IntegrationTests.Database.Catalog;

// Worker xóa ảnh: xóa xong thì đánh dấu xong, lỗi thì hẹn giờ thử lại, quá 8 lần thì dừng và ghi log lỗi.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ImageDeletionProcessorTests(IntegrationDatabaseFixture database) : IAsyncLifetime
{
    // Mốc thời gian xa trong tương lai để job của test khác không đến hạn cùng lúc với job của test này.
    private static readonly DateTimeOffset Now = new(2099, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly string _prefix = $"pickory/products/deletion-{Guid.NewGuid():N}";
    private readonly FakeImageStorage _storage = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly ListLogger<ImageDeletionProcessor> _logger = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Dọn job của test này, để lần chạy sau và test khác không thấy dữ liệu cũ.
        await using var context = database.CreateCatalogContext();
        await context.ImageDeletionJobs.Where(job => job.PublicId.StartsWith(_prefix)).ExecuteDeleteAsync();
    }

    private async Task<string> AddJobAsync(string name, int attempts = 0, DateTime? nextRetryAt = null)
    {
        await using var context = database.CreateCatalogContext();
        var job = new ImageDeletionJob
        {
            PublicId = $"{_prefix}/{name}",
            Status = ImageDeletionProcessor.PendingStatus,
            Attempts = attempts,
            MaxAttempts = 8,
            NextRetryAt = nextRetryAt ?? Now.UtcDateTime,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        };
        context.ImageDeletionJobs.Add(job);
        await context.SaveChangesAsync();
        return job.Id;
    }

    private async Task<int> ProcessAsync()
    {
        await using var context = database.CreateCatalogContext();
        var processor = new ImageDeletionProcessor(context, _storage, _clock, _logger);
        return await processor.ProcessDueJobsAsync(batchSize: 50, CancellationToken.None);
    }

    private async Task<ImageDeletionJob> ReadJobAsync(string id)
    {
        await using var context = database.CreateCatalogContext();
        return await context.ImageDeletionJobs.AsNoTracking().SingleAsync(job => job.Id == id);
    }

    [Fact]
    public async Task DueJob_DeleteSucceeds_MarksDoneAndDeletesImage()
    {
        var jobId = await AddJobAsync("ok");

        await ProcessAsync();

        var job = await ReadJobAsync(jobId);
        Assert.Equal(ImageDeletionProcessor.DoneStatus, job.Status);
        Assert.Equal(0, job.Attempts);
        Assert.Contains($"{_prefix}/ok", _storage.DeletedPublicIds);
    }

    [Fact]
    // Lỗi lần đầu tăng số lần thử lên 1 và hẹn thử lại sau 1 phút, job vẫn ở trạng thái chờ.
    public async Task DueJob_DeleteFails_IncrementsAttemptsAndSchedulesRetry()
    {
        var jobId = await AddJobAsync("loi");
        _storage.FailDeletesFor($"{_prefix}/loi", "Cloudinary tạm thời lỗi");

        await ProcessAsync();

        var job = await ReadJobAsync(jobId);
        Assert.Equal(ImageDeletionProcessor.PendingStatus, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Equal(Now.UtcDateTime.AddMinutes(1), job.NextRetryAt);
        Assert.Equal("Cloudinary tạm thời lỗi", job.LastError);
    }

    [Fact]
    // Job chưa đến giờ hẹn thì không được gọi kho ảnh.
    public async Task JobNotDue_IsSkipped()
    {
        var jobId = await AddJobAsync("chua-den-gio", attempts: 1, nextRetryAt: Now.UtcDateTime.AddMinutes(5));

        await ProcessAsync();

        var job = await ReadJobAsync(jobId);
        Assert.Equal(1, job.Attempts);
        Assert.DoesNotContain($"{_prefix}/chua-den-gio", _storage.DeletedPublicIds);
    }

    [Fact]
    // Lần lỗi thứ 8 dừng job, ghi log mức Error có `publicId`, và các vòng sau không thử lại nữa.
    public async Task EighthFailure_StopsJobAndLogsError()
    {
        var jobId = await AddJobAsync("het-luot", attempts: 7);
        _storage.FailDeletesFor($"{_prefix}/het-luot", "Cloudinary từ chối");

        await ProcessAsync();

        var job = await ReadJobAsync(jobId);
        Assert.Equal(ImageDeletionProcessor.FailedStatus, job.Status);
        Assert.Equal(8, job.Attempts);
        Assert.Contains(_logger.Entries, entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains($"{_prefix}/het-luot"));

        // Đẩy đồng hồ qua mọi khoảng chờ. Job đã dừng không được chạy lại.
        _clock.Advance(TimeSpan.FromDays(2));
        await ProcessAsync();
        Assert.Equal(8, (await ReadJobAsync(jobId)).Attempts);
    }

    [Fact]
    // Lỗi lần 7 vẫn còn lượt cuối, nên chưa dừng và chưa ghi log Error.
    public async Task SeventhFailure_StillRetries()
    {
        var jobId = await AddJobAsync("con-luot", attempts: 6);
        _storage.FailDeletesFor($"{_prefix}/con-luot", "Cloudinary từ chối");

        await ProcessAsync();

        var job = await ReadJobAsync(jobId);
        Assert.Equal(ImageDeletionProcessor.PendingStatus, job.Status);
        Assert.Equal(7, job.Attempts);
        Assert.Equal(Now.UtcDateTime.AddMinutes(64), job.NextRetryAt);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Level == LogLevel.Error);
    }
}
