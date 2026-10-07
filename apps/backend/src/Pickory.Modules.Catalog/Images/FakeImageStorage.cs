using System.Collections.Concurrent;

namespace Pickory.Modules.Catalog.Images;

// Kho ảnh giả không gọi mạng, dùng khi chưa có khóa Cloudinary.
// Chế độ dễ dãi chỉ bật ở dev và E2E, nơi trình duyệt không tải được ảnh thật.
public sealed class FakeImageStorage(bool acceptUnknownProductImages = false) : IImageStorage
{
    // Ảnh "đã tải lên" ở chế độ dễ dãi: JPG nhỏ, luôn qua luật kích thước và định dạng.
    private static readonly StoredImage UnknownProductImage = new(Bytes: 150_000, Format: "jpg");

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
        if (_images.TryGetValue(publicId, out var image))
        {
            return Task.FromResult<StoredImage?>(image);
        }

        // Chế độ dễ dãi chỉ nhận ảnh trong thư mục sản phẩm, để luật thư mục vẫn được kiểm ở dev.
        var acceptAsUploaded = acceptUnknownProductImages && ImageUploadRules.IsProductPublicId(publicId);
        return Task.FromResult(acceptAsUploaded ? UnknownProductImage : null);
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
