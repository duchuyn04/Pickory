namespace Pickory.Modules.Catalog.Contracts.Reading;

// Định nghĩa một thuộc tính hiệu lực của danh mục. `Options` chỉ có với kiểu `enum`.
public sealed record AttributeDefinitionInfo(
    string Key,
    string Label,
    string Group,
    string Type,
    string? Unit,
    IReadOnlyList<string>? Options,
    bool Required,
    bool Filterable);
