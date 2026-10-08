using Pickory.Modules.Catalog.Reading;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Reading;

// CAT-10: cắt trang theo cursor cho `ListForIndexingAsync`. Logic thuần, không cần DB.
public sealed class IndexingPagingTests
{
    private static string[] Rows(int count) => Enumerable.Range(1, count).Select(number => $"id{number}").ToArray();

    [Fact]
    public void Split_WithExtraRow_ReturnsPageAndCursorOfLastKeptRow()
    {
        var (items, next) = IndexingPaging.Split(Rows(3), 2, id => id);

        Assert.Equal(["id1", "id2"], items);
        Assert.Equal("id2", next);
    }

    [Fact]
    public void Split_WithExactlyPageSizeRows_HasNoNextPage()
    {
        var (items, next) = IndexingPaging.Split(Rows(2), 2, id => id);

        Assert.Equal(["id1", "id2"], items);
        Assert.Null(next);
    }

    [Fact]
    public void Split_WithFewerRows_HasNoNextPage()
    {
        var (items, next) = IndexingPaging.Split(Rows(1), 2, id => id);

        Assert.Single(items);
        Assert.Null(next);
    }

    [Fact]
    public void Split_WithNoRows_ReturnsEmptyPage()
    {
        var (items, next) = IndexingPaging.Split(Rows(0), 2, id => id);

        Assert.Empty(items);
        Assert.Null(next);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(IndexingPaging.MaxPageSize + 1)]
    public void EnsurePageSizeInRange_RejectsOutOfRangeSize(int pageSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IndexingPaging.EnsurePageSizeInRange(pageSize));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(IndexingPaging.DefaultPageSize)]
    [InlineData(IndexingPaging.MaxPageSize)]
    public void EnsurePageSizeInRange_AcceptsBoundaryValues(int pageSize)
    {
        IndexingPaging.EnsurePageSizeInRange(pageSize);
    }
}
