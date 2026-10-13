using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Shipping.Contracts;
using Pickory.Modules.Shipping.Contracts.Parcels;

namespace Pickory.Modules.Commerce.Orders;

// Dựng kiện hàng từ dòng hàng của đơn, dùng cùng luật với checkout. Dùng cho gợi ý kiện và kiện mặc định khi tạo vận đơn.
public sealed class OrderParcelResolver(ICatalogReader catalog, IShippingService shipping)
{
    // Trả `null` khi không dựng được kiện: SKU không còn trong catalog hoặc thiếu cân nặng hay kích thước.
    // Hệ thống không đoán số đo, vì kiện sai làm phí và phiếu GHN sai.
    public async Task<Parcel?> BuildAsync(IReadOnlyList<OrderItem> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return null;
        }

        // Số đo hiện tại của SKU lấy từ catalog, vì dòng đơn chỉ chụp tên, mã và giá.
        var skuIds = items.Select(item => item.SkuId).Distinct().ToList();
        var snapshots = await catalog.GetSkusAsync(skuIds, cancellationToken);
        var snapshotsBySkuId = snapshots.ToDictionary(snapshot => snapshot.SkuId);

        var lines = new List<CheckoutLine>(items.Count);
        foreach (var item in items)
        {
            // SKU đã bị xóa khỏi catalog thì không còn số đo để dựng kiện.
            if (!snapshotsBySkuId.TryGetValue(item.SkuId, out var sku))
            {
                return null;
            }

            lines.Add(new CheckoutLine(
                item.SkuId, item.Quantity, item.UnitPrice, sku.WeightGrams, sku.LengthCm, sku.WidthCm, sku.HeightCm));
        }

        // Luật thiếu số đo dùng chung với checkout, nên đơn và giỏ hàng cùng từ chối đúng một tập SKU.
        if (CheckoutParcelRules.FindSkuIdsMissingDimensions(lines).Count > 0)
        {
            return null;
        }

        try
        {
            return shipping.BuildParcel(CheckoutParcelRules.BuildParcelItems(lines));
        }
        catch (ArgumentException)
        {
            // Tổng cân nặng hoặc chiều cao vượt `int` là dữ liệu catalog sai. Không có gợi ý hợp lệ nào để trả.
            return null;
        }
    }
}
