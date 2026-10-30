using System.Text.Json;

namespace Pickory.Modules.Catalog.Entities;

// Danh mục sản phẩm tổ chức dạng cây tối đa hai cấp để phân cấp mặt hàng.
public sealed class Category
{
    public string Id { get; set; } = null!;
    public string? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int Position { get; set; }
    public JsonDocument Attributes { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    // Thời điểm xóa mềm. Dòng đã xóa không còn trong cây danh mục và không nhận sản phẩm mới.
    public DateTime? DeletedAt { get; set; }

    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
