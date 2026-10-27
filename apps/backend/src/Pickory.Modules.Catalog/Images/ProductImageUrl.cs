namespace Pickory.Modules.Catalog.Images;

// Dựng URL gốc cho ảnh sản phẩm và logo thương hiệu. Các phản hồi dùng cùng cách dựng để không lệch nhau.
public static class ProductImageUrl
{
    public static string Build(CloudinaryCredentials credentials, string publicId)
    {
        // URL không kèm tham số biến đổi. Giao diện tự thêm chiều rộng và định dạng cho từng vị trí hiển thị.
        return $"https://res.cloudinary.com/{credentials.CloudName}/image/upload/{publicId}";
    }
}
