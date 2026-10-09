namespace Pickory.Modules.Catalog.Entities;

// Lịch sử đường dẫn sản phẩm cũ để hỗ trợ chuyển hướng khi đổi tên.
public sealed class ProductSlugHistory
{
    public string Id { get; set; } = null!;
    public string OldSlug { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Product? Product { get; set; }
}
