using Pickory.Modules.Commerce.Orders.Admin;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

// Cỡ lô đọc dòng hàng của file xuất đơn hàng. Lô không dương làm `Chunk` ném lỗi khó hiểu, nên giá trị bị chặn từ đầu.
public sealed class OrderExportLineBatchingTests
{
    [Fact]
    // Không truyền giá trị thì lô mặc định là 2.000 đơn.
    public void Default_Is2000Orders()
    {
        Assert.Equal(2_000, new OrderExportLineBatching().OrderBatchSize);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5_000)]
    // Mọi lô từ một đơn trở lên là hợp lệ.
    public void Constructor_PositiveSize_KeepsValue(int size)
    {
        Assert.Equal(size, new OrderExportLineBatching(size).OrderBatchSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    // Lô bằng 0 hoặc âm bị từ chối với `ArgumentOutOfRangeException`.
    public void Constructor_NonPositiveSize_Throws(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderExportLineBatching(size));
    }
}
