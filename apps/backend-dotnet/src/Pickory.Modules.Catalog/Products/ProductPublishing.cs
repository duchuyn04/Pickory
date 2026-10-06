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

        if (enabledSkus.Any(sku => sku.Price is not > 0))
        {
            reasons.Add("Mọi biến thể đang bật phải có giá bán lớn hơn 0.");
        }

        // Phí vận chuyển tính theo cân nặng và kích thước đóng gói của từng SKU.
        var missingShippingInfo = enabledSkus.Any(sku =>
            sku.WeightGrams is not > 0 || sku.LengthCm is not > 0 || sku.WidthCm is not > 0 || sku.HeightCm is not > 0);
        if (missingShippingInfo)
        {
            reasons.Add("Mọi biến thể đang bật phải có đầy đủ cân nặng và kích thước đóng gói lớn hơn 0.");
        }

        return new PublishCheckResult(reasons);
    }
}
