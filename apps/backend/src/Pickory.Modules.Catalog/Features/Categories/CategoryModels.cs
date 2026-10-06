using Pickory.Modules.Catalog.Attributes;

namespace Pickory.Modules.Catalog.Features.Categories;

// Dữ liệu phản hồi của một danh mục sản phẩm trong trang quản trị.
public sealed record CategoryResponse(
    string Id,
    string? ParentId,
    string Name,
    string Slug,
    int Position,
    IReadOnlyList<AttributeDefinition> Attributes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

// Dữ liệu nút trong cây danh mục phục vụ hiển thị phía người dùng.
public sealed record CategoryTreeResponse(
    string Id,
    string? ParentId,
    string Name,
    string Slug,
    int Position,
    IReadOnlyList<AttributeDefinition> Attributes,
    IReadOnlyList<CategoryTreeResponse> Children);

// Yêu cầu tạo mới danh mục sản phẩm từ trang quản trị.
public sealed record CreateCategoryRequest(
    string Name,
    string? ParentId,
    int? Position,
    IReadOnlyList<AttributeDefinition>? Attributes);

// Yêu cầu cập nhật thông tin danh mục sản phẩm từ trang quản trị.
public sealed class UpdateCategoryRequest
{
    private string? _parentId;

    public string? Name { get; set; }

    public string? ParentId
    {
        get => _parentId;
        set
        {
            _parentId = value;
            ParentIdSpecified = true;
        }
    }

    // Đánh dấu trường parentId có trong JSON để phân biệt không gửi và gửi giá trị null.
    public bool ParentIdSpecified { get; private set; }
    public int? Position { get; set; }

    public IReadOnlyList<AttributeDefinition>? Attributes { get; set; }
}
