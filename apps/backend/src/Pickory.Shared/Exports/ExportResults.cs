using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Pickory.Shared.Exports;

public static partial class ExportResults
{
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // Tên file chỉ gồm chữ không dấu, số, `.`, `_` và `-`. Nhờ vậy `filename` và `filename*` giống nhau và header không bị chèn ký tự lạ.
    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex SafeFileNameRegex();

    public static IResult Xlsx(byte[] content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        // Từ chối tên file có dấu ngoặc kép, xuống dòng hoặc ký tự ngoài ASCII. Các ký tự này làm hỏng hoặc chèn thêm header.
        if (!SafeFileNameRegex().IsMatch(fileName))
        {
            throw new ArgumentException("Tên file chứa ký tự không an toàn cho header.", nameof(fileName));
        }

        return new XlsxFileResult(content, fileName);
    }

    private sealed class XlsxFileResult(byte[] content, string fileName) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var response = httpContext.Response;
            response.ContentType = XlsxContentType;
            // Tự ghi header vì `Results.File` không đặt dấu ngoặc kép quanh `filename`, trong khi contract yêu cầu có.
            response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"; filename*=UTF-8''{fileName}";
            response.ContentLength = content.Length;
            await response.Body.WriteAsync(content, httpContext.RequestAborted);
        }
    }
}
