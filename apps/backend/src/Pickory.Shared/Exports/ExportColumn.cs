namespace Pickory.Shared.Exports;

// Khai báo một cột của file xuất. Module chỉ dùng kiểu này, không thấy kiểu của thư viện ghi file.
public sealed class ExportColumn<TRow>
{
    private ExportColumn(string title, ExportCellKind kind, double width, Func<TRow, object?> readValue)
    {
        Title = title;
        Kind = kind;
        Width = width;
        ReadValue = readValue;
    }

    // Tiêu đề tiếng Việt có dấu, ghi ở dòng 1.
    public string Title { get; }

    public ExportCellKind Kind { get; }

    // Độ rộng tính theo số ký tự, đúng đơn vị của Excel.
    public double Width { get; }

    // Giá trị đã đúng kiểu của `Kind`. Chỉ bộ ghi trong `Pickory.Shared` đọc hàm này.
    internal Func<TRow, object?> ReadValue { get; }

    public static ExportColumn<TRow> Text(string title, double width, Func<TRow, string?> selector)
    {
        return Create(title, ExportCellKind.Text, width, row => selector(row));
    }

    public static ExportColumn<TRow> Integer(string title, double width, Func<TRow, long?> selector)
    {
        return Create(title, ExportCellKind.Integer, width, row => selector(row));
    }

    public static ExportColumn<TRow> Money(string title, double width, Func<TRow, long?> selector)
    {
        return Create(title, ExportCellKind.Money, width, row => selector(row));
    }

    // Giá trị là thời điểm UTC. Bộ ghi đổi sang giờ Việt Nam khi ghi ô.
    public static ExportColumn<TRow> DateTime(string title, double width, Func<TRow, System.DateTime?> selector)
    {
        return Create(title, ExportCellKind.DateTime, width, row => selector(row));
    }

    public static ExportColumn<TRow> YesNo(string title, double width, Func<TRow, bool?> selector)
    {
        return Create(title, ExportCellKind.YesNo, width, row => selector(row));
    }

    private static ExportColumn<TRow> Create(string title, ExportCellKind kind, double width, Func<TRow, object?> readValue)
    {
        // Tiêu đề rỗng tạo cột không có tên trong bộ lọc của Excel. Chặn sớm để lỗi nằm ở nơi khai báo cột.
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        // Độ rộng không dương làm cột bị ẩn hoặc lỗi file. Chặn sớm cùng lý do.
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        return new ExportColumn<TRow>(title, kind, width, readValue);
    }
}
