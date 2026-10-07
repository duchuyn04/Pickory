using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Specs;

namespace Pickory.Modules.Catalog.Products;

// Quy tắc trạng thái và điều kiện đăng bán, giống `domain/status.ts` của bản Node.
public static class ProductPublishing
{
    // Bảng chuyển trạng thái hợp lệ. Sản phẩm đã phát hành không quay về bản nháp.
    private static readonly Dictionary<ProductStatus, ProductStatus[]> AllowedTransitions = new()
    {
        [ProductStatus.Draft] = [ProductStatus.Draft, ProductStatus.Active],
        [ProductStatus.Active] = [ProductStatus.Active, ProductStatus.Hidden, ProductStatus.Discontinued],
        [ProductStatus.Hidden] = [ProductStatus.Hidden, ProductStatus.Active, ProductStatus.Discontinued],
        [ProductStatus.Discontinued] = [ProductStatus.Discontinued, ProductStatus.Active, ProductStatus.Hidden],
    };

    public static bool CanTransition(ProductStatus from, ProductStatus to)
    {
        return AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }

    public static PublishCheckResult CheckPublishable(ProductPublishableInput product)
    {
        var reasons = new List<string>();

        // Thiếu thông số bắt buộc thì trang sản phẩm không đủ thông tin cho khách so sánh.
        var specValidation = SpecValidator.Validate(product.Specs, product.AttributeDefinitions);
        reasons.AddRange(specValidation.Errors.Select(error => $"Lỗi thông số kỹ thuật: {error.Message}"));

        // Khách cần thấy ảnh sản phẩm trước khi mua.
        if (product.ImageCount == 0)
        {
            reasons.Add("Sản phẩm phải có ít nhất 1 ảnh để đăng bán.");
        }

        var enabledSkus = product.Skus.Where(sku => sku.Enabled).ToList();
        if (enabledSkus.Count == 0)
        {
            reasons.Add("Sản phẩm phải có ít nhất 1 biến thể đang bật.");
            return new PublishCheckResult(reasons);
        }

        foreach (var sku in enabledSkus)
        {
            // Mỗi lý do ghi mã SKU để admin tìm đúng biến thể cần sửa.
            reasons.AddRange(CheckSku(sku));
        }

        return new PublishCheckResult(reasons);
    }

    private static IEnumerable<string> CheckSku(SkuPublishableInput sku)
    {
        // Giá thiếu không qua được `ValidatePrice`, vì hàm này nhận số `decimal` chứ không nhận null.
        if (sku.Price is not { } price)
        {
            yield return $"SKU {sku.Code}: thiếu giá bán.";
        }
        else
        {
            // Dùng chung quy tắc giá với lúc lưu sản phẩm, để hai nơi không lệch nhau.
            var priceCheck = ProductPricing.ValidatePrice(price, sku.CompareAtPrice);
            if (!priceCheck.Valid)
            {
                yield return $"SKU {sku.Code}: {priceCheck.Error}";
            }
        }

        // Phí vận chuyển tính theo cân nặng và kích thước đóng gói của từng SKU.
        var missingShippingInfo = sku.WeightGrams is not > 0 || sku.LengthCm is not > 0
            || sku.WidthCm is not > 0 || sku.HeightCm is not > 0;
        if (missingShippingInfo)
        {
            yield return $"SKU {sku.Code}: thiếu cân nặng hoặc kích thước đóng gói.";
        }
    }
}
