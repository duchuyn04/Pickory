namespace Pickory.Modules.Catalog.Images;

// Thông tin thật của ảnh do kho ảnh trả về. API dùng thông tin này để kiểm lại kích thước và định dạng.
public sealed record StoredImage(long Bytes, string Format);

// Cổng tới kho ảnh. Production dùng Cloudinary, dev và test dùng bản giả.
public interface IImageStorage
{
    // Trả null khi kho ảnh không có ảnh này, để API báo lỗi thay vì gắn ảnh không tồn tại.
    Task<StoredImage?> FindAsync(string publicId, CancellationToken cancellationToken);

    // Ném lỗi khi kho ảnh từ chối. Worker dùng lỗi này để hẹn giờ thử lại.
    Task DeleteAsync(string publicId, CancellationToken cancellationToken);
}

// Khóa dùng để ký yêu cầu tải ảnh. Secret chỉ nằm ở server, không bao giờ gửi cho trình duyệt.
public sealed record CloudinaryCredentials(string CloudName, string ApiKey, string ApiSecret)
{
    // Ghi đè `ToString` để secret không lọt vào log khi bản ghi bị in ra.
    public override string ToString() => $"CloudinaryCredentials {{ CloudName = {CloudName} }}";
}
