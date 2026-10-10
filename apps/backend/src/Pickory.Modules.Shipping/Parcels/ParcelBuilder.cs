using Pickory.Modules.Shipping.Contracts.Parcels;

namespace Pickory.Modules.Shipping.Parcels;

// Gộp nhiều dòng hàng thành một kiện để báo phí và tạo vận đơn.
public static class ParcelBuilder
{
    // Cân nặng và chiều cao cộng dồn, dài và rộng lấy giá trị lớn nhất. Cách này ước lượng dư nên phí báo không thấp hơn phí thật.
    public static Parcel Build(IReadOnlyList<ParcelItem> items)
    {
        // Danh sách null là lỗi lập trình của bên gọi. Báo đúng tên tham số để dễ tìm.
        ArgumentNullException.ThrowIfNull(items);

        // Kiện không có hàng không có cân nặng thật. Từ chối thay vì trả kiện toàn số 0.
        if (items.Count == 0)
        {
            throw new ArgumentException("Kiện hàng phải có ít nhất một dòng hàng.", nameof(items));
        }

        // Cộng dồn bằng `long` để phát hiện tổng vượt `int`. Số quay vòng thành số âm sẽ làm sai phí.
        long totalWeightGrams = 0;
        long totalHeightCm = 0;
        var maxLengthCm = 0;
        var maxWidthCm = 0;

        foreach (var item in items)
        {
            // Từng dòng được kiểm riêng. Dòng hợp lệ đứng cạnh không che được dòng sai.
            ValidateItem(item, nameof(items));

            // Cân nặng và chiều cao nhân số lượng vì mỗi đơn vị hàng chiếm thêm chỗ trong kiện.
            totalWeightGrams += (long)item.Parcel.WeightGrams * item.Quantity;
            totalHeightCm += (long)item.Parcel.HeightCm * item.Quantity;

            // Kiểm tổng sau từng dòng, vì tổng chưa vượt `int` thì thêm một tích `int x int` không làm `long` tràn.
            // Kiểm một lần ở cuối thì nhiều dòng lớn làm `long` quay vòng thành số âm mà không bị bắt.
            EnsureWithinIntRange(totalWeightGrams, "cân nặng", nameof(items));
            EnsureWithinIntRange(totalHeightCm, "chiều cao", nameof(items));

            // Dài và rộng không nhân số lượng vì hàng xếp chồng theo chiều cao.
            maxLengthCm = Math.Max(maxLengthCm, item.Parcel.LengthCm);
            maxWidthCm = Math.Max(maxWidthCm, item.Parcel.WidthCm);
        }

        return new Parcel((int)totalWeightGrams, maxLengthCm, maxWidthCm, (int)totalHeightCm);
    }

    private static void ValidateItem(ParcelItem? item, string parameterName)
    {
        // Dòng hoặc kích thước null sẽ gây lỗi tham chiếu null khó hiểu ở bước tính. Báo lỗi rõ ngay tại đây.
        if (item?.Parcel is null)
        {
            throw new ArgumentException("Dòng hàng phải có kích thước.", parameterName);
        }

        // Số lượng không dương làm tổng sai theo hướng thấp hơn. Phí báo cho khách sẽ thấp hơn phí thật.
        if (item.Quantity < 1)
        {
            throw new ArgumentException("Số lượng của dòng hàng phải lớn hơn 0.", parameterName);
        }

        // Giá trị 0 được chấp nhận vì tiêu chí chỉ cấm số âm. Số âm làm giảm tổng một cách vô lý.
        var parcel = item.Parcel;
        if (parcel.WeightGrams < 0 || parcel.LengthCm < 0 || parcel.WidthCm < 0 || parcel.HeightCm < 0)
        {
            throw new ArgumentException("Cân nặng và kích thước của dòng hàng không được âm.", parameterName);
        }
    }

    private static void EnsureWithinIntRange(long total, string measureName, string parameterName)
    {
        // Tổng vượt `int` là dữ liệu không thể giao. Từ chối thay vì cắt bớt hoặc trả số âm.
        if (total > int.MaxValue)
        {
            throw new ArgumentException($"Tổng {measureName} của kiện vượt giới hạn cho phép.", parameterName);
        }
    }
}
