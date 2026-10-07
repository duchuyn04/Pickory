using System.Collections.Concurrent;

namespace Pickory.Modules.Catalog.Images;

// Kho ảnh giả cho dev và test khi chưa có khóa Cloudinary. Không gọi mạng.
public sealed class FakeImageStorage : IImageStorage
{
    // Khóa giả để endpoint chữ ký vẫn trả dữ liệu đúng dạng ở dev. Secret này không mở được tài khoản thật nào.
    public static readonly CloudinaryCredentials Credentials = new("pickory-fake", "fake-api-key", "fake-api-secret");

    private readonly ConcurrentDictionary<string, StoredImage> _images = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _failingDeletes = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _deletedPublicIds = new();

    // Danh sách ảnh đã xóa theo thứ tự gọi, để test kiểm tra worker gọi đúng ảnh.
    public IReadOnlyList<string> DeletedPublicIds => _deletedPublicIds.ToArray();

    // Mô phỏng ảnh trình duyệt vừa tải lên Cloudinary.
    public void Add(string publicId, long bytes, string format)
    {
        _images[publicId] = new StoredImage(bytes, format);
    }

    // Mô phỏng Cloudinary từ chối xóa ảnh này, để test nhánh thử lại của worker.
    public void FailDeletesFor(string publicId, string errorMessage)
    {
        _failingDeletes[publicId] = errorMessage;
    }

    public Task<StoredImage?> FindAsync(string publicId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_images.TryGetValue(publicId, out var image) ? image : null);
    }

    public Task DeleteAsync(string publicId, CancellationToken cancellationToken)
    {
        // Lỗi đã đặt trước thì ném như Cloudinary trả lỗi, để worker tăng số lần thử.
        if (_failingDeletes.TryGetValue(publicId, out var errorMessage))
        {
            throw new InvalidOperationException(errorMessage);
        }

        _images.TryRemove(publicId, out _);
        _deletedPublicIds.Enqueue(publicId);
        return Task.CompletedTask;
    }
}
