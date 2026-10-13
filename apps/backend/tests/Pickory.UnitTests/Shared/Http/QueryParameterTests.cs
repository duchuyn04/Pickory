using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Pickory.Shared.Http;
using Xunit;

namespace Pickory.UnitTests.Shared.Http;

// Đọc một tham số query chỉ có một giá trị. Danh sách back-office dùng cho `status`, `role` và `type`.
public class QueryParameterTests
{
    private static QueryCollection QueryOf(string name, params string[] values)
    {
        return new QueryCollection(new Dictionary<string, StringValues> { [name] = new StringValues(values) });
    }

    [Fact]
    // Không gửi tham số thì không lọc và không phải lỗi.
    public void ReadSingle_MissingParameter_ReturnsNullWithoutFailure()
    {
        var failures = new List<ValidationFailure>();

        var value = QueryParameter.ReadSingle(new QueryCollection(), "status", failures);

        Assert.Null(value);
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    // Ô lọc để trống được coi như không gửi, để form lọc không gây lỗi.
    public void ReadSingle_BlankValue_ReturnsNullWithoutFailure(string raw)
    {
        var failures = new List<ValidationFailure>();

        var value = QueryParameter.ReadSingle(QueryOf("status", raw), "status", failures);

        Assert.Null(value);
        Assert.Empty(failures);
    }

    [Fact]
    // Giá trị hợp lệ được trả nguyên văn, không đổi chữ hoa thường. Bên gọi tự quyết định cách so sánh.
    public void ReadSingle_SingleValue_ReturnsValueUnchanged()
    {
        var failures = new List<ValidationFailure>();

        var value = QueryParameter.ReadSingle(QueryOf("status", "Locked"), "status", failures);

        Assert.Equal("Locked", value);
        Assert.Empty(failures);
    }

    [Fact]
    // Tham số lặp lại ghi lỗi đúng tên trường, vì không rõ giá trị nào là ý của người dùng.
    public void ReadSingle_RepeatedParameter_AddsFailureForThatField()
    {
        var failures = new List<ValidationFailure>();

        var value = QueryParameter.ReadSingle(QueryOf("role", "sales", "support"), "role", failures);

        Assert.Null(value);
        var failure = Assert.Single(failures);
        Assert.Equal("role", failure.PropertyName);
    }

    [Fact]
    // Tham số lặp lại có một giá trị trống vẫn là lỗi. Giá trị trống không được che việc lặp.
    public void ReadSingle_RepeatedParameterWithBlankValue_AddsFailure()
    {
        var failures = new List<ValidationFailure>();

        var value = QueryParameter.ReadSingle(QueryOf("status", "", "active"), "status", failures);

        Assert.Null(value);
        Assert.Single(failures);
    }
}
