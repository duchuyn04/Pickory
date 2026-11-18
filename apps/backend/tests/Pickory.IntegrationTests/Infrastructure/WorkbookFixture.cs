using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Xunit.Sdk;

namespace Pickory.IntegrationTests.Infrastructure;

// So file Excel và header của route xuất với ví dụ trong `spec/fixtures`. Helper so status, tên sheet, tiêu đề và kiểu ô dòng 2, không so giá trị ô.
public static class WorkbookFixture
{
    private const string ContentTypeHeader = "Content-Type";
    private const string ContentDispositionHeader = "Content-Disposition";

    // Phần ngày giờ `yyyyMMdd-HHmm` đổi theo từng lần xuất, nên helper chỉ so hình dạng của phần này.
    // Dùng `[0-9]` vì `\d` nhận cả chữ số của bảng chữ khác.
    private const string FileStampPattern = "[0-9]{8}-[0-9]{4}";

    private static readonly Regex FileStampRegex = new(FileStampPattern, RegexOptions.CultureInvariant);
    private static readonly Regex DateTimeSampleRegex = new("^[0-9]{2}/[0-9]{2}/[0-9]{4} [0-9]{2}:[0-9]{2}\\z", RegexOptions.CultureInvariant);
    private static readonly Regex QuotedFileNameRegex = new("filename=\"(?<name>[^\"]*)\"", RegexOptions.CultureInvariant);
    private static readonly Regex EncodedFileNameRegex = new("filename\\*=UTF-8''(?<name>[^;]*)", RegexOptions.CultureInvariant);

    private enum ExpectedCellKind
    {
        Any,
        Number,
        DateTime,
        Text,
    }

    // Một sheet mong đợi đã đọc và kiểm từ fixture. `SampleKinds` là `null` khi fixture không mô tả dòng dữ liệu.
    private sealed record ExpectedSheet(string Name, IReadOnlyList<string> Header, IReadOnlyList<ExpectedCellKind>? SampleKinds);

    /// <summary>
    /// Trả `void` vì file đã nằm trong bộ nhớ và header đọc ngay được.
    /// Không có bước nào cần `await`.
    /// </summary>
    /// <remarks>
    /// Gọi hàm này trước khi test tự đọc `response.Content.Headers.ContentDisposition`.
    /// Sau khi đọc thuộc tính đó, `ReadHeader` có thể nhận chuỗi đã chuẩn hóa.
    /// </remarks>
    public static void AssertMatches(string fixtureFile, string exampleName, byte[] file, HttpResponseMessage response)
    {
        AssertExampleMatches(fixtureFile, FixtureFiles.FindExample(fixtureFile, exampleName), file, response);
    }

    internal static void AssertExampleMatches(string fixtureFile, JsonObject example, byte[] file, HttpResponseMessage response)
    {
        var context = FixtureContext.For(fixtureFile, example);

        // Ví dụ lỗi không mô tả file. Báo rõ để người viết test đổi sang `ContractFixture`.
        var expectedWorkbook = example["workbook"];
        if (expectedWorkbook is null)
        {
            throw new XunitException(
                $"Ví dụ \"{context.ExampleName}\" trong `{fixtureFile}` không phải ví dụ file Excel vì không có khóa `workbook`. Dùng `ContractFixture` cho ví dụ này.");
        }

        // Đọc và kiểm toàn bộ fixture trước khi so. Fixture sai thì dừng với lỗi "Fixture sai", không lẫn với lỗi của route.
        var expectedStatus = context.RequireInt(example["status"], "khóa `status`");
        var expectedHeaders = context.RequireObject(example["headers"], "khóa `headers`");
        var expectedContentType = context.RequireString(expectedHeaders[ContentTypeHeader], $"khóa `headers.{ContentTypeHeader}`");
        var expectedDisposition = context.RequireString(expectedHeaders[ContentDispositionHeader], $"khóa `headers.{ContentDispositionHeader}`");
        var expectedSheets = ReadExpectedSheets(expectedWorkbook, context);

        var mismatches = new FixtureMismatches();

        // Response lỗi (ví dụ 500) có thể kèm đúng header và file. So status để không báo xanh giả.
        var actualStatus = (int)response.StatusCode;
        if (expectedStatus != actualStatus)
        {
            mismatches.Add("status", $"mong đợi {expectedStatus}, thực tế {actualStatus}");
        }

        CompareHeader(ContentTypeHeader, expectedContentType, response, mismatches);
        CompareContentDisposition(expectedDisposition, response, mismatches);
        CompareWorkbook(expectedSheets, file, mismatches);
        mismatches.ThrowIfAny($"File Excel không khớp ví dụ \"{context.ExampleName}\" trong `{fixtureFile}`:");
    }

    private static List<ExpectedSheet> ReadExpectedSheets(JsonNode expectedWorkbook, FixtureContext context)
    {
        var workbookObject = context.RequireObject(expectedWorkbook, "khóa `workbook`");

        // Đọc từng sheet một lần thành kiểu có kiểm tra. Các bước so sau đó không phải đọc JSON thô.
        var sheetNodes = context.RequireArray(workbookObject["sheets"], "khóa `workbook.sheets`");
        var sheets = new List<ExpectedSheet>();
        for (var sheetIndex = 0; sheetIndex < sheetNodes.Count; sheetIndex++)
        {
            var sheetPath = $"workbook.sheets[{sheetIndex}]";
            var sheetObject = context.RequireObject(sheetNodes[sheetIndex], $"khóa `{sheetPath}`");
            var name = context.RequireString(sheetObject["name"], $"khóa `{sheetPath}.name`");
            var header = context.RequireArray(sheetObject["header"], $"khóa `{sheetPath}.header`")
                .Select((title, index) => context.RequireString(title, $"khóa `{sheetPath}.header[{index}]`"))
                .ToList();
            sheets.Add(new ExpectedSheet(name, header, ReadSampleKinds(sheetObject["sampleRow"], header.Count, sheetPath, context)));
        }

        return sheets;
    }

    private static List<ExpectedCellKind>? ReadSampleKinds(JsonNode? sampleRow, int headerCount, string sheetPath, FixtureContext context)
    {
        // `sampleRow` là `null` hoặc không có thì fixture không mô tả dòng dữ liệu. Không so kiểu ô.
        if (sampleRow is null)
        {
            return null;
        }

        var sampleCells = context.RequireArray(sampleRow, $"khóa `{sheetPath}.sampleRow`");

        // `sampleRow` dài hơn `header` là fixture tự mâu thuẫn. Chặn ở đây để bước so không đọc ngoài mảng.
        if (sampleCells.Count > headerCount)
        {
            throw context.Fault($"khóa `{sheetPath}.sampleRow`", $"có {sampleCells.Count} ô, nhiều hơn {headerCount} cột của `header`");
        }

        var kinds = new List<ExpectedCellKind>();
        for (var index = 0; index < sampleCells.Count; index++)
        {
            kinds.Add(ReadSampleKind(sampleCells[index], $"khóa `{sheetPath}.sampleRow[{index}]`", context));
        }

        return kinds;
    }

    private static ExpectedCellKind ReadSampleKind(JsonNode? sample, string location, FixtureContext context)
    {
        // `null` trong `sampleRow` nghĩa là cột nhận mọi kiểu ô.
        if (sample is null)
        {
            return ExpectedCellKind.Any;
        }

        if (sample is JsonValue value)
        {
            // Chuỗi giống `dd/MM/yyyy HH:mm` là ngày giờ thật. Mọi chuỗi khác là chữ, kể cả mã số thuế toàn chữ số.
            if (value.TryGetValue<string>(out var text))
            {
                return DateTimeSampleRegex.IsMatch(text) ? ExpectedCellKind.DateTime : ExpectedCellKind.Text;
            }

            if (value.TryGetValue<double>(out _))
            {
                return ExpectedCellKind.Number;
            }
        }

        // Quy ước kiểu ô chỉ có số, chuỗi và `null`. Giá trị khác (ví dụ true/false) là lỗi của fixture.
        throw context.Fault(location, "`sampleRow` chỉ hỗ trợ số, chuỗi và null");
    }

    private static void CompareHeader(string name, string expected, HttpResponseMessage response, FixtureMismatches mismatches)
    {
        // So đúng chuỗi, kể cả phần `; charset=...`. Loại file sai làm trình duyệt không mở được bằng Excel.
        var actual = ReadHeader(response, name);
        if (actual is null)
        {
            mismatches.Add(name, "thiếu header");
        }
        else if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            mismatches.Add(name, $"mong đợi \"{expected}\", thực tế \"{actual}\"");
        }
    }

    private static void CompareContentDisposition(string expected, HttpResponseMessage response, FixtureMismatches mismatches)
    {
        var actual = ReadHeader(response, ContentDispositionHeader);
        if (actual is null)
        {
            mismatches.Add(ContentDispositionHeader, "thiếu header");
            return;
        }

        // Đổi phần ngày giờ thành ký tự đại diện, giữ nguyên phần còn lại để dấu ngoặc kép và `filename*=UTF-8''` đúng từng ký tự.
        // Dùng `\z` thay cho `$` vì `$` còn khớp trước ký tự xuống dòng cuối chuỗi.
        var literalParts = FileStampRegex.Split(expected).Select(Regex.Escape);
        var pattern = $"^{string.Join(FileStampPattern, literalParts)}\\z";
        if (!Regex.IsMatch(actual, pattern, RegexOptions.CultureInvariant))
        {
            mismatches.Add(
                ContentDispositionHeader,
                $"không khớp mẫu \"{expected}\" (phần yyyyMMdd-HHmm là ký tự đại diện), thực tế \"{actual}\"");
            return;
        }

        // `filename` và `filename*` là hai cách ghi cùng một tên. Mẫu có ký tự đại diện cho phép hai tên lệch ngày giờ, nên so riêng.
        var quotedName = QuotedFileNameRegex.Match(actual);
        var encodedName = EncodedFileNameRegex.Match(actual);
        if (quotedName.Success && encodedName.Success
            && !string.Equals(quotedName.Groups["name"].Value, encodedName.Groups["name"].Value, StringComparison.Ordinal))
        {
            mismatches.Add(
                ContentDispositionHeader,
                $"hai tên file khác nhau: filename=\"{quotedName.Groups["name"].Value}\", filename*={encodedName.Groups["name"].Value}");
        }
    }

    // Đọc header thô bằng `TryGetValues`, không qua thuộc tính đã phân tích như `ContentDisposition`.
    // Sau khi test đọc thuộc tính đó, .NET có thể chuẩn hóa chuỗi, và hành vi này chưa được kiểm.
    private static string? ReadHeader(HttpResponseMessage response, string name)
    {
        if (response.Content.Headers.TryGetValues(name, out var contentValues))
        {
            return string.Join(", ", contentValues);
        }

        return response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : null;
    }

    private static void CompareWorkbook(IReadOnlyList<ExpectedSheet> expectedSheets, byte[] file, FixtureMismatches mismatches)
    {
        // Mọi lỗi mở file đều có nghĩa là file hỏng. Báo một thông báo rõ, không để lỗi của thư viện lọt ra.
        using var stream = new MemoryStream(file);
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception exception)
        {
            mismatches.Add("file", $"không mở được bằng ClosedXML ({exception.GetType().Name}: {exception.Message})");
            return;
        }

        using (workbook)
        {
            CompareSheetNames(expectedSheets, workbook, mismatches);

            // Chỉ so nội dung của sheet có mặt trong file. Sheet thiếu đã được báo ở bước so tên.
            var actualSheets = workbook.Worksheets.ToDictionary(sheet => sheet.Name, StringComparer.Ordinal);
            foreach (var expectedSheet in expectedSheets)
            {
                if (actualSheets.TryGetValue(expectedSheet.Name, out var actualSheet))
                {
                    CompareHeaderRow(expectedSheet, actualSheet, mismatches);
                    CompareDataRowTypes(expectedSheet, actualSheet, mismatches);
                }
            }
        }
    }

    private static void CompareSheetNames(IReadOnlyList<ExpectedSheet> expectedSheets, XLWorkbook workbook, FixtureMismatches mismatches)
    {
        var expectedNames = expectedSheets.Select(sheet => sheet.Name).ToList();
        var actualNames = workbook.Worksheets.Select(sheet => sheet.Name).ToList();

        // Báo từng sheet thiếu và từng sheet thừa. Người sửa biết chính xác sheet nào sai.
        var hasMissingOrExtra = false;
        foreach (var missingName in expectedNames.Where(name => !actualNames.Contains(name, StringComparer.Ordinal)))
        {
            mismatches.Add("sheets", $"thiếu sheet \"{missingName}\"");
            hasMissingOrExtra = true;
        }

        foreach (var extraName in actualNames.Where(name => !expectedNames.Contains(name, StringComparer.Ordinal)))
        {
            mismatches.Add("sheets", $"thừa sheet \"{extraName}\"");
            hasMissingOrExtra = true;
        }

        // Cùng tập tên mà khác thứ tự là lỗi riêng. Người dùng thấy các tab sai chỗ.
        if (!hasMissingOrExtra && !expectedNames.SequenceEqual(actualNames, StringComparer.Ordinal))
        {
            mismatches.Add("sheets", $"sai thứ tự, mong đợi [{string.Join(", ", expectedNames)}], thực tế [{string.Join(", ", actualNames)}]");
        }
    }

    private static void CompareHeaderRow(ExpectedSheet expectedSheet, IXLWorksheet worksheet, FixtureMismatches mismatches)
    {
        // So từng ô tiêu đề theo vị trí. Cột đổi chỗ làm lệch dữ liệu bên dưới nên cũng là lỗi.
        for (var index = 0; index < expectedSheet.Header.Count; index++)
        {
            var cell = worksheet.Cell(1, index + 1);
            var expectedTitle = expectedSheet.Header[index];
            var actualTitle = ReadCellText(cell);
            if (!string.Equals(expectedTitle, actualTitle, StringComparison.Ordinal))
            {
                mismatches.Add(
                    $"sheet \"{expectedSheet.Name}\", dòng 1, {DescribeColumn(cell)}",
                    $"tiêu đề mong đợi \"{expectedTitle}\", thực tế \"{actualTitle}\"");
            }
        }

        // Cột tiêu đề nằm sau cột cuối của fixture là cột thừa ngoài contract.
        var lastUsedColumn = worksheet.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0;
        for (var column = expectedSheet.Header.Count + 1; column <= lastUsedColumn; column++)
        {
            var cell = worksheet.Cell(1, column);
            mismatches.Add($"sheet \"{expectedSheet.Name}\", dòng 1, {DescribeColumn(cell)}", $"thừa cột, tiêu đề \"{ReadCellText(cell)}\"");
        }
    }

    private static void CompareDataRowTypes(ExpectedSheet expectedSheet, IXLWorksheet worksheet, FixtureMismatches mismatches)
    {
        // Sheet chỉ có tiêu đề là kết quả hợp lệ của bộ lọc không khớp dòng nào. Không có dòng 2 để so.
        var lastUsedRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        if (lastUsedRow < 2)
        {
            return;
        }

        // Fixture không mô tả dòng dữ liệu thì không so kiểu ô.
        if (expectedSheet.SampleKinds is null)
        {
            return;
        }

        // Dòng 2 có nhiều ô hơn số cột tiêu đề là dữ liệu tràn ra ngoài bảng.
        var lastUsedColumn = worksheet.Row(2).LastCellUsed()?.Address.ColumnNumber ?? 0;
        if (lastUsedColumn > expectedSheet.Header.Count)
        {
            mismatches.Add(
                $"sheet \"{expectedSheet.Name}\", dòng 2",
                $"có {lastUsedColumn} cột, nhiều hơn {expectedSheet.Header.Count} cột tiêu đề");
        }

        for (var index = 0; index < expectedSheet.SampleKinds.Count; index++)
        {
            var cell = worksheet.Cell(2, index + 1);
            var location = $"sheet \"{expectedSheet.Name}\", dòng 2, {DescribeColumn(cell)} \"{expectedSheet.Header[index]}\"";
            CompareCellType(location, expectedSheet.SampleKinds[index], cell, mismatches);
        }
    }

    private static void CompareCellType(string location, ExpectedCellKind expectedKind, IXLCell cell, FixtureMismatches mismatches)
    {
        switch (expectedKind)
        {
            case ExpectedCellKind.Number:
                RequireCellType(location, "kiểu số", cell, [XLDataType.Number], mismatches);
                break;
            case ExpectedCellKind.DateTime:
                RequireCellType(location, "kiểu ngày giờ", cell, [XLDataType.DateTime], mismatches);
                break;
            case ExpectedCellKind.Text:
                RequireCellType(location, "kiểu chữ hoặc trống", cell, [XLDataType.Text, XLDataType.Blank], mismatches);
                break;
        }
    }

    private static void RequireCellType(string location, string expectedDescription, IXLCell cell, XLDataType[] acceptedTypes, FixtureMismatches mismatches)
    {
        if (!acceptedTypes.Contains(cell.DataType))
        {
            mismatches.Add(location, $"mong đợi {expectedDescription}, thực tế {DescribeCellType(cell.DataType)}");
        }
    }

    private static string DescribeCellType(XLDataType type)
    {
        return type switch
        {
            XLDataType.Number => "kiểu số",
            XLDataType.Text => "kiểu chữ",
            XLDataType.DateTime => "kiểu ngày giờ",
            XLDataType.Boolean => "kiểu true/false",
            XLDataType.Blank => "ô trống",
            _ => $"kiểu {type}",
        };
    }

    private static string DescribeColumn(IXLCell cell)
    {
        return $"cột {cell.Address.ColumnNumber} ({cell.Address.ColumnLetter})";
    }

    // Đọc ô bằng `InvariantCulture`. Kết quả không phụ thuộc culture của máy chạy test.
    private static string ReadCellText(IXLCell cell)
    {
        return cell.Value.IsBlank ? string.Empty : cell.Value.ToString(CultureInfo.InvariantCulture);
    }
}
