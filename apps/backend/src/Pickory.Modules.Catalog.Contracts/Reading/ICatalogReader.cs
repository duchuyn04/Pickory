namespace Pickory.Modules.Catalog.Contracts.Reading;

// Cổng đọc dữ liệu catalog cho module khác. Module khác không đọc bảng của catalog trực tiếp.
public interface ICatalogReader
{
    // Nhận id hoặc slug hiện tại. Trả `null` khi không có sản phẩm hoặc khách không thấy được sản phẩm đó.
    Task<ProductDetail?> GetProductAsync(string idOrSlug, CancellationToken cancellationToken);

    // Giữ thứ tự của `ids`. Bỏ id lạ, id trùng và sản phẩm khách không thấy.
    Task<IReadOnlyList<ProductSummary>> GetProductsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken);

    // Giữ thứ tự của `productIds`. Bỏ id lạ, id trùng và sản phẩm khách không thấy.
    Task<IReadOnlyList<ProductCategoryInfo>> GetProductCategoriesAsync(
        IReadOnlyList<string> productIds, CancellationToken cancellationToken);

    // Giữ thứ tự của `skuIds` và bỏ id lạ. Trả cả SKU tắt và sản phẩm không bán, kèm cờ để `commerce` hiện nhãn.
    Task<IReadOnlyList<SkuSnapshot>> GetSkusAsync(IReadOnlyList<string> skuIds, CancellationToken cancellationToken);

    // Duyệt sản phẩm khách thấy được theo `Id` tăng dần. `cursor` là `Id` cuối của trang trước, `null` là trang đầu.
    Task<IndexingPage> ListForIndexingAsync(string? cursor, int pageSize, CancellationToken cancellationToken);

    // Thuộc tính hiệu lực của danh mục, gồm thuộc tính của danh mục cha. Danh mục không tồn tại trả danh sách rỗng.
    Task<IReadOnlyList<AttributeDefinitionInfo>> GetAttributeDefinitionsAsync(string categoryId, CancellationToken cancellationToken);

    // Dùng cùng bộ lọc với `GET /api/products`. `limit` nằm ngoài khoảng 1 đến 48 thì bị kéo về khoảng đó.
    Task<IReadOnlyList<ProductSummary>> ListProductsAsync(ProductFilters filters, int limit, CancellationToken cancellationToken);

    // Đếm SKU đang bật có tồn kho không vượt ngưỡng sắp hết hàng. Badge của trang quản trị dùng số này.
    Task<int> CountLowStockAsync(CancellationToken cancellationToken);

    // Dựng URL ảnh khi module khác chỉ có mã ảnh trong bản chụp để giữ định dạng đồng nhất với catalog.
    string BuildProductImageUrl(string publicId);
}
