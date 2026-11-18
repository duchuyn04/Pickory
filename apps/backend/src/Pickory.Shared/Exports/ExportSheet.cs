namespace Pickory.Shared.Exports;

// Một sheet của file xuất. Sheet giữ dòng đã đọc ra giá trị ô, nên một file chứa được nhiều kiểu dòng khác nhau.
public sealed class ExportSheet
{
    private ExportSheet(string name, IReadOnlyList<ExportColumnLayout> columns, IEnumerable<object?[]> rows)
    {
        Name = name;
        Columns = columns;
        Rows = rows;
    }

    public string Name { get; }

    internal IReadOnlyList<ExportColumnLayout> Columns { get; }

    internal IEnumerable<object?[]> Rows { get; }

    public static ExportSheet Create<TRow>(string name, IReadOnlyList<ExportColumn<TRow>> columns, IEnumerable<TRow> rows)
    {
        // Sheet không có tên hoặc không có cột không tạo được dòng tiêu đề. Chặn sớm để lỗi nằm ở nơi khai báo.
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        if (columns.Count == 0)
        {
            throw new ArgumentException("Sheet cần ít nhất một cột.", nameof(columns));
        }

        // Chụp tiêu đề, kiểu và độ rộng một lần. Bộ ghi không cần biết kiểu dòng `TRow`.
        var layouts = columns.Select(column => new ExportColumnLayout(column.Title, column.Kind, column.Width)).ToList();
        // Đọc giá trị từng dòng khi bộ ghi duyệt tới. Không giữ thêm một bản sao toàn bộ dòng trong bộ nhớ.
        var cellRows = rows.Select(row => columns.Select(column => column.ReadValue(row)).ToArray());
        return new ExportSheet(name, layouts, cellRows);
    }
}

internal sealed record ExportColumnLayout(string Title, ExportCellKind Kind, double Width);
