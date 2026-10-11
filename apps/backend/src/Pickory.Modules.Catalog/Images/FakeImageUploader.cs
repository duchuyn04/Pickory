using System.Collections.Concurrent;

namespace Pickory.Modules.Catalog.Images;

// Bộ tải ảnh giả không gọi mạng. Ảnh "đã tải" được ghi vào kho ảnh giả để `FindAsync` thấy ngay.
public sealed class FakeImageUploader(FakeImageStorage storage) : IImageUploader
{
    // Kích thước giả của ảnh sau khi tải, luôn nằm trong luật kích thước và định dạng.
    private const long FakeBytes = 150_000;

    private readonly ConcurrentQueue<(string PublicId, string SourceUrl)> _uploads = new();
    private readonly ConcurrentDictionary<string, string> _failures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Exception>> _queuedFailures = new(StringComparer.Ordinal);

    // Danh sách lần tải theo thứ tự gọi, để test kiểm tra seeder chỉ tải ảnh chưa có.
    public IReadOnlyList<(string PublicId, string SourceUrl)> Uploads => _uploads.ToArray();

    // Mô phỏng Cloudinary từ chối URL nguồn này, để test nhánh lỗi của lệnh seed.
    public void FailFor(string sourceUrl, string errorMessage)
    {
        _failures[sourceUrl] = errorMessage;
    }

    // Mô phỏng lỗi tạm thời: mỗi lần tải URL này ném một lỗi trong hàng đợi, hết hàng đợi thì tải thành công.
    public void QueueFailures(string sourceUrl, params Exception[] failures)
    {
        var queue = _queuedFailures.GetOrAdd(sourceUrl, _ => new ConcurrentQueue<Exception>());
        foreach (var failure in failures)
        {
            queue.Enqueue(failure);
        }
    }

    // Xóa mọi lỗi đã đặt, để lỗi của một test không lan sang test sau dùng chung bộ tải.
    public void ClearFailures()
    {
        _failures.Clear();
        _queuedFailures.Clear();
    }

    public Task<ImageUploadOutcome> UploadFromUrlAsync(string publicId, string sourceUrl, CancellationToken cancellationToken)
    {
        // Lỗi trong hàng đợi được ném theo thứ tự, mỗi lỗi một lần gọi.
        if (_queuedFailures.TryGetValue(sourceUrl, out var queue) && queue.TryDequeue(out var queued))
        {
            throw queued;
        }

        // Lỗi đã đặt trước thì ném như Cloudinary từ chối ảnh.
        if (_failures.TryGetValue(sourceUrl, out var errorMessage))
        {
            throw new ImageUploadException(errorMessage);
        }

        // Ảnh đã có thì giữ nguyên, giống Cloudinary với `overwrite=false`.
        if (storage.Contains(publicId))
        {
            return Task.FromResult(ImageUploadOutcome.AlreadyExisted);
        }

        _uploads.Enqueue((publicId, sourceUrl));
        storage.Add(publicId, FakeBytes, "webp");
        return Task.FromResult(ImageUploadOutcome.Uploaded);
    }
}
