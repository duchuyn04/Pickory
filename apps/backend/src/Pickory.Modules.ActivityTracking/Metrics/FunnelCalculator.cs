using Pickory.Modules.ActivityTracking.Contracts.Metrics;

namespace Pickory.Modules.ActivityTracking.Metrics;

// Hàm thuần dựng phễu từ tổng ba chỉ số. Hàm không gọi DB, nên unit test kiểm được từng giá trị biên.
internal static class FunnelCalculator
{
    public static Funnel Calculate(long productViews, long addToCarts, long ordersPlaced)
    {
        // Bộ đếm chỉ tăng, nên số âm là dữ liệu hỏng. Báo lỗi ngay để phễu không hiện tỉ lệ âm.
        RequireNonNegative(productViews, nameof(productViews));
        RequireNonNegative(addToCarts, nameof(addToCarts));
        RequireNonNegative(ordersPlaced, nameof(ordersPlaced));

        // Thứ tự bước cố định theo đường mua hàng. Bước đầu không có bước trước nên tỉ lệ là `null`.
        var steps = new[]
        {
            new FunnelStep(ActivityMetricNames.ProductView, productViews, ConversionRate: null),
            new FunnelStep(
                ActivityMetricNames.AddToCart, addToCarts, CalculateConversionRate(addToCarts, productViews)),
            new FunnelStep(
                ActivityMetricNames.OrderPlaced, ordersPlaced, CalculateConversionRate(ordersPlaced, addToCarts)),
        };

        return new Funnel(steps);
    }

    private static void RequireNonNegative(long count, string parameterName)
    {
        if (count >= 0)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            parameterName, count, $"Số lượt `{parameterName}` không được âm, nhưng nhận được {count}.");
    }

    private static decimal? CalculateConversionRate(long count, long previousStepCount)
    {
        // Bước trước có 0 lượt thì không có gì để so. Trả `null` thay vì chia cho 0.
        if (previousStepCount == 0)
        {
            return null;
        }

        // Ép sang `decimal` trước khi chia, vì phép chia `long` bỏ phần thập phân.
        // Không cắt về 1, vì số đếm của hai bước độc lập nhau nên bước sau có thể lớn hơn bước trước.
        return (decimal)count / previousStepCount;
    }
}
