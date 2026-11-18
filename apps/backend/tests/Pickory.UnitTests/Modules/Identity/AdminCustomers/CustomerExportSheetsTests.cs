using Pickory.Modules.Identity.Features.AdminCustomers;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.AdminCustomers;

// Dựng dòng hiển thị của sheet "Khách hàng" từ khách đọc từ DB.
public sealed class CustomerExportSheetsTests
{
    private static readonly DateTime CreatedAt = new(2026, 9, 15, 7, 20, 0, DateTimeKind.Utc);
    private static readonly DateTime LockedAt = new(2026, 10, 2, 1, 5, 0, DateTimeKind.Utc);

    [Fact]
    // Khách hoạt động giữ ngày khóa là null. Null làm ô Excel để trống.
    public void BuildRow_ActiveCustomer_HasActiveLabelAndNullLockedAt()
    {
        var source = new CustomerExportSource("Trần Thị Minh Thư", "minhthu@example.com", "0901234567", true, null, CreatedAt);

        var row = CustomerExportSheets.BuildRow(source);

        Assert.Equal("Trần Thị Minh Thư", row.Name);
        Assert.Equal("minhthu@example.com", row.Email);
        Assert.Equal("0901234567", row.Phone);
        Assert.True(row.EmailVerified);
        Assert.Equal("Hoạt động", row.StatusLabel);
        Assert.Null(row.LockedAt);
        Assert.Equal(CreatedAt, row.CreatedAt);
    }

    [Fact]
    // Khách bị khóa có nhãn "Đã khóa" và giữ nguyên thời điểm khóa.
    public void BuildRow_LockedCustomer_HasLockedLabelAndLockedAt()
    {
        var source = new CustomerExportSource("Lê Văn An", "an@example.com", "0912345678", false, LockedAt, CreatedAt);

        var row = CustomerExportSheets.BuildRow(source);

        Assert.False(row.EmailVerified);
        Assert.Equal("Đã khóa", row.StatusLabel);
        Assert.Equal(LockedAt, row.LockedAt);
    }

    [Fact]
    // Khách chưa nhập họ tên và số điện thoại giữ hai trường là null. Chuỗi rỗng sẽ khác ô trống.
    public void BuildRow_CustomerWithoutNameAndPhone_KeepsBothNull()
    {
        var source = new CustomerExportSource(null, "anon@example.com", null, false, null, CreatedAt);

        var row = CustomerExportSheets.BuildRow(source);

        Assert.Null(row.Name);
        Assert.Null(row.Phone);
        Assert.Equal("anon@example.com", row.Email);
    }
}
