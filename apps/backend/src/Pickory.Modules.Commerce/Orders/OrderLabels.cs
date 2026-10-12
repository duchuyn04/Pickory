using System.Text.Json;
using Pickory.Modules.Commerce.Contracts.Orders;

namespace Pickory.Modules.Commerce.Orders;

// Nhãn snake_case của enum trong response. Nhãn khớp nhãn enum trong DB, nên client dùng một bộ giá trị cho cả hai.
public static class OrderLabels
{
    public static string ToLabel(OrderStatus status) => Convert(status);

    public static string ToLabel(PaymentStatus status) => Convert(status);

    public static string ToLabel(PaymentMethod method) => Convert(method);

    private static string Convert<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
