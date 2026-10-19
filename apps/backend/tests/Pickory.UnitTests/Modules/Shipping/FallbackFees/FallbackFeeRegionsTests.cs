using Pickory.Modules.Shipping.FallbackFees;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.FallbackFees;

// SHIP-13: bảng 34 tỉnh theo 3 vùng của GHN, mã tỉnh lấy từ `province/all` của sandbox API v3.
public sealed class FallbackFeeRegionsTests
{
    [Fact]
    public void Table_HasExactly34ProvincesWithUniqueCodes()
    {
        Assert.Equal(34, FallbackFeeRegions.All.Count);
        Assert.Equal(34, FallbackFeeRegions.All.Select(entry => entry.ProvinceCode).Distinct().Count());
    }

    [Theory]
    [InlineData(3, 18)]
    [InlineData(2, 4)]
    [InlineData(1, 12)]
    public void Table_HasExpectedProvinceCountPerRegion(int region, int expectedCount)
    {
        Assert.Equal(expectedCount, FallbackFeeRegions.All.Count(entry => (int)entry.Region == region));
    }

    [Theory]
    [InlineData("1000000", "Hà Nội", 3)]
    [InlineData("1000001", "Hồ Chí Minh", 1)]
    [InlineData("1000002", "Huế", 2)]
    [InlineData("1000003", "Đà Nẵng", 2)]
    [InlineData("1000004", "Hải Phòng", 3)]
    [InlineData("1000005", "Cần Thơ", 1)]
    [InlineData("1000006", "Đồng Nai", 1)]
    [InlineData("1000007", "Gia Lai", 1)]
    [InlineData("1000008", "Khánh Hòa", 1)]
    [InlineData("1000009", "Lâm Đồng", 1)]
    [InlineData("1000010", "Đắk Lắk", 1)]
    [InlineData("1000011", "Vĩnh Long", 1)]
    [InlineData("1000012", "Đồng Tháp", 1)]
    [InlineData("1000013", "An Giang", 1)]
    [InlineData("1000014", "Tuyên Quang", 3)]
    [InlineData("1000015", "Phú Thọ", 3)]
    [InlineData("1000016", "Ninh Bình", 3)]
    [InlineData("1000017", "Quảng Trị", 2)]
    [InlineData("1000018", "Tây Ninh", 1)]
    [InlineData("1000019", "Quảng Ngãi", 2)]
    [InlineData("1000020", "Thái Nguyên", 3)]
    [InlineData("1000021", "Bắc Ninh", 3)]
    [InlineData("1000022", "Cà Mau", 1)]
    [InlineData("1000023", "Hưng Yên", 3)]
    [InlineData("1000024", "Lào Cai", 3)]
    [InlineData("1000025", "Lai Châu", 3)]
    [InlineData("1000026", "Điện Biên", 3)]
    [InlineData("1000027", "Sơn La", 3)]
    [InlineData("1000028", "Quảng Ninh", 3)]
    [InlineData("1000029", "Hà Tĩnh", 3)]
    [InlineData("1000030", "Thanh Hóa", 3)]
    [InlineData("1000031", "Nghệ An", 3)]
    [InlineData("1000032", "Cao Bằng", 3)]
    [InlineData("1000033", "Lạng Sơn", 3)]
    // Tên chỉ để người đọc đối chiếu với sandbox. Luật tính phí chỉ dùng mã.
    public void Find_ReturnsRegionOfEachSandboxProvince(string provinceCode, string provinceName, int expectedRegion)
    {
        Assert.Equal((FallbackFeeRegion)expectedRegion, FallbackFeeRegions.Find(provinceCode));
        Assert.Contains(FallbackFeeRegions.All, entry => entry.ProvinceCode == provinceCode && entry.ProvinceName == provinceName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("201")]
    [InlineData("1000034")]
    [InlineData("1000099")]
    [InlineData(" 1000000")]
    [InlineData("abc")]
    public void Find_ReturnsNullForCodeOutsideTable(string provinceCode)
    {
        Assert.Null(FallbackFeeRegions.Find(provinceCode));
    }
}
