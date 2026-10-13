using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Stock;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Inventory;

// FLT-04: tên loại biến động kho trong tham số `type`. Chỉ bốn tên chữ thường của API được nhận.
public class MovementTypeNamesTests
{
    [Theory]
    [InlineData("manual", InventoryMovementType.Manual)]
    [InlineData("order_reserve", InventoryMovementType.OrderReserve)]
    [InlineData("order_release", InventoryMovementType.OrderRelease)]
    [InlineData("return", InventoryMovementType.Return)]
    // Bốn tên khớp đúng `ToName`, nên giá trị trả về từ API lọc lại được.
    public void TryParse_AcceptsEachApiName(string name, InventoryMovementType expected)
    {
        var parsed = MovementTypeNames.TryParse(name, out var type);

        Assert.True(parsed);
        Assert.Equal(expected, type);
    }

    [Theory]
    [InlineData("MANUAL")]
    [InlineData("Manual")]
    [InlineData("Order_Reserve")]
    // Chữ hoa bị từ chối. Tên API chỉ có một dạng, để link lọc không có hai cách viết.
    public void TryParse_RejectsUpperCase(string name)
    {
        Assert.False(MovementTypeNames.TryParse(name, out _));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("3")]
    // Tên số của enum bị từ chối. `Enum.TryParse` nhận chuỗi số, nên hàm không được dùng nó.
    public void TryParse_RejectsNumericEnumValues(string name)
    {
        Assert.False(MovementTypeNames.TryParse(name, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" manual")]
    [InlineData("manual ")]
    [InlineData("orderReserve")]
    [InlineData("manual,return")]
    [InlineData("khong-biet")]
    // Chuỗi lạ, chuỗi có khoảng trắng và tên enum viết theo C# đều bị từ chối.
    public void TryParse_RejectsUnknownText(string name)
    {
        Assert.False(MovementTypeNames.TryParse(name, out _));
    }

    [Fact]
    // `ToName` và `TryParse` là cặp nghịch đảo trên mọi giá trị enum.
    public void ToName_ThenTryParse_RoundTripsEveryValue()
    {
        foreach (var value in Enum.GetValues<InventoryMovementType>())
        {
            Assert.True(MovementTypeNames.TryParse(MovementTypeNames.ToName(value), out var parsed));
            Assert.Equal(value, parsed);
        }
    }
}
