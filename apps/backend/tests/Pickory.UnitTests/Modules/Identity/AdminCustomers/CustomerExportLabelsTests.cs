using Pickory.Modules.Identity.Features.AdminCustomers;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.AdminCustomers;

// Nhãn tiếng Việt của trạng thái khách trong file xuất, theo ví dụ `customers export success`.
public sealed class CustomerExportLabelsTests
{
    [Fact]
    // Khách chưa bị khóa có nhãn "Hoạt động".
    public void ForLockedAt_CustomerWithoutLockedAt_ReturnsActiveLabel()
    {
        Assert.Equal("Hoạt động", CustomerExportLabels.ForLockedAt(lockedAt: null));
    }

    [Fact]
    // Khách có `LockedAt` có nhãn "Đã khóa". Đổi chữ của nhãn làm test này đỏ.
    public void ForLockedAt_CustomerWithLockedAt_ReturnsLockedLabel()
    {
        Assert.Equal("Đã khóa", CustomerExportLabels.ForLockedAt(new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc)));
    }
}
