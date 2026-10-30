namespace Pickory.Modules.Catalog.Entities;

// Thương hiệu sản xuất các mặt hàng điện tử được bán trên nền tảng.
public sealed class Brand
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? LogoPublicId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    // Thời điểm xóa mềm. Slug của dòng đã xóa được giải phóng cho thương hiệu mới.
    public DateTime? DeletedAt { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
