using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Pickory.Shared.Exports;

// Bỏ tham số phân trang khỏi query string chung của mọi route xuất file.
public static class ExportQueryString
{
    // Hai tham số phân trang của route danh sách. Route xuất bỏ qua chúng và luôn trả mọi dòng khớp bộ lọc.
    private static readonly string[] PagingParameters = ["page", "limit"];

    // Bỏ `page` và `limit` trước khi đọc tham số. Giá trị sai của hai tham số này không gây lỗi 400, theo contract.
    public static QueryCollection WithoutPaging(IQueryCollection query)
    {
        var remaining = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in query)
        {
            if (!PagingParameters.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                remaining[name] = values;
            }
        }

        return new QueryCollection(remaining);
    }
}
