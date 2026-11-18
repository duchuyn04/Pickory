using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Pickory.Modules.Commerce.Exports;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Exports;

// Bỏ `page` và `limit` khỏi query string của route xuất. Mọi route xuất trong `commerce` dùng chung hàm này.
public sealed class ExportQueryStringTests
{
    private static QueryCollection Query(params (string Name, string[] Values)[] parameters) =>
        new(parameters.ToDictionary(parameter => parameter.Name, parameter => new StringValues(parameter.Values)));

    [Fact]
    // Query rỗng cho kết quả rỗng và không ném lỗi.
    public void WithoutPaging_EmptyQuery_ReturnsEmpty()
    {
        Assert.Empty(ExportQueryString.WithoutPaging(Query()));
    }

    [Fact]
    // Chỉ `page` và `limit` bị bỏ. Các tham số lọc giữ nguyên tên và giá trị.
    public void WithoutPaging_MixedQuery_KeepsOnlyNonPagingParameters()
    {
        var result = ExportQueryString.WithoutPaging(Query(
            ("page", ["2"]), ("matchStatus", ["matched"]), ("limit", ["abc"]), ("orderFlag", ["underpaid"])));

        Assert.Equal(["matchStatus", "orderFlag"], result.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal("matched", result["matchStatus"]);
        Assert.Equal("underpaid", result["orderFlag"]);
    }

    [Theory]
    [InlineData("PAGE")]
    [InlineData("Limit")]
    [InlineData("pAgE")]
    // Tên tham số phân trang không phân biệt hoa thường, vì phía đọc query cũng không phân biệt.
    public void WithoutPaging_DifferentCase_IsRemoved(string name)
    {
        var result = ExportQueryString.WithoutPaging(Query((name, ["1"]), ("matchStatus", ["matched"])));

        Assert.Equal(["matchStatus"], result.Keys);
    }

    [Theory]
    [InlineData("pageSize")]
    [InlineData("limitX")]
    [InlineData("xpage")]
    // Chỉ tên đúng `page` và `limit` bị bỏ. Tên chỉ có chung tiền tố hay hậu tố vẫn được giữ.
    public void WithoutPaging_NameSharingPrefixWithPaging_IsKept(string name)
    {
        var result = ExportQueryString.WithoutPaging(Query((name, ["1"])));

        Assert.Equal([name], result.Keys);
    }

    [Fact]
    // Tham số lặp giữ đủ mọi giá trị, để bước kiểm tham số sau đó vẫn báo lỗi lặp.
    public void WithoutPaging_RepeatedFilterParameter_KeepsAllValues()
    {
        var result = ExportQueryString.WithoutPaging(Query(("matchStatus", ["matched", "ignored"])));

        Assert.Equal(new StringValues(["matched", "ignored"]), result["matchStatus"]);
    }

    [Fact]
    // Tham số phân trang lặp cũng bị bỏ hết, nên giá trị lặp của chúng không gây lỗi 400.
    public void WithoutPaging_RepeatedPagingParameter_IsRemoved()
    {
        var result = ExportQueryString.WithoutPaging(Query(("page", ["1", "2"])));

        Assert.Empty(result);
    }

    [Fact]
    // Kết quả tra tên không phân biệt hoa thường, giống query của máy chủ. Tên lọc viết khác hoa thường vẫn tìm thấy.
    public void WithoutPaging_ResultLookup_IgnoresCase()
    {
        var result = ExportQueryString.WithoutPaging(Query(("matchStatus", ["matched"])));

        Assert.Equal("matched", result["MATCHSTATUS"]);
    }
}
