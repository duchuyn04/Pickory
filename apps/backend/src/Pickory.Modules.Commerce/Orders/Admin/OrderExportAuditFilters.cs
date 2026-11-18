using System.Globalization;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Bộ lọc của một lần xuất, ghi vào nhật ký thao tác. Khóa là tên tham số của query, giá trị đúng như người dùng có thể gửi lại.
internal static class OrderExportAuditFilters
{
    // Chỉ giữ tham số có giá trị. `page` và `limit` không có mặt, vì route xuất bỏ qua hai tham số này.
    public static IReadOnlyDictionary<string, string> From(AdminOrderListQuery filter)
    {
        var filters = new Dictionary<string, string>(StringComparer.Ordinal);

        // Trạng thái dùng nhãn snake_case, cùng chữ với tham số của query.
        if (filter.Status is { } status)
        {
            filters[AdminOrderListQuery.StatusParameter] = OrderLabels.ToLabel(status);
        }

        if (filter.PaymentStatus is { } paymentStatus)
        {
            filters[AdminOrderListQuery.PaymentStatusParameter] = OrderLabels.ToLabel(paymentStatus);
        }

        if (filter.PaymentMethod is { } paymentMethod)
        {
            filters[AdminOrderListQuery.PaymentMethodParameter] = OrderLabels.ToLabel(paymentMethod);
        }

        if (filter.Flag is { } flag)
        {
            filters[AdminOrderListQuery.FlagParameter] = flag;
        }

        // Ngày ghi theo định dạng của query bằng `InvariantCulture`. Nhật ký không đổi theo ngôn ngữ của máy chủ.
        if (filter.From is { } from)
        {
            filters[AdminOrderListQuery.FromParameter] = from.ToString(AdminOrderListQuery.DateFormat, CultureInfo.InvariantCulture);
        }

        if (filter.To is { } to)
        {
            filters[AdminOrderListQuery.ToParameter] = to.ToString(AdminOrderListQuery.DateFormat, CultureInfo.InvariantCulture);
        }

        // Từ khóa là chữ đã bỏ khoảng trắng thừa, không phải mẫu `ILIKE` đã escape.
        if (filter.Search is { } search)
        {
            filters[SearchTerm.ParameterName] = search.Text;
        }

        return filters;
    }
}
