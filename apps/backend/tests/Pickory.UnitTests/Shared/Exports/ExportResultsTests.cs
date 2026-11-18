using Microsoft.AspNetCore.Http;
using Pickory.Shared.Exports;
using Xunit;

namespace Pickory.UnitTests.Shared.Exports;

public sealed class ExportResultsTests
{
    private static async Task<(DefaultHttpContext Context, MemoryStream Body)> ExecuteAsync(byte[] content, string fileName)
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        await ExportResults.Xlsx(content, fileName).ExecuteAsync(context);

        return (context, body);
    }

    [Fact]
    // Header phải đúng từng ký tự như contract, gồm dấu ngoặc kép quanh `filename`.
    public async Task Xlsx_WritesContractHeaders_AndBody()
    {
        byte[] content = [0x50, 0x4B, 0x03, 0x04, 0x01];

        var (context, body) = await ExecuteAsync(content, "don-hang-20261110-1530.xlsx");

        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            context.Response.ContentType);
        Assert.Equal(
            "attachment; filename=\"don-hang-20261110-1530.xlsx\"; filename*=UTF-8''don-hang-20261110-1530.xlsx",
            context.Response.Headers.ContentDisposition.ToString());
        Assert.Equal(content.Length, context.Response.ContentLength);
        Assert.Equal(content, body.ToArray());
    }

    [Theory]
    // Ký tự ngoặc kép, xuống dòng, dấu cách hoặc chữ có dấu có thể chèn header hoặc làm hai tên file lệch nhau.
    [InlineData("a\".xlsx")]
    [InlineData("a\r\nSet-Cookie: x=1.xlsx")]
    [InlineData("đơn hàng.xlsx")]
    [InlineData("a b.xlsx")]
    [InlineData("")]
    public void Xlsx_UnsafeFileName_Throws(string fileName)
    {
        Assert.ThrowsAny<ArgumentException>(() => ExportResults.Xlsx([1], fileName));
    }
}
