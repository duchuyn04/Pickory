namespace Pickory.Modules.Catalog.Attributes;

// Bốn kiểu giá trị của thuộc tính thông số. Giá trị là chuỗi lưu trong cột JSON `categories.attributes`.
public static class AttributeTypes
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Enum = "enum";
    public const string Boolean = "boolean";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal) { Text, Number, Enum, Boolean };
}
