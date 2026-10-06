namespace Pickory.Modules.Catalog.Products;

// Một trục tùy chọn của sản phẩm, ví dụ `Màu` với các giá trị `Đen` và `Trắng`.
public sealed record ProductOption(string Name, IReadOnlyList<string> Values);
