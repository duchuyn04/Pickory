using System.Text.Json;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Shipping.Contracts.Shipments;

namespace Pickory.Modules.Commerce.Orders;

// Nhãn snake_case của enum trong response. Nhãn khớp nhãn enum trong DB, nên client dùng một bộ giá trị cho cả hai.
public static class OrderLabels
{
    public static string ToLabel(OrderStatus status) => Convert(status);

    public static string ToLabel(PaymentStatus status) => Convert(status);

    public static string ToLabel(PaymentMethod method) => Convert(method);

    public static string ToLabel(ShippingFeeSource source) => Convert(source);

    // Trạng thái vận đơn do module Shipping sở hữu. Nhãn dùng cùng quy tắc để client không có hai kiểu viết.
    public static string ToLabel(ShipmentStatus status) => Convert(status);

    // Đổi ngược nhãn của client về enum. So khớp chính xác với nhãn, nên giá trị lạ hoặc sai chữ hoa chữ thường trả `false`.
    public static bool TryParse<TEnum>(string? label, out TEnum value)
        where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(Convert(candidate), label, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string Convert<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
