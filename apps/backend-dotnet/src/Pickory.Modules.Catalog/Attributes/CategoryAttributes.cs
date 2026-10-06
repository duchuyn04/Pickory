using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Catalog.Attributes;

// Gộp thuộc tính của danh mục cha và danh mục con. Danh mục con kế thừa mọi thuộc tính của cha.
public static class CategoryAttributes
{
    public static IReadOnlyList<AttributeDefinition> Resolve(
        IReadOnlyList<AttributeDefinition>? parentAttributes,
        IReadOnlyList<AttributeDefinition> childAttributes)
    {
        AssertValid(childAttributes, "con");

        // Danh mục gốc không có cha, nên chỉ có thuộc tính của chính nó.
        if (parentAttributes is null || parentAttributes.Count == 0)
        {
            return childAttributes.ToList();
        }

        AssertValid(parentAttributes, "cha");

        // Con trùng khóa với cha sẽ ghi đè thuộc tính kế thừa, nên đây là lỗi định nghĩa.
        var parentKeys = parentAttributes.Select(attribute => attribute.Key).ToHashSet(StringComparer.Ordinal);
        var shadowed = childAttributes.FirstOrDefault(attribute => parentKeys.Contains(attribute.Key));
        if (shadowed is not null)
        {
            throw new AppException(
                "INVALID_CATEGORY_ATTRIBUTES",
                400,
                $"Trùng khóa thuộc tính \"{shadowed.Key}\" giữa danh mục cha và danh mục con.");
        }

        // Thuộc tính của cha đứng trước, giữ thứ tự phân cấp trên bảng thông số.
        return [.. parentAttributes, .. childAttributes];
    }

    private static void AssertValid(IReadOnlyList<AttributeDefinition> attributes, string level)
    {
        var validation = AttributeDefinitionRules.ValidateAll(attributes);
        if (!validation.Valid)
        {
            var detail = string.Join(' ', validation.Errors.Select(error => error.Message));
            throw new AppException("INVALID_CATEGORY_ATTRIBUTES", 400, $"Định nghĩa thuộc tính danh mục {level} không hợp lệ: {detail}");
        }
    }
}
