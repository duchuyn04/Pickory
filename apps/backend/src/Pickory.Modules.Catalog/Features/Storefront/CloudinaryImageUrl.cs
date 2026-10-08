using Pickory.Modules.Catalog.Images;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Dựng URL ảnh gốc từ `publicId`. Giao diện tự chèn `f_auto,q_auto,w_<rộng>` để lấy ảnh vừa kích thước.
public static class CloudinaryImageUrl
{
    public static string Original(CloudinaryCredentials credentials, string publicId) =>
        $"https://res.cloudinary.com/{credentials.CloudName}/image/upload/{publicId}";
}
