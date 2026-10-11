using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Settings;

namespace Pickory.Modules.Commerce.Carts;

// Dựng response giỏ từ dòng DB và ảnh chụp catalog. Hàm thuần, nên unit test không cần DB.
public static class CartResponseBuilder
{
    public static CartResponse Build(
        IReadOnlyList<CartLineInput> lines,
        IReadOnlyDictionary<string, SkuSnapshot> skusById,
        CommerceLimits limits)
    {
        // Sắp theo `skuId` để thứ tự dòng ổn định giữa các lần xem. Cột thời điểm thêm không có trong bảng.
        var responseLines = lines
            .OrderBy(line => line.SkuId, StringComparer.Ordinal)
            .Select(line => BuildLine(line, skusById.GetValueOrDefault(line.SkuId)))
            .ToList();

        // Dùng `checked` để tổng vượt `int` ném lỗi thay vì quay vòng thành số âm.
        var subtotal = 0;
        foreach (var line in responseLines.Where(line => line.Purchasable))
        {
            subtotal = checked(subtotal + line.LineTotal!.Value);
        }

        return new CartResponse(
            responseLines,
            subtotal,
            HasUnavailableItems: responseLines.Any(line => !line.Purchasable),
            limits.MaxQuantityPerSku,
            limits.MaxCartLines);
    }

    private static CartLineResponse BuildLine(CartLineInput line, SkuSnapshot? sku)
    {
        var reason = CartRules.FindUnavailableReason(sku, line.Quantity);

        // Dòng `removed` không có dữ liệu catalog được phép hiện. Trả `null` cho mọi trường sản phẩm và giá.
        if (reason == CartUnavailableReason.Removed || sku is null)
        {
            return new CartLineResponse(
                line.SkuId, line.Quantity,
                ProductId: null, ProductName: null, ProductSlug: null, OptionValues: null, Image: null,
                UnitPrice: null, CompareAtPrice: null, LineTotal: null, Stock: null,
                Purchasable: false, UnavailableReason: reason);
        }

        // Dòng không mua được vẫn có giá, để khách thấy mình đã chọn gì. Tạm tính sẽ bỏ dòng này.
        return new CartLineResponse(
            line.SkuId, line.Quantity,
            sku.ProductId, sku.ProductName, sku.ProductSlug, sku.OptionValues, sku.Image,
            sku.Price, sku.CompareAtPrice, checked(sku.Price * line.Quantity), sku.Stock,
            Purchasable: reason is null, UnavailableReason: reason);
    }
}
