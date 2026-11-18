using System.Globalization;
using ClosedXML.Excel;
using Pickory.Shared.Exports;
using Xunit;

namespace Pickory.UnitTests.Shared.Exports;

// Test đọc lại mảng byte bằng ClosedXML để chứng minh file mở được và ô đúng kiểu, đúng định dạng.
public sealed class XlsxWorkbookWriterTests
{
    private sealed record SampleRow(
        string? TaxCode,
        long? Quantity,
        int? Total,
        DateTime? OrderedAt,
        bool? IsPaid);

    private static readonly IReadOnlyList<ExportColumn<SampleRow>> SampleColumns =
    [
        ExportColumn<SampleRow>.Text("Mã số thuế", 18, row => row.TaxCode),
        ExportColumn<SampleRow>.Integer("Số lượng", 10, row => row.Quantity),
        ExportColumn<SampleRow>.Money("Tổng tiền", 14, row => row.Total),
        ExportColumn<SampleRow>.DateTime("Ngày đặt", 20, row => row.OrderedAt),
        ExportColumn<SampleRow>.YesNo("Đã thanh toán", 16, row => row.IsPaid),
    ];

    private static XLWorkbook Read(byte[] bytes)
    {
        return new XLWorkbook(new MemoryStream(bytes));
    }

    // Trả workbook để test giữ bằng `using var`, nên file đọc lại được giải phóng sau mỗi test.
    private static XLWorkbook WriteAndOpen(params SampleRow[] rows)
    {
        var sheet = ExportSheet.Create("Đơn hàng", SampleColumns, rows);
        return Read(XlsxWorkbookWriter.Write(sheet));
    }

    private static SampleRow RowWithTaxCode(string? taxCode)
    {
        return new SampleRow(taxCode, null, null, null, null);
    }

    [Fact]
    // Dòng 1 là tiêu đề đậm, được khóa khi cuộn. Bộ lọc phủ từ tiêu đề tới dòng dữ liệu cuối.
    public void HeaderRow_IsBold_Frozen_AndFilterCoversData()
    {
        using var workbook = WriteAndOpen(RowWithTaxCode("0109876543"), RowWithTaxCode("0100000001"));
        var worksheet = workbook.Worksheet(1);

        Assert.All(worksheet.Range("A1:E1").Cells(), cell => Assert.True(cell.Style.Font.Bold));
        Assert.False(worksheet.Cell("A2").Style.Font.Bold);
        Assert.Equal(1, worksheet.SheetView.SplitRow);
        Assert.True(worksheet.AutoFilter.IsEnabled);
        Assert.Equal("A1:E3", worksheet.AutoFilter.Range.RangeAddress.ToStringRelative());
    }

    [Fact]
    // Ô tiền là số, nên kế toán cộng trừ được. Định dạng chỉ đổi cách hiện.
    public void MoneyCell_IsNumber_WithThousandsFormat()
    {
        using var workbook = WriteAndOpen(new SampleRow(null, null, 1_250_000, null, null));
        var worksheet = workbook.Worksheet(1);

        var cell = worksheet.Cell("C2");
        Assert.Equal(XLDataType.Number, cell.DataType);
        Assert.Equal(1_250_000d, cell.GetDouble());
        Assert.Equal("#,##0", cell.Style.NumberFormat.Format);
    }

    [Fact]
    // Định dạng phải phủ mọi dòng dữ liệu, kể cả dòng cuối. Cột số nguyên không được nhận định dạng tiền.
    public void ColumnFormats_CoverEveryDataRow_AndStayOnTheirOwnColumn()
    {
        var moment = new DateTime(2026, 11, 9, 17, 0, 0, DateTimeKind.Utc);
        using var workbook = WriteAndOpen(
            new SampleRow(null, 1, 1_000, moment, null),
            new SampleRow(null, 2, 2_000, moment, null),
            new SampleRow(null, 3, 3_000, moment, null));
        var worksheet = workbook.Worksheet(1);

        Assert.Equal("#,##0", worksheet.Cell("C2").Style.NumberFormat.Format);
        Assert.Equal("#,##0", worksheet.Cell("C4").Style.NumberFormat.Format);
        Assert.Equal("dd/MM/yyyy HH:mm", worksheet.Cell("D2").Style.DateFormat.Format);
        Assert.Equal("dd/MM/yyyy HH:mm", worksheet.Cell("D4").Style.DateFormat.Format);
        Assert.NotEqual("#,##0", worksheet.Cell("B4").Style.NumberFormat.Format);
    }

    [Fact]
    // Ô số nguyên là số, và giá trị `int` được nhận qua `long?`.
    public void IntegerCell_IsNumber()
    {
        using var workbook = WriteAndOpen(new SampleRow(null, 42, null, null, null));
        var worksheet = workbook.Worksheet(1);

        var cell = worksheet.Cell("B2");
        Assert.Equal(XLDataType.Number, cell.DataType);
        Assert.Equal(42d, cell.GetDouble());
    }

    [Theory]
    // 16:59 UTC vẫn là 23:59 ngày 9. 17:00 UTC là nửa đêm, nên sang 00:00 ngày 10.
    [InlineData("2026-11-09T16:59:00Z", "2026-11-09T23:59:00", "09/11/2026 23:59")]
    [InlineData("2026-11-09T17:00:00Z", "2026-11-10T00:00:00", "10/11/2026 00:00")]
    public void DateTimeCell_IsRealDateInVietnamTime(string utcText, string expectedVietnamText, string expectedText)
    {
        var utc = DateTime.Parse(utcText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var expectedVietnamTime = DateTime.Parse(expectedVietnamText, CultureInfo.InvariantCulture, DateTimeStyles.None);
        using var workbook = WriteAndOpen(new SampleRow(null, null, null, utc, null));
        var worksheet = workbook.Worksheet(1);

        var cell = worksheet.Cell("D2");
        Assert.Equal(XLDataType.DateTime, cell.DataType);
        // So giá trị ngày giờ đọc lại, vì định dạng `HH:mm` che mất lỗi lệch giây.
        Assert.Equal(expectedVietnamTime, cell.GetDateTime());
        // Truyền culture bất biến để test không đổi kết quả theo ngôn ngữ của máy chạy.
        Assert.Equal(expectedText, cell.GetFormattedString(CultureInfo.InvariantCulture));
        Assert.Equal("dd/MM/yyyy HH:mm", cell.Style.DateFormat.Format);
    }

    [Fact]
    // Chuỗi bắt đầu bằng `=` là chữ, không phải công thức.
    public void TextCell_StartingWithEquals_IsTextWithoutFormula()
    {
        using var workbook = WriteAndOpen(RowWithTaxCode("=1+1"));
        var worksheet = workbook.Worksheet(1);

        var cell = worksheet.Cell("A2");
        Assert.False(cell.HasFormula);
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.Equal("=1+1", cell.GetText());
    }

    [Fact]
    // Mã số thuế có số 0 đầu phải giữ nguyên là chữ.
    public void TextCell_WithLeadingZero_KeepsExactText()
    {
        using var workbook = WriteAndOpen(RowWithTaxCode("0109876543"));
        var worksheet = workbook.Worksheet(1);

        var cell = worksheet.Cell("A2");
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.Equal("0109876543", cell.GetText());
    }

    [Fact]
    // Giá trị `true` ghi `Có`, `false` ghi `Không`, `null` để ô trống.
    public void YesNoCell_WritesVietnameseWords_AndNullIsEmpty()
    {
        using var workbook = WriteAndOpen(
            new SampleRow(null, null, null, null, true),
            new SampleRow(null, null, null, null, false),
            new SampleRow(null, null, null, null, null));
        var worksheet = workbook.Worksheet(1);

        Assert.Equal("Có", worksheet.Cell("E2").GetText());
        Assert.Equal("Không", worksheet.Cell("E3").GetText());
        Assert.True(worksheet.Cell("E4").IsEmpty());
    }

    [Fact]
    // Giá trị `null` để ô trống ở mọi kiểu cột.
    public void NullValue_LeavesEveryCellKindEmpty()
    {
        using var workbook = WriteAndOpen(new SampleRow(null, null, null, null, null));
        var worksheet = workbook.Worksheet(1);

        Assert.All(worksheet.Range("A2:E2").Cells(), cell => Assert.True(cell.IsEmpty()));
    }

    [Fact]
    // Tên sheet và tiêu đề có dấu đọc lại đúng từng ký tự.
    public void VietnameseSheetNameAndHeaders_RoundTripExactly()
    {
        using var workbook = WriteAndOpen();
        var worksheet = workbook.Worksheet(1);

        Assert.Equal("Đơn hàng", worksheet.Name);
        Assert.Equal(
            new[] { "Mã số thuế", "Số lượng", "Tổng tiền", "Ngày đặt", "Đã thanh toán" },
            worksheet.Row(1).Cells(1, 5).Select(cell => cell.GetText()).ToArray());
    }

    [Fact]
    // Sheet không có dòng dữ liệu vẫn có dòng tiêu đề, khóa dòng và bộ lọc.
    public void EmptySheet_HasOnlyHeaderRow()
    {
        using var workbook = WriteAndOpen();
        var worksheet = workbook.Worksheet(1);

        Assert.Equal(1, worksheet.LastRowUsed()!.RowNumber());
        Assert.Equal("Mã số thuế", worksheet.Cell("A1").GetText());
        Assert.Equal(1, worksheet.SheetView.SplitRow);
        Assert.True(worksheet.AutoFilter.IsEnabled);
        Assert.Equal("A1:E1", worksheet.AutoFilter.Range.RangeAddress.ToStringRelative());
    }

    [Fact]
    // File nhiều sheet giữ đúng thứ tự và tên, và mỗi sheet có kiểu dòng riêng.
    public void MultipleSheets_KeepOrderAndNames()
    {
        var first = ExportSheet.Create("Đơn hàng", SampleColumns, [RowWithTaxCode("0109876543")]);
        var second = ExportSheet.Create(
            "Sản phẩm",
            [ExportColumn<string>.Text("Tên sản phẩm", 30, name => name)],
            ["Áo thun"]);

        using var workbook = Read(XlsxWorkbookWriter.Write([first, second]));

        Assert.Equal(new[] { "Đơn hàng", "Sản phẩm" }, workbook.Worksheets.Select(sheet => sheet.Name).ToArray());
        Assert.Equal("Áo thun", workbook.Worksheet("Sản phẩm").Cell("A2").GetText());
    }

    [Fact]
    // Độ rộng khai báo của cột được ghi vào file, theo đơn vị ký tự của Excel.
    public void ColumnWidths_AreWrittenInExcelCharacterUnits()
    {
        using var workbook = WriteAndOpen();
        var worksheet = workbook.Worksheet(1);

        Assert.Equal(18d, worksheet.Column(1).Width, 1);
        Assert.Equal(10d, worksheet.Column(2).Width, 1);
        Assert.Equal(14d, worksheet.Column(3).Width, 1);
        Assert.Equal(20d, worksheet.Column(4).Width, 1);
        Assert.Equal(16d, worksheet.Column(5).Width, 1);
    }

    [Fact]
    // Tiêu đề bắt đầu bằng `=` cũng không được thành công thức.
    public void HeaderStartingWithEquals_IsText()
    {
        var sheet = ExportSheet.Create(
            "Kiểm tra",
            [ExportColumn<string>.Text("=1+1", 10, value => value)],
            Array.Empty<string>());

        using var workbook = Read(XlsxWorkbookWriter.Write(sheet));
        var worksheet = workbook.Worksheet(1);

        Assert.False(worksheet.Cell("A1").HasFormula);
        Assert.Equal("=1+1", worksheet.Cell("A1").GetText());
    }

    [Fact]
    // Sheet không có cột không tạo được dòng tiêu đề, nên bị chặn khi tạo.
    public void ExportSheetCreate_WithoutColumns_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ExportSheet.Create("Rỗng", Array.Empty<ExportColumn<string>>(), Array.Empty<string>()));
    }

    [Fact]
    // File không có sheet là file hỏng, nên bị chặn.
    public void Write_WithoutSheets_Throws()
    {
        Assert.Throws<ArgumentException>(() => XlsxWorkbookWriter.Write(Array.Empty<ExportSheet>()));
    }
}
