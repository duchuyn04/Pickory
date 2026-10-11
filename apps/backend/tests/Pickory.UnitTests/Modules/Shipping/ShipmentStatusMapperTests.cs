using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Shipments;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping;

// SHIP-01: ánh xạ trạng thái GHN sang trạng thái chuẩn và nhận biết trạng thái kết thúc.
public sealed class ShipmentStatusMapperTests
{
    [Theory]
    [InlineData("ready_to_pick", ShipmentStatus.PendingPickup)]
    [InlineData("picking", ShipmentStatus.PendingPickup)]
    [InlineData("picked", ShipmentStatus.InTransit)]
    [InlineData("storing", ShipmentStatus.InTransit)]
    [InlineData("transporting", ShipmentStatus.InTransit)]
    [InlineData("sorting", ShipmentStatus.InTransit)]
    [InlineData("delivering", ShipmentStatus.Delivering)]
    [InlineData("money_collect_delivering", ShipmentStatus.Delivering)]
    [InlineData("delivered", ShipmentStatus.Delivered)]
    [InlineData("delivery_fail", ShipmentStatus.DeliveryFailed)]
    [InlineData("waiting_to_return", ShipmentStatus.Returning)]
    [InlineData("return", ShipmentStatus.Returning)]
    [InlineData("return_transporting", ShipmentStatus.Returning)]
    [InlineData("return_sorting", ShipmentStatus.Returning)]
    [InlineData("returning", ShipmentStatus.Returning)]
    [InlineData("returned", ShipmentStatus.Returned)]
    [InlineData("cancel", ShipmentStatus.Cancelled)]
    [InlineData("exception", ShipmentStatus.Exception)]
    [InlineData("damage", ShipmentStatus.Exception)]
    [InlineData("lost", ShipmentStatus.Exception)]
    // Ba mã dưới đây có trong tài liệu GHN nhưng thiếu ở bảng gốc. SHIP-00 phát hiện, SHIP-10 bổ sung.
    [InlineData("money_collect_picking", ShipmentStatus.PendingPickup)]
    [InlineData("return_fail", ShipmentStatus.Returning)]
    [InlineData("scrap", ShipmentStatus.Exception)]
    // Mỗi dòng của bảng trong spec có một ca riêng, nên đổi một dòng ánh xạ sẽ làm đúng ca đó đỏ.
    public void Map_KnownGhnStatus_ReturnsStandardStatus(string rawStatus, ShipmentStatus expected)
    {
        Assert.Equal(expected, ShipmentStatusMapper.Map(rawStatus));
    }

    [Theory]
    [InlineData("something_new")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    // Trạng thái lạ trả `null`, không ném lỗi. Luồng đồng bộ giữ trạng thái cũ và ghi log.
    public void Map_UnknownOrEmptyStatus_ReturnsNull(string? rawStatus)
    {
        Assert.Null(ShipmentStatusMapper.Map(rawStatus));
    }

    [Theory]
    [InlineData("DELIVERED")]
    [InlineData("Delivered")]
    [InlineData(" delivered")]
    [InlineData("delivered ")]
    // Mapper so khớp đúng chuỗi chữ thường trong bảng spec. Chuỗi khác hoa thường hoặc có khoảng trắng bị coi là lạ.
    public void Map_NonExactSpelling_ReturnsNull(string rawStatus)
    {
        Assert.Null(ShipmentStatusMapper.Map(rawStatus));
    }

    [Theory]
    [InlineData(ShipmentStatus.Delivered)]
    [InlineData(ShipmentStatus.Returned)]
    [InlineData(ShipmentStatus.Cancelled)]
    public void IsTerminal_TerminalStatus_ReturnsTrue(ShipmentStatus status)
    {
        Assert.True(status.IsTerminal());
    }

    [Theory]
    [InlineData(ShipmentStatus.PendingPickup)]
    [InlineData(ShipmentStatus.InTransit)]
    [InlineData(ShipmentStatus.Delivering)]
    [InlineData(ShipmentStatus.DeliveryFailed)]
    [InlineData(ShipmentStatus.Returning)]
    [InlineData(ShipmentStatus.Exception)]
    public void IsTerminal_NonTerminalStatus_ReturnsFalse(ShipmentStatus status)
    {
        Assert.False(status.IsTerminal());
    }

    [Fact]
    // Trả hàng thất bại một lần thì GHN còn thử lại. Trạng thái không được là trạng thái kết thúc để job đồng bộ còn theo dõi.
    public void Map_ReturnFail_IsNotTerminal()
    {
        var status = ShipmentStatusMapper.Map("return_fail");

        Assert.NotNull(status);
        Assert.False(status.Value.IsTerminal());
    }

    [Fact]
    // Enum có đúng 9 trạng thái chuẩn. Thêm trạng thái mới buộc người sửa cập nhật cả hai bảng test.
    public void ShipmentStatus_HasExactlyNineValues()
    {
        Assert.Equal(9, Enum.GetValues<ShipmentStatus>().Length);
    }
}
