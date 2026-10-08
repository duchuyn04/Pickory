using Pickory.Modules.Catalog.Contracts.Reading;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Một nhóm thông số trên bảng so sánh, ví dụ "Màn hình".
public sealed record AttributeGroup(string Name, IReadOnlyList<AttributeDefinitionInfo> Attributes);

public static class AttributeGrouping
{
    // Nhóm đứng theo lần xuất hiện đầu. Thuộc tính của danh mục cha nhờ đó đứng trước thuộc tính của con.
    public static IReadOnlyList<AttributeGroup> Group(IReadOnlyList<AttributeDefinitionInfo> definitions)
    {
        var groupNames = new List<string>();
        var attributesByGroup = new Dictionary<string, List<AttributeDefinitionInfo>>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (!attributesByGroup.TryGetValue(definition.Group, out var attributes))
            {
                attributes = [];
                attributesByGroup[definition.Group] = attributes;
                groupNames.Add(definition.Group);
            }

            attributes.Add(definition);
        }

        return groupNames.Select(name => new AttributeGroup(name, attributesByGroup[name])).ToList();
    }
}
