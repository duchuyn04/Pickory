namespace Pickory.Modules.Catalog.Products;

// Dữ liệu của một SKU cần kiểm tra trước khi đăng bán. Cân nặng và kích thước dùng để tính phí vận chuyển.
public sealed record SkuPublishableInput(
    bool Enabled,
    int? Price,
    int? WeightGrams,
    int? LengthCm,
    int? WidthCm,
    int? HeightCm);
