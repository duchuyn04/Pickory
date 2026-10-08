namespace Pickory.Modules.Catalog.Contracts.Reading;

// Một trang sản phẩm cho `smart-search`. `Next` là `null` ở trang cuối, còn lại là cursor của trang kế.
public sealed record IndexingPage(IReadOnlyList<ProductDetail> Items, string? Next);
