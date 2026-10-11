using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Shipping.Persistence;

namespace Pickory.Modules.Shipping.Shipments;

// Quy tắc đổi `ref` của vận đơn đã hủy. Hủy bằng tay, webhook và job đồng bộ dùng chung để mã gốc rảnh theo cùng một cách.
internal static class CancelledShipmentRef
{
    // Vận đơn đã hủy có `ref` dạng `{ref}#cancelled#{n}`, theo quyết định 6 của plan.
    private const string Marker = "#cancelled#";

    // Số thứ tự là số vận đơn đã hủy của `ref` gốc cộng 1. Nhờ vậy hai vận đơn đã hủy của cùng `ref` không trùng nhau.
    public static string Build(string originalRef, int cancelledCount) => $"{originalRef}{Marker}{cancelledCount + 1}";

    public static async Task<int> CountAsync(
        ShippingDbContext database, string originalRef, CancellationToken cancellationToken)
    {
        var prefix = $"{originalRef}{Marker}";
        return await database.Shipments
            .AsNoTracking()
            .CountAsync(shipment => shipment.Ref.StartsWith(prefix), cancellationToken);
    }
}
