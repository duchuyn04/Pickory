using System.Text.Json;

namespace Pickory.Modules.Catalog.Attributes;

// Một thuộc tính thông số của danh mục, ví dụ RAM hoặc màu sắc.
// `Type` là chuỗi, không phải enum C#, để kiểm tra được kiểu lạ đọc từ JSON.
public sealed record AttributeDefinition
{
    // Tên trường camelCase giống JSON mà bản Node đã lưu trong DB.
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Group { get; init; }
    public required string Type { get; init; }
    public string? Unit { get; init; }
    public IReadOnlyList<string>? Options { get; init; }
    public bool Required { get; init; }
    public bool Filterable { get; init; }
}
