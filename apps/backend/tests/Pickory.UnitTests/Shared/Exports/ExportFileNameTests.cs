using Pickory.Shared.Exports;
using Xunit;

namespace Pickory.UnitTests.Shared.Exports;

// Tên file xuất theo giờ Việt Nam. Test truyền thời điểm vào nên không phụ thuộc giờ máy.
public sealed class ExportFileNameTests
{
    [Fact]
    // 08:30 UTC là 15:30 giờ Việt Nam cùng ngày.
    public void Create_UsesVietnamTime()
    {
        var name = ExportFileName.Create("don-hang", new DateTimeOffset(2026, 11, 10, 8, 30, 0, TimeSpan.Zero));

        Assert.Equal("don-hang-20261110-1530.xlsx", name);
    }

    [Fact]
    // 17:00 UTC là nửa đêm giờ Việt Nam, nên ngày trong tên file sang ngày kế tiếp.
    public void Create_AtVietnamMidnight_UsesNextDay()
    {
        var name = ExportFileName.Create("don-hang", new DateTimeOffset(2026, 11, 9, 17, 0, 0, TimeSpan.Zero));

        Assert.Equal("don-hang-20261110-0000.xlsx", name);
    }

    [Fact]
    // Thời điểm có offset khác 0 vẫn đổi đúng về giờ Việt Nam.
    public void Create_NonUtcOffset_StillConvertsToVietnamTime()
    {
        var name = ExportFileName.Create("khach-hang", new DateTimeOffset(2026, 11, 10, 3, 30, 0, TimeSpan.FromHours(-5)));

        Assert.Equal("khach-hang-20261110-1530.xlsx", name);
    }
}
