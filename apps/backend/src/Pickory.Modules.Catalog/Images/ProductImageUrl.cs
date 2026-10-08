namespace Pickory.Modules.Catalog.Images;

// Dựng URL gốc của ảnh sản phẩm. Mọi phản hồi có ảnh sản phẩm, cho quản trị và cho khách, đều dựng URL ở đây để không lệch nhau.
public static class ProductImageUrl
{
    public static string Build(CloudinaryCredentials credentials, string publicId)
    {
        // URL không kèm tham số biến đổi. Giao diện tự thêm chiều rộng và định dạng cho từng vị trí hiển thị.
        return $"https://res.cloudinary.com/{credentials.CloudName}/image/upload/{publicId}";
    }
}
