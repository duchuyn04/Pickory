namespace Pickory.Modules.Catalog.Products;

// Giá đại diện và trạng thái còn hàng của sản phẩm, lưu vào `products.minPrice` và `products.inStock`.
public sealed record DisplayPrice(int? MinPrice, bool InStock);
