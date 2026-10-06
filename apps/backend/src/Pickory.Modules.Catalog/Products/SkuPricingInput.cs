namespace Pickory.Modules.Catalog.Products;

// Dữ liệu của một SKU dùng để tính giá hiển thị và trạng thái còn hàng.
public sealed record SkuPricingInput(int Price, int Stock, bool Enabled);
