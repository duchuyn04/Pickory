using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Catalog.Features.Storefront;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Storefront;

// CAT-10: gom thuộc tính hiệu lực thành nhóm cho bảng so sánh. Logic thuần, không cần DB.
public sealed class AttributeGroupingTests
{
    private static AttributeDefinitionInfo Definition(string key, string group) =>
        new(key, key, group, "text", null, null, false, false);

    [Fact]
    public void Group_OrdersGroupsByFirstAppearance_AndKeepsAttributeOrder()
    {
        var groups = AttributeGrouping.Group(
            [Definition("ram", "Cấu hình"), Definition("screen", "Màn hình"), Definition("cpu", "Cấu hình")]);

        Assert.Equal(["Cấu hình", "Màn hình"], groups.Select(group => group.Name));
        Assert.Equal(["ram", "cpu"], groups[0].Attributes.Select(attribute => attribute.Key));
        Assert.Equal(["screen"], groups[1].Attributes.Select(attribute => attribute.Key));
    }

    [Fact]
    public void Group_WithNoDefinitions_ReturnsNoGroups()
    {
        Assert.Empty(AttributeGrouping.Group([]));
    }
}
