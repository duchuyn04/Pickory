using Pickory.Modules.Shipping.Contracts.Parcels;

namespace Pickory.Modules.Commerce.Checkout;

// Một dòng giỏ đã ghép với dữ liệu catalog. Cân nặng và kích thước là của một đơn vị hàng, có thể `null` khi catalog chưa nhập.
public readonly record struct CheckoutLine(
    string SkuId,
    int Quantity,
    int UnitPrice,
    int? WeightGrams,
    int? LengthCm,
    int? WidthCm,
    int? HeightCm);

// Luật thuần dựng kiện hàng cho checkout, tách khỏi DB và HTTP để unit test kiểm từng trường hợp thiếu dữ liệu.
public static class CheckoutParcelRules
{
    // Trả `SkuId` theo thứ tự dòng giỏ. Thiếu cân nặng hoặc một kích thước thì không báo phí được, và hệ thống không đoán giá trị.
    public static IReadOnlyList<string> FindSkuIdsMissingDimensions(IReadOnlyList<CheckoutLine> lines)
    {
        return lines
            .Where(line => !HasCompleteDimensions(line))
            .Select(line => line.SkuId)
            .ToList();
    }

    // Mỗi dòng giỏ thành một `ParcelItem` mang kích thước của một đơn vị hàng và số lượng của dòng.
    // Bên gọi phải kiểm `FindSkuIdsMissingDimensions` trước, vì dòng thiếu dữ liệu là lỗi lập trình ở bước này.
    public static IReadOnlyList<ParcelItem> BuildParcelItems(IReadOnlyList<CheckoutLine> lines)
    {
        return lines.Select(ToParcelItem).ToList();
    }

    private static ParcelItem ToParcelItem(CheckoutLine line)
    {
        // Dòng thiếu dữ liệu mà vẫn vào kiện sẽ cho cân nặng 0 và phí thấp hơn phí thật. Dừng ngay thay vì đoán.
        if (!HasCompleteDimensions(line))
        {
            throw new InvalidOperationException($"SKU {line.SkuId} thiếu cân nặng hoặc kích thước, không dựng được kiện hàng.");
        }

        var unitParcel = new Parcel(line.WeightGrams!.Value, line.LengthCm!.Value, line.WidthCm!.Value, line.HeightCm!.Value);
        return new ParcelItem(unitParcel, line.Quantity);
    }

    // Giá trị không dương coi như chưa nhập. Cân nặng 0 làm phí báo thấp hơn phí GHN thu thật.
    private static bool HasCompleteDimensions(CheckoutLine line) =>
        line.WeightGrams is > 0 && line.LengthCm is > 0 && line.WidthCm is > 0 && line.HeightCm is > 0;
}
