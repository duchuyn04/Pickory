namespace Pickory.Modules.Catalog.Reading;

// Một ảnh ứng viên của sản phẩm. `SkuId` khác `null` khi ảnh gắn riêng cho một SKU.
public sealed record ImageCandidate(string PublicId, string Alt, string? SkuId);

public static class SkuImageChooser
{
    // `orderedImages` đã sắp theo vị trí hiển thị. Ảnh riêng của SKU thắng, không có thì lấy ảnh đầu của sản phẩm.
    public static ImageCandidate? Choose(string skuId, IReadOnlyList<ImageCandidate> orderedImages)
    {
        foreach (var image in orderedImages)
        {
            if (image.SkuId == skuId)
            {
                return image;
            }
        }

        return orderedImages.Count > 0 ? orderedImages[0] : null;
    }
}
