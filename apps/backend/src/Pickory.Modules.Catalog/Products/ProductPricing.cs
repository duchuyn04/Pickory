namespace Pickory.Modules.Catalog.Products;

// Quy tắc giá của sản phẩm, giống `domain/pricing.ts` của bản Node.
public static class ProductPricing
{
    // Giá nhận dạng `decimal` để phát hiện số lẻ trong dữ liệu gửi lên. Tiền VND không có phần lẻ.
    public static PriceValidationResult ValidatePrice(decimal price, decimal? compareAtPrice)
    {
        if (price <= 0 || decimal.Truncate(price) != price)
        {
            return new PriceValidationResult("Giá bán phải là số nguyên dương lớn hơn 0.");
        }

        // Giá niêm yết trống nghĩa là sản phẩm không có chương trình giảm giá.
        if (compareAtPrice is not { } listedPrice)
        {
            return new PriceValidationResult(null);
        }

        if (decimal.Truncate(listedPrice) != listedPrice)
        {
            return new PriceValidationResult("Giá niêm yết phải là số nguyên.");
        }

        // Giá niêm yết không cao hơn giá bán sẽ hiển thị mức giảm giá sai.
        if (listedPrice <= price)
        {
            return new PriceValidationResult("Giá niêm yết phải lớn hơn giá bán thực tế.");
        }

        return new PriceValidationResult(null);
    }

    public static DisplayPrice ComputeDisplayPrice(IReadOnlyList<SkuPricingInput> skus)
    {
        // SKU đã tắt không bán cho khách, nên không tính vào giá hiển thị.
        var enabledSkus = skus.Where(sku => sku.Enabled).ToList();
        if (enabledSkus.Count == 0)
        {
            return new DisplayPrice(null, InStock: false);
        }

        // Ưu tiên giá thấp nhất trong các SKU còn hàng.
        var inStockSkus = enabledSkus.Where(sku => sku.Stock > 0).ToList();
        if (inStockSkus.Count > 0)
        {
            return new DisplayPrice(inStockSkus.Min(sku => sku.Price), InStock: true);
        }

        // Hết hàng toàn bộ thì vẫn hiển thị giá thấp nhất để khách tham khảo.
        return new DisplayPrice(enabledSkus.Min(sku => sku.Price), InStock: false);
    }
}
