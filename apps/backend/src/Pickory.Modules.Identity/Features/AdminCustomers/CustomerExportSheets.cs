using Pickory.Shared.Exports;

namespace Pickory.Modules.Identity.Features.AdminCustomers;

// Một khách đọc từ DB cho file xuất. Họ tên và số điện thoại là null khi khách chưa nhập.
internal sealed record CustomerExportSource(
    string? Name,
    string Email,
    string? Phone,
    bool EmailVerified,
    DateTime? LockedAt,
    DateTime CreatedAt);

// Một khách ở sheet "Khách hàng". Mọi giá trị đã ở dạng hiển thị, nên cột chỉ đọc thuộc tính mà không tính thêm.
internal sealed record CustomerExportRow(
    string? Name,
    string Email,
    string? Phone,
    bool EmailVerified,
    string StatusLabel,
    DateTime? LockedAt,
    DateTime CreatedAt);

// Tên sheet, tiêu đề và thứ tự cột theo mục "File khách hàng" của `spec/SPEC-exports-printing.md`.
internal static class CustomerExportSheets
{
    public const string SheetName = "Khách hàng";

    private static readonly IReadOnlyList<ExportColumn<CustomerExportRow>> Columns =
    [
        // Họ tên và số điện thoại là null khi khách chưa nhập. Ô để trống, không ghi chuỗi rỗng.
        ExportColumn<CustomerExportRow>.Text("Họ tên", 28, row => row.Name),
        ExportColumn<CustomerExportRow>.Text("Email", 32, row => row.Email),
        ExportColumn<CustomerExportRow>.Text("Số điện thoại", 16, row => row.Phone),
        ExportColumn<CustomerExportRow>.YesNo("Đã xác minh email", 18, row => row.EmailVerified),
        ExportColumn<CustomerExportRow>.Text("Trạng thái", 14, row => row.StatusLabel),
        // Ngày khóa là null khi khách đang hoạt động. Ô để trống.
        ExportColumn<CustomerExportRow>.DateTime("Ngày khóa", 20, row => row.LockedAt),
        ExportColumn<CustomerExportRow>.DateTime("Ngày đăng ký", 20, row => row.CreatedAt),
    ];

    public static ExportSheet BuildSheet(IEnumerable<CustomerExportRow> rows) =>
        ExportSheet.Create(SheetName, Columns, rows);

    // Đổi khách đọc từ DB thành dòng hiển thị. Trạng thái đổi sang nhãn tiếng Việt, giá trị null giữ nguyên để ô trống.
    public static CustomerExportRow BuildRow(CustomerExportSource source) =>
        new(
            source.Name,
            source.Email,
            source.Phone,
            source.EmailVerified,
            CustomerExportLabels.ForLockedAt(source.LockedAt),
            source.LockedAt,
            source.CreatedAt);
}
