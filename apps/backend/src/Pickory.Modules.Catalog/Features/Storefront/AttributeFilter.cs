namespace Pickory.Modules.Catalog.Features.Storefront;

// Bộ lọc thông số đã kiểm. Mỗi loại ứng với một cách so sánh giá trị trong cột `specs`.
public abstract record AttributeFilter(string Key);

// Dùng cho thuộc tính `enum` và `text`: khớp khi giá trị thông số nằm trong danh sách.
public sealed record ValueListFilter(string Key, IReadOnlyList<string> Values) : AttributeFilter(Key);

// Dùng cho thuộc tính `boolean`: khớp khi giá trị thông số bằng `Value`.
public sealed record BooleanFilter(string Key, bool Value) : AttributeFilter(Key);

// Dùng cho thuộc tính `number`: khoảng đóng, cận nào là `null` thì để mở.
public sealed record NumberRangeFilter(string Key, decimal? Min, decimal? Max) : AttributeFilter(Key);
