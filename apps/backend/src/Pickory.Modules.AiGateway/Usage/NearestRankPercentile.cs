namespace Pickory.Modules.AiGateway.Usage;

// Phân vị theo phương pháp nearest-rank: thứ hạng `ceil(P / 100 * N)` trên dãy đã sắp xếp.
// Phương pháp này không nội suy, nên kết quả luôn là một giá trị đo thật.
public static class NearestRankPercentile
{
    public static int Calculate(IReadOnlyList<int> values, double percentile)
    {
        // Chưa có giá trị thì trả 0. Báo cáo không hiển thị độ trễ giả.
        if (values.Count == 0)
        {
            return 0;
        }

        // Sắp xếp bản sao. Mảng của người gọi không bị đổi thứ tự.
        var sorted = values.Order().ToArray();
        var rank = (int)Math.Ceiling(percentile / 100 * sorted.Length);
        var index = Math.Clamp(rank - 1, 0, sorted.Length - 1);
        return sorted[index];
    }
}
