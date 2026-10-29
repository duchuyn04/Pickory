namespace Pickory.Modules.Catalog.Contracts.Reading;

// Danh mục lá của một sản phẩm. Báo cáo dùng để gộp doanh thu và số lượng theo danh mục.
public sealed record ProductCategoryInfo(string ProductId, CategoryRef Category);
