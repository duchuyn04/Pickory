using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Pickory.Shared.Exports;
using Xunit;
using Xunit.Sdk;

namespace Pickory.IntegrationTests.Infrastructure;

// Test của chính `WorkbookFixture`. Test không dùng database, `Collection` chỉ để qua luật kiến trúc.
[Collection(CommerceCollection.Name)]
public sealed class WorkbookFixtureTests
{
    private const string ExportsFixture = "exports-printing/exports.json";
    private const string SuccessExample = "orders export success";
    private const string OrdersSheet = "Đơn hàng";
    private const string DetailSheet = "Chi tiết sản phẩm";
    private const string ValidContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string ValidDisposition = "attachment; filename=\"don-hang-20261115-0905.xlsx\"; filename*=UTF-8''don-hang-20261115-0905.xlsx";

    private static JsonObject SuccessExampleNode()
    {
        return FixtureFiles.FindExample(ExportsFixture, SuccessExample);
    }

    // Dựng file bằng `XlsxWorkbookWriter` từ chính `workbook` của ví dụ. Cột và dòng 2 lấy từ `header` và `sampleRow`.
    private static byte[] BuildMatchingFile(bool includeDataRow = true)
    {
        var sheets = SuccessExampleNode()["workbook"]!["sheets"]!.AsArray()
            .Select(sheetNode => BuildSheet(sheetNode!, includeDataRow))
            .ToList();
        return XlsxWorkbookWriter.Write(sheets);
    }

    private static ExportSheet BuildSheet(JsonNode sheetNode, bool includeDataRow)
    {
        var header = sheetNode["header"]!.AsArray();
        var sampleRow = sheetNode["sampleRow"]!.AsArray();
        var columns = header
            .Select((title, index) => ColumnFor(title!.GetValue<string>(), index, sampleRow[index]!))
            .ToList();
        var rows = includeDataRow ? new[] { sampleRow.Select(ValueOf).ToArray() } : [];
        return ExportSheet.Create(sheetNode["name"]!.GetValue<string>(), columns, rows);
    }

    private static ExportColumn<object?[]> ColumnFor(string title, int index, JsonNode sample)
    {
        var sampleValue = sample.AsValue();
        if (sampleValue.TryGetValue<long>(out _))
        {
            return ExportColumn<object?[]>.Integer(title, 16, row => (long?)row[index]);
        }

        if (sampleValue.TryGetValue<string>(out var text) && IsDateTimeText(text))
        {
            return ExportColumn<object?[]>.DateTime(title, 18, row => (DateTime?)row[index]);
        }

        return ExportColumn<object?[]>.Text(title, 20, row => (string?)row[index]);
    }

    private static object? ValueOf(JsonNode? sample)
    {
        var sampleValue = sample!.AsValue();
        if (sampleValue.TryGetValue<long>(out var number))
        {
            return number;
        }

        var text = sampleValue.GetValue<string>();
        if (!IsDateTimeText(text))
        {
            return text;
        }

        // Helper không so giá trị ô. Mọi cột ngày giờ dùng một thời điểm UTC cố định.
        return new DateTime(2026, 11, 9, 3, 15, 0, DateTimeKind.Utc);
    }

    private static bool IsDateTimeText(string text)
    {
        return DateTime.TryParseExact(text, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    // Mở file, sửa bằng ClosedXML rồi lưu lại. Mỗi test tạo đúng một lỗi từ file khớp hoàn toàn.
    private static byte[] ChangeWorkbook(byte[] file, Action<XLWorkbook> change)
    {
        using var workbook = new XLWorkbook(new MemoryStream(file));
        change(workbook);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // Số thứ tự cột (bắt đầu từ 1) của một tiêu đề trong fixture. Test không ghi cứng vị trí cột.
    private static int ColumnOf(string sheetName, string title)
    {
        var sheetNode = SuccessExampleNode()["workbook"]!["sheets"]!.AsArray().Single(sheet => sheet!["name"]!.GetValue<string>() == sheetName)!;
        var titles = sheetNode["header"]!.AsArray().Select(node => node!.GetValue<string>()).ToList();
        return titles.IndexOf(title) + 1;
    }

    private static HttpResponseMessage XlsxResponse(string? contentType = ValidContentType, string? disposition = ValidDisposition)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        // Ghi header thô, không qua bộ kiểm của .NET. Test cần header sai mẫu mà .NET có thể từ chối hoặc sửa lại.
        if (contentType is not null)
        {
            response.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        if (disposition is not null)
        {
            response.Content.Headers.TryAddWithoutValidation("Content-Disposition", disposition);
        }

        return response;
    }

    private static string FailureMessage(Action assertion)
    {
        return Assert.ThrowsAny<XunitException>(assertion).Message;
    }

    [Fact]
    // Tên ví dụ sai là lỗi của người viết test. Thông báo nêu tên đã tìm và các tên có trong file.
    public void UnknownExampleName_FailsWithSearchedNameAndAvailableNames()
    {
        using var response = new HttpResponseMessage();

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, "khong co vi du nay", [], response));

        Assert.Contains("khong co vi du nay", message, StringComparison.Ordinal);
        Assert.Contains(SuccessExample, message, StringComparison.Ordinal);
    }

    [Fact]
    // Đường xanh: file dựng bằng `XlsxWorkbookWriter` từ chính ví dụ phải khớp. Tên file khác ngày giờ vẫn khớp mẫu.
    public void MatchingFile_Passes()
    {
        using var response = XlsxResponse();

        WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, BuildMatchingFile(), response);
    }

    [Fact]
    // Helper không so giá trị ô. Số khác trong ô số vẫn khớp.
    public void DifferentValueOfSameCellType_Passes()
    {
        var file = ChangeWorkbook(BuildMatchingFile(), workbook =>
            workbook.Worksheet(OrdersSheet).Cell(2, ColumnOf(OrdersSheet, "Tạm tính")).Value = 1);
        using var response = XlsxResponse();

        WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, file, response);
    }

    [Fact]
    // File thiếu sheet là file thiếu dữ liệu. Thông báo nêu tên sheet thiếu.
    public void MissingSheet_FailsWithSheetName()
    {
        var file = ChangeWorkbook(BuildMatchingFile(), workbook => workbook.Worksheet(DetailSheet).Delete());
        using var response = XlsxResponse();

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, file, response));

        Assert.Contains("thiếu sheet", message, StringComparison.Ordinal);
        Assert.Contains(DetailSheet, message, StringComparison.Ordinal);
    }

    [Fact]
    // File có sheet ngoài contract là lỗi. Thông báo nêu tên sheet thừa.
    public void ExtraSheet_FailsWithSheetName()
    {
        var file = ChangeWorkbook(BuildMatchingFile(), workbook => workbook.AddWorksheet("Ghi chú nội bộ"));
        using var response = XlsxResponse();

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, file, response));

        Assert.Contains("thừa sheet", message, StringComparison.Ordinal);
        Assert.Contains("Ghi chú nội bộ", message, StringComparison.Ordinal);
    }

    [Fact]
    // Hai sheet đúng tên nhưng đổi chỗ vẫn là lỗi. Người dùng mở file thấy tab sai thứ tự.
    public void SheetsInWrongOrder_Fails()
    {
        var file = ChangeWorkbook(BuildMatchingFile(), workbook => workbook.Worksheet(DetailSheet).Position = 1);
        using var response = XlsxResponse();

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, file, response));

        Assert.Contains("thứ tự", message, StringComparison.Ordinal);
    }

    [Fact]
    // Sai tiêu đề một cột thì thông báo nêu tên sheet, vị trí cột, tiêu đề mong đợi và tiêu đề thực tế.
    public void WrongHeaderCell_FailsWithSheetColumnAndTitles()
    {
        var file = ChangeWorkbook(BuildMatchingFile(), workbook =>
            workbook.Worksheet(OrdersSheet).Cell(1, 3).Value = "Tình trạng đơn");
        using var response = XlsxResponse();

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, file, response));

        Assert.Contains(OrdersSheet, message, StringComparison.Ordinal);
        Assert.Contains("cột 3 (C)", message, StringComparison.Ordinal);
        Assert.Contains("Trạng thái đơn", message, StringComparison.Ordinal);
        Assert.Contains("Tình trạng đơn", message, StringComparison.Ordinal);
    }

    [Fact]
    // File thừa một cột tiêu đề so với fixture là lỗi. Thông báo nêu vị trí cột thừa.
    public void ExtraHeaderColumn_Fails()
    {
        var file = ChangeWorkbook(BuildMatchingFile(), workbook =>
            workbook.Worksheet(DetailSheet).Cell(1, 9).Value = "Cột thừa");
        using var response = XlsxResponse();

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, file, response));

        Assert.Contains(DetailSheet, message, StringComparison.Ordinal);
        Assert.Contains("cột 9 (I)", message, StringComparison.Ordinal);
        Assert.Contains("Cột thừa", message, StringComparison.Ordinal);
    }

    [Fact]
    // Ô tiền kiểu chữ làm kế toán không cộng được. Thông báo nêu cột và hai kiểu.
    public void MoneyCellStoredAsText_Fails()
    {
        var moneyColumn = ColumnOf(OrdersSheet, "Tạm tính");
        var file = ChangeWorkbook(BuildMatchingFile(), workbook =>
            workbook.Worksheet(OrdersSheet).Cell(2, moneyColumn).Value = "25370000");
        using var response = XlsxResponse();

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, file, response));

        Assert.Contains(OrdersSheet, message, StringComparison.Ordinal);
        Assert.Contains("Tạm tính", message, StringComparison.Ordinal);
        Assert.Contains("mong đợi kiểu số", message, StringComparison.Ordinal);
        Assert.Contains("thực tế kiểu chữ", message, StringComparison.Ordinal);
    }

    [Fact]
    // Ô ngày giờ là chuỗi thì người dùng không sắp xếp và lọc theo ngày được.
    public void DateTimeCellStoredAsText_Fails()
    {
        var dateColumn = ColumnOf(OrdersSheet, "Ngày đặt");
        var file = ChangeWorkbook(BuildMatchingFile(), workbook =>
            workbook.Worksheet(OrdersSheet).Cell(2, dateColumn).Value = "09/11/2026 10:15");
        using var response = XlsxResponse();

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, file, response));

        Assert.Contains("Ngày đặt", message, StringComparison.Ordinal);
        Assert.Contains("mong đợi kiểu ngày giờ", message, StringComparison.Ordinal);
        Assert.Contains("thực tế kiểu chữ", message, StringComparison.Ordinal);
    }

    [Fact]
    // Mã số thuế là chuỗi. Ô số làm mất số 0 đầu, nên helper không chấp nhận.
    public void TextCellStoredAsNumber_Fails()
    {
        var taxColumn = ColumnOf(OrdersSheet, "Mã số thuế");
        var file = ChangeWorkbook(BuildMatchingFile(), workbook =>
            workbook.Worksheet(OrdersSheet).Cell(2, taxColumn).Value = 109876543);
        using var response = XlsxResponse();

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, file, response));

        Assert.Contains("Mã số thuế", message, StringComparison.Ordinal);
        Assert.Contains("mong đợi kiểu chữ hoặc trống", message, StringComparison.Ordinal);
        Assert.Contains("thực tế kiểu số", message, StringComparison.Ordinal);
    }

    [Fact]
    // Cột chữ để trống là hợp lệ. Cột ghi chú đơn thường không có dữ liệu.
    public void TextColumnLeftBlank_Passes()
    {
        var noteColumn = ColumnOf(OrdersSheet, "Ghi chú đơn");
        var file = ChangeWorkbook(BuildMatchingFile(), workbook =>
            workbook.Worksheet(OrdersSheet).Cell(2, noteColumn).Clear());
        using var response = XlsxResponse();

        WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, file, response);
    }

    [Fact]
    // Dòng 2 nhiều ô hơn số cột tiêu đề là dữ liệu tràn ra ngoài bảng.
    public void DataRowWiderThanHeader_Fails()
    {
        var file = ChangeWorkbook(BuildMatchingFile(), workbook =>
            workbook.Worksheet(DetailSheet).Cell(2, 9).Value = "tràn");
        using var response = XlsxResponse();

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, file, response));

        Assert.Contains(DetailSheet, message, StringComparison.Ordinal);
        Assert.Contains("dòng 2", message, StringComparison.Ordinal);
    }

    [Fact]
    // Sheet chỉ có tiêu đề (không có dòng dữ liệu) vẫn khớp. Danh sách rỗng là kết quả hợp lệ.
    public void SheetWithHeaderOnly_Passes()
    {
        using var response = XlsxResponse();

        WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, BuildMatchingFile(includeDataRow: false), response);
    }

    [Fact]
    // `sampleRow` có `null` ở một cột thì cột đó nhận mọi kiểu ô.
    public void NullInSampleRow_AcceptsAnyCellType()
    {
        var example = (JsonObject)SuccessExampleNode().DeepClone();
        example["workbook"]!["sheets"]![0]!["sampleRow"]![0] = null;
        var file = ChangeWorkbook(BuildMatchingFile(), workbook =>
            workbook.Worksheet(OrdersSheet).Cell(2, 1).Value = 12345);
        using var response = XlsxResponse();

        WorkbookFixture.AssertExampleMatches(ExportsFixture, example, file, response);
    }

    [Fact]
    // Thiếu dấu ngoặc kép quanh `filename` là sai contract. Trình duyệt đọc tên file có dấu cách sai.
    public void ContentDispositionWithoutQuotes_Fails()
    {
        using var response = XlsxResponse(disposition: "attachment; filename=don-hang-20261115-0905.xlsx; filename*=UTF-8''don-hang-20261115-0905.xlsx");

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, BuildMatchingFile(), response));

        Assert.Contains("Content-Disposition", message, StringComparison.Ordinal);
    }

    [Fact]
    // Tiền tố tên file sai (`ban-hang` thay cho `don-hang`) không khớp mẫu, dù phần ngày giờ đúng định dạng.
    public void ContentDispositionWithWrongFileNamePrefix_Fails()
    {
        using var response = XlsxResponse(disposition: "attachment; filename=\"ban-hang-20261115-0905.xlsx\"; filename*=UTF-8''ban-hang-20261115-0905.xlsx");

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, BuildMatchingFile(), response));

        Assert.Contains("Content-Disposition", message, StringComparison.Ordinal);
    }

    [Fact]
    // `filename` và `filename*` là hai cách ghi cùng một tên. Hai tên khác nhau là lỗi.
    public void ContentDispositionWithDifferentFileNames_Fails()
    {
        using var response = XlsxResponse(disposition: "attachment; filename=\"don-hang-20261115-0905.xlsx\"; filename*=UTF-8''don-hang-20261116-0905.xlsx");

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, BuildMatchingFile(), response));

        Assert.Contains("Content-Disposition", message, StringComparison.Ordinal);
        Assert.Contains("hai tên file", message, StringComparison.Ordinal);
    }

    [Fact]
    // Thiếu `Content-Disposition` thì trình duyệt không tải file về với tên đúng.
    public void MissingContentDisposition_Fails()
    {
        using var response = XlsxResponse(disposition: null);

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, BuildMatchingFile(), response));

        Assert.Contains("Content-Disposition", message, StringComparison.Ordinal);
        Assert.Contains("thiếu header", message, StringComparison.Ordinal);
    }

    [Fact]
    // `Content-Type` so đúng chuỗi. Loại file khác là lỗi.
    public void WrongContentType_Fails()
    {
        using var response = XlsxResponse(contentType: "application/octet-stream");

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, BuildMatchingFile(), response));

        Assert.Contains("Content-Type", message, StringComparison.Ordinal);
        Assert.Contains("application/octet-stream", message, StringComparison.Ordinal);
    }

    [Fact]
    // Ví dụ lỗi không có `workbook`. Helper nói rõ đây không phải ví dụ file Excel và không so gì thêm.
    public void ExampleWithoutWorkbook_FailsWithClearMessage()
    {
        using var response = XlsxResponse();

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, "orders export too large", BuildMatchingFile(), response));

        Assert.Contains("không phải ví dụ file Excel", message, StringComparison.Ordinal);
        Assert.Contains("orders export too large", message, StringComparison.Ordinal);
    }

    [Fact]
    // Mảng byte không phải file `.xlsx` thì thông báo nói rõ, không ném lỗi của ClosedXML.
    public void BytesThatAreNotXlsx_FailsWithClearMessage()
    {
        using var response = XlsxResponse();

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, [1, 2, 3], response));

        Assert.Contains("không mở được", message, StringComparison.Ordinal);
    }

    [Fact]
    // Helper gom mọi lỗi của một lần so. Người sửa route thấy hết lỗi trong một lần chạy.
    public void SeveralMismatches_AreReportedTogether()
    {
        var file = ChangeWorkbook(BuildMatchingFile(), workbook =>
        {
            workbook.Worksheet(DetailSheet).Delete();
            workbook.Worksheet(OrdersSheet).Cell(1, 3).Value = "Tình trạng đơn";
            workbook.Worksheet(OrdersSheet).Cell(2, ColumnOf(OrdersSheet, "Tạm tính")).Value = "25370000";
        });
        using var response = XlsxResponse(contentType: "application/octet-stream");

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, file, response));

        Assert.Contains("Content-Type", message, StringComparison.Ordinal);
        Assert.Contains("thiếu sheet", message, StringComparison.Ordinal);
        Assert.Contains("cột 3 (C)", message, StringComparison.Ordinal);
        Assert.Contains("Tạm tính", message, StringComparison.Ordinal);
    }

    private static JsonObject CloneExample(Action<JsonObject> change)
    {
        var example = (JsonObject)SuccessExampleNode().DeepClone();
        change(example);
        return example;
    }

    [Fact]
    // Response lỗi (500) kèm đúng header và đúng file vẫn là sai. Thông báo nêu status mong đợi và thực tế.
    public void WrongStatus_FailsWithExpectedAndActualStatus()
    {
        using var response = XlsxResponse();
        response.StatusCode = HttpStatusCode.InternalServerError;

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, BuildMatchingFile(), response));

        Assert.Contains("status", message, StringComparison.Ordinal);
        Assert.Contains("200", message, StringComparison.Ordinal);
        Assert.Contains("500", message, StringComparison.Ordinal);
    }

    [Fact]
    // Status sai và header sai xuất hiện cùng lúc trong một thông báo.
    public void WrongStatus_AndWrongContentType_AreReportedTogether()
    {
        using var response = XlsxResponse(contentType: "application/octet-stream");
        response.StatusCode = HttpStatusCode.InternalServerError;

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, BuildMatchingFile(), response));

        Assert.Contains("status", message, StringComparison.Ordinal);
        Assert.Contains("Content-Type", message, StringComparison.Ordinal);
    }

    [Fact]
    // Thiếu hẳn `Content-Type` thì báo thiếu header, không báo so sai chuỗi.
    public void MissingContentType_FailsWithMissingHeader()
    {
        using var response = XlsxResponse(contentType: null);

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, BuildMatchingFile(), response));

        Assert.Contains("Content-Type: thiếu header", message, StringComparison.Ordinal);
    }

    [Fact]
    // Quyết định: `Content-Type` so đúng chuỗi. Giá trị có thêm `; charset=utf-8` không khớp, vì contract không có phần này.
    public void ContentTypeWithCharset_Fails()
    {
        using var response = XlsxResponse(contentType: ValidContentType + "; charset=utf-8");

        var message = FailureMessage(() => WorkbookFixture.AssertMatches(ExportsFixture, SuccessExample, BuildMatchingFile(), response));

        Assert.Contains("Content-Type", message, StringComparison.Ordinal);
        Assert.Contains("charset=utf-8", message, StringComparison.Ordinal);
    }

    [Fact]
    // `sampleRow` có `true` là fixture sai. Lỗi mở đầu bằng "Fixture sai" và không lẫn lỗi của route, dù response cũng sai.
    public void SampleRowWithBoolean_FailsAsFixtureFault_NotMixedWithRouteMismatches()
    {
        var example = CloneExample(node => node["workbook"]!["sheets"]![0]!["sampleRow"]![0] = true);
        using var response = XlsxResponse(contentType: "application/octet-stream");

        var message = FailureMessage(() => WorkbookFixture.AssertExampleMatches(ExportsFixture, example, BuildMatchingFile(), response));

        Assert.StartsWith("Fixture sai, không phải lỗi của route:", message, StringComparison.Ordinal);
        Assert.Contains("workbook.sheets[0].sampleRow[0]", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Content-Type", message, StringComparison.Ordinal);
    }

    [Fact]
    // Fixture sai được báo ngay cả khi file không có dòng dữ liệu. Helper kiểm fixture trước khi so file.
    public void SampleRowWithBoolean_FailsEvenWhenFileHasHeaderOnly()
    {
        var example = CloneExample(node => node["workbook"]!["sheets"]![0]!["sampleRow"]![0] = true);
        using var response = XlsxResponse();

        var message = FailureMessage(() => WorkbookFixture.AssertExampleMatches(ExportsFixture, example, BuildMatchingFile(includeDataRow: false), response));

        Assert.StartsWith("Fixture sai", message, StringComparison.Ordinal);
    }

    [Fact]
    // `sampleRow` dài hơn `header` là fixture tự mâu thuẫn. Thông báo nêu số ô và số cột.
    public void SampleRowLongerThanHeader_FailsAsFixtureFault()
    {
        var example = CloneExample(node => node["workbook"]!["sheets"]![1]!["sampleRow"]!.AsArray().Add("thừa"));
        using var response = XlsxResponse();

        var message = FailureMessage(() => WorkbookFixture.AssertExampleMatches(ExportsFixture, example, BuildMatchingFile(), response));

        Assert.StartsWith("Fixture sai, không phải lỗi của route:", message, StringComparison.Ordinal);
        Assert.Contains("9 ô", message, StringComparison.Ordinal);
        Assert.Contains("8 cột", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("status", "khóa `status`")]
    [InlineData("headers", "khóa `headers`")]
    [InlineData("workbook", "khóa `workbook.sheets`")]
    // Fixture thiếu khóa bắt buộc thì báo tiếng Việt, không để ngoại lệ thô của JSON lọt ra.
    public void FixtureMissingRequiredKey_FailsAsFixtureFault(string keyToRemove, string expectedLocation)
    {
        var example = CloneExample(node =>
        {
            if (keyToRemove == "workbook")
            {
                node["workbook"]!.AsObject().Remove("sheets");
            }
            else
            {
                node.Remove(keyToRemove);
            }
        });
        using var response = XlsxResponse();

        var message = FailureMessage(() => WorkbookFixture.AssertExampleMatches(ExportsFixture, example, BuildMatchingFile(), response));

        Assert.StartsWith("Fixture sai, không phải lỗi của route:", message, StringComparison.Ordinal);
        Assert.Contains(expectedLocation, message, StringComparison.Ordinal);
    }
}
