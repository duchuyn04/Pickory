using Pickory.Modules.Catalog.Reading;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Reading;

// CAT-10: giữ thứ tự id người gọi yêu cầu. Logic thuần, không cần DB.
public sealed class RequestedIdOrderTests
{
    [Fact]
    public void Normalize_DropsBlankAndDuplicateIds_KeepingFirstOccurrence()
    {
        var normalized = RequestedIdOrder.Normalize(["b", "a", "b", "", "  ", null, "c", "a"]);

        Assert.Equal(["b", "a", "c"], normalized);
    }

    [Fact]
    public void Normalize_IsCaseSensitive()
    {
        Assert.Equal(["a", "A"], RequestedIdOrder.Normalize(["a", "A"]));
    }

    [Fact]
    public void Arrange_FollowsRequestedOrderNotItemOrder()
    {
        var items = new[] { ("a", 1), ("b", 2), ("c", 3) };

        var arranged = RequestedIdOrder.Arrange(["c", "a", "b"], items, item => item.Item1);

        Assert.Equal(["c", "a", "b"], arranged.Select(item => item.Item1));
    }

    [Fact]
    public void Arrange_SkipsIdsWithoutItem()
    {
        var items = new[] { ("a", 1), ("c", 3) };

        var arranged = RequestedIdOrder.Arrange(["c", "ghost", "a"], items, item => item.Item1);

        Assert.Equal(["c", "a"], arranged.Select(item => item.Item1));
    }

    [Fact]
    public void Arrange_IgnoresItemsThatWereNotRequested()
    {
        var items = new[] { ("a", 1), ("extra", 9) };

        var arranged = RequestedIdOrder.Arrange(["a"], items, item => item.Item1);

        Assert.Equal(["a"], arranged.Select(item => item.Item1));
    }

    [Fact]
    public void Arrange_WithNoIds_ReturnsEmpty()
    {
        Assert.Empty(RequestedIdOrder.Arrange([], new[] { ("a", 1) }, item => item.Item1));
    }
}
