using Pickory.Modules.Catalog.Features.Storefront;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Storefront;

// CAT-09: duyệt cây danh mục để lấy chuỗi cha và các danh mục con. Logic thuần, không cần DB.
public sealed class CategoryTreeWalkerTests
{
    private static Dictionary<string, string?> Tree(params (string Id, string? ParentId)[] nodes) =>
        nodes.ToDictionary(node => node.Id, node => node.ParentId);

    [Fact]
    public void AncestorChain_ReturnsRootFirstAndSelectedLast()
    {
        var tree = Tree(("root", null), ("mid", "root"), ("leaf", "mid"));

        Assert.Equal(["root", "mid", "leaf"], CategoryTreeWalker.AncestorChain("leaf", tree));
    }

    [Fact]
    public void AncestorChain_OfRoot_ContainsOnlyItself()
    {
        Assert.Equal(["root"], CategoryTreeWalker.AncestorChain("root", Tree(("root", null), ("child", "root"))));
    }

    [Fact]
    public void AncestorChain_WithParentCycle_StopsAndKeepsEachCategoryOnce()
    {
        // Dữ liệu sai: a có cha b và b có cha a. Vòng đi lên phải dừng thay vì treo request.
        var chain = CategoryTreeWalker.AncestorChain("a", Tree(("a", "b"), ("b", "a")));

        Assert.Equal(["b", "a"], chain);
    }

    [Fact]
    public void AncestorChain_WithMissingParent_StopsAtTheLastKnownCategory()
    {
        Assert.Equal(["child"], CategoryTreeWalker.AncestorChain("child", Tree(("child", "ghost"))));
    }

    [Fact]
    public void SelfAndDescendants_OfParent_IncludesChildrenAtEveryLevel()
    {
        var tree = Tree(("root", null), ("a", "root"), ("b", "root"), ("a1", "a"), ("other", null));

        Assert.Equal(["root", "a", "b", "a1"], CategoryTreeWalker.SelfAndDescendants("root", tree));
    }

    [Fact]
    public void SelfAndDescendants_OfLeaf_ContainsOnlyItself()
    {
        var tree = Tree(("root", null), ("leaf", "root"));

        Assert.Equal(["leaf"], CategoryTreeWalker.SelfAndDescendants("leaf", tree));
    }

    [Fact]
    public void SelfAndDescendants_WithChildCycle_StopsAndKeepsEachCategoryOnce()
    {
        var chain = CategoryTreeWalker.SelfAndDescendants("a", Tree(("a", "b"), ("b", "a")));

        Assert.Equal(["a", "b"], chain);
    }
}
