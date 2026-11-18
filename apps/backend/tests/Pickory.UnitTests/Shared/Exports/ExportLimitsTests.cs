using Pickory.Shared.Exceptions;
using Pickory.Shared.Exports;
using Xunit;

namespace Pickory.UnitTests.Shared.Exports;

public sealed class ExportLimitsTests
{
    [Fact]
    // Đúng 50.000 dòng vẫn được xuất.
    public void EnsureWithinLimit_AtMaxRows_DoesNotThrow()
    {
        var exception = Record.Exception(() => ExportLimits.EnsureWithinLimit(50_000));

        Assert.Null(exception);
    }

    [Fact]
    // Dòng thứ 50.001 làm danh sách vượt giới hạn. Route đọc `MaxRows + 1` dòng để phát hiện điều này.
    public void EnsureWithinLimit_OneOverMaxRows_ThrowsExportTooLarge()
    {
        var exception = Assert.Throws<AppException>(() => ExportLimits.EnsureWithinLimit(50_001));

        Assert.Equal("EXPORT_TOO_LARGE", exception.Code);
        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("Danh sách có hơn 50.000 dòng. Hãy thu hẹp bộ lọc rồi xuất lại.", exception.Message);
    }

    [Fact]
    public void MaxRows_Is50000()
    {
        Assert.Equal(50_000, ExportLimits.MaxRows);
    }

    [Fact]
    // Danh sách rỗng hợp lệ, vì route vẫn trả file chỉ có dòng tiêu đề.
    public void EnsureWithinLimit_Zero_DoesNotThrow()
    {
        var exception = Record.Exception(() => ExportLimits.EnsureWithinLimit(0));

        Assert.Null(exception);
    }

    [Fact]
    // Đúng bằng giới hạn cấu hình vẫn được xuất.
    public void EnsureWithinLimit_AtConfiguredLimit_DoesNotThrow()
    {
        var exception = Record.Exception(() => ExportLimits.EnsureWithinLimit(3, 3));

        Assert.Null(exception);
    }

    [Fact]
    // Vượt giới hạn cấu hình một dòng thì trả lỗi với chuỗi của contract, dù giới hạn không phải 50.000.
    public void EnsureWithinLimit_OneOverConfiguredLimit_ThrowsContractError()
    {
        var exception = Assert.Throws<AppException>(() => ExportLimits.EnsureWithinLimit(4, 3));

        Assert.Equal("EXPORT_TOO_LARGE", exception.Code);
        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("Danh sách có hơn 50.000 dòng. Hãy thu hẹp bộ lọc rồi xuất lại.", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    // Giới hạn không dương là lỗi cấu hình của người gọi, không phải lỗi 422 của người dùng.
    public void EnsureWithinLimit_NonPositiveLimit_ThrowsArgumentOutOfRange(int maxRows)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExportLimits.EnsureWithinLimit(1, maxRows));
    }
}
