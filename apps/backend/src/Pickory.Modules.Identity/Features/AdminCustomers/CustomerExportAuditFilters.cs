using Pickory.Shared.Http;

namespace Pickory.Modules.Identity.Features.AdminCustomers;

// Bộ lọc của một lần xuất, ghi vào nhật ký thao tác. Khóa là tên tham số của query, giá trị đúng như người dùng có thể gửi lại.
internal static class CustomerExportAuditFilters
{
    // Chỉ giữ tham số có giá trị. `page` và `limit` không có mặt, vì route xuất bỏ qua hai tham số này.
    public static IReadOnlyDictionary<string, string> From(CustomerListQuery filter)
    {
        var filters = new Dictionary<string, string>(StringComparer.Ordinal);

        // Ghi đúng chữ người dùng gõ, sau khi bỏ khoảng trắng thừa. Không ghi mẫu `LIKE` đã escape.
        if (filter.Search is { } search)
        {
            filters[SearchTerm.ParameterName] = search.Text;
        }

        if (filter.Status is { } status)
        {
            filters[CustomerListQuery.StatusParameter] = CustomerListQuery.StatusValue(status);
        }

        if (filter.EmailVerified is { } emailVerified)
        {
            filters[CustomerListQuery.EmailVerifiedParameter] = emailVerified ? "true" : "false";
        }

        return filters;
    }
}
