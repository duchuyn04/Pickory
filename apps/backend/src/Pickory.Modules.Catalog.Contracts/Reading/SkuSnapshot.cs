namespace Pickory.Modules.Catalog.Contracts.Reading;

// Ảnh chụp một SKU tại thời điểm gọi, cho giỏ hàng và đơn hàng của `commerce`.
// `Enabled` và `ProductStatus` cho biết vì sao SKU không mua được.
public sealed record SkuSnapshot(
    string SkuId,
    string Code,
    string ProductId,
    string ProductName,
    string ProductSlug,
    string ProductStatus,
    bool Enabled,
    IReadOnlyDictionary<string, string> OptionValues,
    int Price,
    int? CompareAtPrice,
    int Stock,
    int? WeightGrams,
    int? LengthCm,
    int? WidthCm,
    int? HeightCm,
    ProductImageInfo? Image,
    // Mã ảnh trong kho ảnh. Đơn hàng lưu mã này thay cho URL, vì URL đổi theo cấu hình.
    string? ImagePublicId = null,
    // Đơn cũ vẫn đọc được SKU, nhưng giỏ mới phải chặn sản phẩm đã xóa mềm.
    bool ProductDeleted = false);
