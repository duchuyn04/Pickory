namespace Pickory.Modules.SupportDesk.Metrics;

// Hàm thuần tính phân vị theo nearest-rank. Kết quả luôn là một giá trị có trong danh sách, không nội suy.
internal static class Percentile
{
    public static double? Of(IReadOnlyList<double> values, double percentile)
    {
        ArgumentNullException.ThrowIfNull(values);

        // Viết dạng phủ định để `NaN` cũng bị chặn. Phân vị 0 không có hạng nào, nên khoảng hợp lệ là (0, 100].
        if (!(percentile > 0 && percentile <= 100))
        {
            throw new ArgumentOutOfRangeException(
                nameof(percentile), percentile, $"Phân vị phải nằm trong khoảng (0, 100], nhưng nhận được {percentile}.");
        }

        // Chưa có số liệu thì không có phân vị. Trả `null` để màn quản trị hiện "chưa có dữ liệu" thay vì số 0 sai.
        if (values.Count == 0)
        {
            return null;
        }

        // Sắp xếp trên bản sao. Bên gọi có thể dùng lại danh sách gốc và mong thứ tự không đổi.
        var sortedValues = values.Order().ToArray();

        // Nhân trước, chia sau. Với phân vị nguyên, tích là số nguyên chính xác nên `ceil` không lệch hạng vì sai số dấu phẩy động.
        var computedRank = (int)Math.Ceiling(percentile * sortedValues.Length / 100.0);

        // Phân vị rất nhỏ (ví dụ `double.Epsilon`) làm kết quả phép chia tràn xuống 0, nên hạng thành 0. Hạng nhỏ nhất hợp lệ là 1, nếu không chỉ số mảng thành -1.
        var rank = Math.Max(computedRank, 1);

        // Hạng đếm từ 1, còn chỉ số mảng đếm từ 0.
        return sortedValues[rank - 1];
    }
}
