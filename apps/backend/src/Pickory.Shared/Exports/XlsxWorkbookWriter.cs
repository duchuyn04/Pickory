using ClosedXML.Excel;
using Pickory.Shared.Calendar;

namespace Pickory.Shared.Exports;

public static class XlsxWorkbookWriter
{
    private const string MoneyFormat = "#,##0";
    private const string DateTimeFormat = "dd/MM/yyyy HH:mm";
    private const string YesText = "Có";
    private const string NoText = "Không";

    public static byte[] Write(ExportSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return Write([sheet]);
    }

    public static byte[] Write(IReadOnlyList<ExportSheet> sheets)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        // File không có sheet là file Excel hỏng. Chặn sớm thay vì để thư viện báo lỗi khó hiểu.
        if (sheets.Count == 0)
        {
            throw new ArgumentException("File cần ít nhất một sheet.", nameof(sheets));
        }

        using var workbook = new XLWorkbook();
        // Thêm sheet theo đúng thứ tự khai báo. Người dùng thấy các tab theo thứ tự đó.
        foreach (var sheet in sheets)
        {
            WriteSheet(workbook, sheet);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteSheet(XLWorkbook workbook, ExportSheet sheet)
    {
        var worksheet = workbook.Worksheets.Add(sheet.Name);
        var columnCount = sheet.Columns.Count;

        // Ghi tiêu đề và độ rộng trước. Sheet không có dòng dữ liệu vẫn có dòng tiêu đề.
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            var excelColumn = columnIndex + 1;
            // Tiêu đề cũng là chữ. Gán chuỗi thì ClosedXML giữ kiểu chữ, nên tiêu đề bắt đầu bằng `=` không thành công thức.
            WriteTextCell(worksheet.Cell(1, excelColumn), sheet.Columns[columnIndex].Title);
            worksheet.Column(excelColumn).Width = sheet.Columns[columnIndex].Width;
        }

        // Ghi giá trị trước, đặt định dạng sau. Định dạng đặt một lần cho vùng dữ liệu của mỗi cột, không đặt từng ô.
        var lastRow = 1;
        foreach (var rowValues in sheet.Rows)
        {
            lastRow++;
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                WriteCell(worksheet.Cell(lastRow, columnIndex + 1), sheet.Columns[columnIndex].Kind, rowValues[columnIndex]);
            }
        }

        // Sheet rỗng không có dòng dữ liệu, nên không có vùng nào cần định dạng.
        if (lastRow > 1)
        {
            ApplyColumnFormats(worksheet, sheet, lastRow);
        }

        // Chữ đậm giúp người dùng phân biệt dòng tiêu đề với dữ liệu.
        worksheet.Range(1, 1, 1, columnCount).Style.Font.Bold = true;
        // Khóa dòng 1 để tiêu đề luôn hiện khi cuộn danh sách dài.
        worksheet.SheetView.FreezeRows(1);
        // Bộ lọc phải phủ cả dòng dữ liệu. Excel chỉ lọc và sắp xếp trong vùng đã lưu của bộ lọc.
        worksheet.Range(1, 1, lastRow, columnCount).SetAutoFilter();
    }

    private static void ApplyColumnFormats(IXLWorksheet worksheet, ExportSheet sheet, int lastRow)
    {
        for (var columnIndex = 0; columnIndex < sheet.Columns.Count; columnIndex++)
        {
            var dataRange = worksheet.Range(2, columnIndex + 1, lastRow, columnIndex + 1);
            switch (sheet.Columns[columnIndex].Kind)
            {
                case ExportCellKind.Money:
                    // Ô tiền là số để kế toán cộng trừ được. Định dạng chỉ đổi cách hiện.
                    dataRange.Style.NumberFormat.Format = MoneyFormat;
                    break;
                case ExportCellKind.DateTime:
                    // Hiện ngày giờ theo kiểu Việt Nam. Giá trị ô vẫn là ngày thật để sắp xếp được.
                    dataRange.Style.DateFormat.Format = DateTimeFormat;
                    break;
            }
        }
    }

    private static void WriteCell(IXLCell cell, ExportCellKind kind, object? value)
    {
        // Giá trị rỗng để ô trống, đúng với mọi kiểu ô.
        if (value is null)
        {
            return;
        }

        switch (kind)
        {
            case ExportCellKind.Text:
                WriteTextCell(cell, (string)value);
                break;
            case ExportCellKind.Integer:
            case ExportCellKind.Money:
                // Số nguyên và tiền đều là số thật của Excel. Định dạng tiền do `ApplyColumnFormats` đặt.
                cell.Value = (long)value;
                break;
            case ExportCellKind.DateTime:
                // Ghi giá trị ngày giờ thật theo giờ Việt Nam để người dùng sắp xếp và lọc theo ngày.
                cell.Value = VietnamCalendar.ToVietnamDateTime((DateTime)value);
                break;
            case ExportCellKind.YesNo:
                WriteTextCell(cell, (bool)value ? YesText : NoText);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Kiểu ô không được hỗ trợ.");
        }
    }

    private static void WriteTextCell(IXLCell cell, string text)
    {
        // Gán giá trị kiểu chữ. Nhờ vậy `=1+1` không thành công thức và `0109876543` giữ số 0 đầu.
        cell.Value = XLCellValue.FromObject(text);
    }
}
