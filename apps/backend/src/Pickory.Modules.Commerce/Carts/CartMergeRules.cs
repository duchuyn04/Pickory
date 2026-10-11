namespace Pickory.Modules.Commerce.Carts;

// Luật gộp giỏ vãng lai vào giỏ tài khoản. Hàm thuần, nên unit test không cần DB.
public static class CartMergeRules
{
    // Trả toàn bộ dòng của giỏ tài khoản sau khi gộp. Dòng của giỏ tài khoản không bị bỏ.
    public static IReadOnlyList<CartLineInput> Merge(
        IReadOnlyList<CartLineInput> accountLines,
        IReadOnlyList<CartLineInput> guestLines,
        int maxQuantityPerSku,
        int maxCartLines)
    {
        var merged = accountLines.ToDictionary(line => line.SkuId, line => line.Quantity, StringComparer.Ordinal);

        // Duyệt theo `skuId` để kết quả không phụ thuộc thứ tự đọc từ DB. Dòng nào bị bỏ vì giới hạn số dòng luôn xác định.
        foreach (var guestLine in guestLines.OrderBy(line => line.SkuId, StringComparer.Ordinal))
        {
            if (merged.TryGetValue(guestLine.SkuId, out var existingQuantity))
            {
                // Cộng rồi kẹp ở giới hạn. Không hạ số lượng đang có của tài khoản, kể cả khi nó đã vượt giới hạn.
                var summed = checked(existingQuantity + guestLine.Quantity);
                merged[guestLine.SkuId] = Math.Max(existingQuantity, Math.Min(summed, maxQuantityPerSku));
                continue;
            }

            // Giỏ đã đủ số dòng thì bỏ dòng mới. Dòng cộng vào SKU có sẵn không tốn thêm dòng.
            if (merged.Count >= maxCartLines)
            {
                continue;
            }

            merged[guestLine.SkuId] = Math.Min(guestLine.Quantity, maxQuantityPerSku);
        }

        return merged.Select(pair => new CartLineInput(pair.Key, pair.Value)).ToList();
    }
}
