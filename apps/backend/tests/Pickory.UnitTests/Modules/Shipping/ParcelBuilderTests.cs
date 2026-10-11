using Pickory.Modules.Shipping.Contracts.Parcels;
using Pickory.Modules.Shipping.Parcels;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping;

// SHIP-01: gộp nhiều dòng hàng thành một kiện theo spec.
public sealed class ParcelBuilderTests
{
    [Fact]
    // Một dòng hàng số lượng 1 cho ra đúng kích thước của dòng đó.
    public void Build_SingleItem_ReturnsSameParcel()
    {
        var parcel = ParcelBuilder.Build([new(new Parcel(500, 20, 10, 5), 1)]);

        Assert.Equal(new Parcel(500, 20, 10, 5), parcel);
    }

    [Fact]
    // Cân nặng là tổng của cân nặng nhân số lượng. Chọn số khác nhau để phép cộng sai không trùng kết quả.
    public void Build_WeightIsSumOfWeightTimesQuantity()
    {
        var parcel = ParcelBuilder.Build([new(new Parcel(300, 1, 1, 1), 2), new(new Parcel(70, 1, 1, 1), 3)]);

        Assert.Equal(810, parcel.WeightGrams);
    }

    [Fact]
    // Dài và rộng lấy giá trị lớn nhất, không nhân số lượng. Dòng có số lượng lớn không được làm đổi kết quả.
    public void Build_LengthAndWidthAreMaximumAndIgnoreQuantity()
    {
        var parcel = ParcelBuilder.Build([new(new Parcel(1, 30, 12, 1), 5), new(new Parcel(1, 25, 40, 1), 1)]);

        Assert.Equal(30, parcel.LengthCm);
        Assert.Equal(40, parcel.WidthCm);
    }

    [Fact]
    // Cao là tổng của chiều cao nhân số lượng, vì các món xếp chồng lên nhau.
    public void Build_HeightIsSumOfHeightTimesQuantity()
    {
        var parcel = ParcelBuilder.Build([new(new Parcel(1, 1, 1, 4), 3), new(new Parcel(1, 1, 1, 7), 2)]);

        Assert.Equal(26, parcel.HeightCm);
    }

    [Fact]
    // Giá trị 0 hợp lệ vì tiêu chí chỉ cấm số âm. Sản phẩm chưa khai kích thước vẫn gộp được.
    public void Build_ZeroDimensions_AreAccepted()
    {
        var parcel = ParcelBuilder.Build([new(new Parcel(0, 0, 0, 0), 2)]);

        Assert.Equal(new Parcel(0, 0, 0, 0), parcel);
    }

    [Fact]
    public void Build_EmptyList_Throws()
    {
        Assert.Throws<ArgumentException>(() => ParcelBuilder.Build([]));
    }

    [Fact]
    public void Build_NullList_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ParcelBuilder.Build(null!));
    }

    [Fact]
    public void Build_NullItem_Throws()
    {
        Assert.Throws<ArgumentException>(() => ParcelBuilder.Build([null!]));
    }

    [Fact]
    public void Build_NullParcelInItem_Throws()
    {
        Assert.Throws<ArgumentException>(() => ParcelBuilder.Build([new(null!, 1)]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    // Số lượng không dương là lỗi của bên gọi. Hàm ném lỗi thay vì âm thầm bỏ dòng.
    public void Build_NonPositiveQuantity_Throws(int quantity)
    {
        Assert.Throws<ArgumentException>(() => ParcelBuilder.Build([new(new Parcel(1, 1, 1, 1), quantity)]));
    }

    [Fact]
    // Dòng có số lượng 0 đứng cạnh dòng hợp lệ vẫn bị từ chối. Mỗi dòng phải được kiểm riêng.
    public void Build_NonPositiveQuantityNextToValidItem_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ParcelBuilder.Build([new(new Parcel(1, 1, 1, 1), 2), new(new Parcel(1, 1, 1, 1), 0)]));
    }

    [Theory]
    [InlineData(-1, 1, 1, 1)]
    [InlineData(1, -1, 1, 1)]
    [InlineData(1, 1, -1, 1)]
    [InlineData(1, 1, 1, -1)]
    public void Build_NegativeWeightOrDimension_Throws(int weight, int length, int width, int height)
    {
        Assert.Throws<ArgumentException>(() => ParcelBuilder.Build([new(new Parcel(weight, length, width, height), 1)]));
    }

    [Fact]
    // Kích thước âm ở dòng thứ hai bị bắt dù tổng cân nặng vẫn dương.
    public void Build_NegativeValueInSecondItem_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ParcelBuilder.Build([new(new Parcel(100, 1, 1, 1), 1), new(new Parcel(-5, 1, 1, 1), 1)]));
    }

    [Fact]
    // Phép nhân tràn số bị từ chối. Nếu không chặn, cân nặng quay vòng thành số âm.
    public void Build_WeightTimesQuantityOverflow_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ParcelBuilder.Build([new(new Parcel(int.MaxValue, 1, 1, 1), 2)]));
    }

    [Fact]
    // Phép cộng giữa các dòng tràn số cũng bị từ chối, dù từng phép nhân đều nằm trong giới hạn.
    public void Build_WeightSumOverflow_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ParcelBuilder.Build([new(new Parcel(int.MaxValue, 1, 1, 1), 1), new(new Parcel(1, 1, 1, 1), 1)]));
    }

    [Fact]
    public void Build_HeightOverflow_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ParcelBuilder.Build([new(new Parcel(1, 1, 1, int.MaxValue), 2)]));
    }

    [Fact]
    // Ba tích `int x int` cộng lại vượt `long.MaxValue`. Nếu chỉ kiểm tổng ở cuối, `long` quay vòng thành số âm và lọt qua.
    public void Build_WeightSumOverflowsLong_Throws()
    {
        var item = new ParcelItem(new Parcel(int.MaxValue, 1, 1, 0), int.MaxValue);

        Assert.Throws<ArgumentException>(() => ParcelBuilder.Build([item, item, item]));
    }

    [Fact]
    // Chiều cao dùng cùng cách cộng dồn nên cũng phải bắt tràn `long` khi cộng nhiều dòng lớn.
    public void Build_HeightSumOverflowsLong_Throws()
    {
        var item = new ParcelItem(new Parcel(0, 1, 1, int.MaxValue), int.MaxValue);

        Assert.Throws<ArgumentException>(() => ParcelBuilder.Build([item, item, item]));
    }

    [Fact]
    // Giá trị sát giới hạn `int` vẫn hợp lệ khi không tràn.
    public void Build_ExactlyAtIntMaximum_IsAccepted()
    {
        var parcel = ParcelBuilder.Build([new(new Parcel(int.MaxValue, 1, 1, 1), 1)]);

        Assert.Equal(int.MaxValue, parcel.WeightGrams);
    }
}
