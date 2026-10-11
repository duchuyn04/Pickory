namespace Pickory.Modules.Catalog.Images;

// Lỗi khi kho ảnh không tải được ảnh từ URL nguồn. Lệnh seed dùng lỗi này để nêu đúng sản phẩm và ảnh bị hỏng.
public sealed class ImageUploadException(string message, bool isTransient = false) : Exception(message)
{
    // Lỗi tạm thời (mạng, hết thời gian chờ, 5xx) thử lại được. Lỗi dữ liệu như ảnh sai định dạng thì thử lại vẫn lỗi.
    public bool IsTransient { get; } = isTransient;
}

// Kho ảnh báo đã hết lượt gọi API (HTTP 420 hoặc 429). Thử lại ngay chỉ làm tình hình tệ hơn.
public sealed class ImageRateLimitedException(string message) : Exception(message);

// Kết quả tải một ảnh. `AlreadyExisted` nghĩa là kho ảnh đã có `publicId` này và không ghi đè.
public enum ImageUploadOutcome
{
    Uploaded,
    AlreadyExisted,
}

// Cổng tải ảnh từ một URL nguồn lên kho ảnh với `publicId` cho trước. Kho ảnh tự tải ảnh, máy chạy lệnh không tải ảnh.
public interface IImageUploader
{
    // Ném `ImageUploadException` khi kho ảnh từ chối hoặc không tải được URL nguồn.
    // Ném `ImageRateLimitedException` khi kho ảnh báo hết lượt gọi.
    Task<ImageUploadOutcome> UploadFromUrlAsync(string publicId, string sourceUrl, CancellationToken cancellationToken);
}
