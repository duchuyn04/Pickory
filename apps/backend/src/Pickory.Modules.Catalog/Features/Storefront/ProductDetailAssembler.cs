using System.Text.Json;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Images;
using Pickory.Shared.Json;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Dựng `ProductDetail` từ sản phẩm đã nạp kèm hãng, SKU và ảnh. Chi tiết và lập chỉ mục dùng chung.
public static class ProductDetailAssembler
{
    public static ProductDetail Build(
        Product product, CategoryAttributeIndex categories, CloudinaryCredentials credentials)
    {
        // Danh mục mất khỏi bảng là dữ liệu sai. Báo lỗi rõ thay vì trả chi tiết thiếu breadcrumb.
        var category = categories.FindCategory(product.CategoryId)
            ?? throw new InvalidOperationException($"Sản phẩm {product.Id} trỏ tới danh mục không tồn tại.");

        // Chỉ SKU đang bật hiện cho khách. SKU tắt không bán nên không được chọn trên trang sản phẩm.
        var skus = product.Skus
            .Where(sku => sku.Enabled)
            .OrderBy(sku => sku.CreatedAt).ThenBy(sku => sku.Id)
            .Select(sku => new ProductSkuInfo(
                sku.Id, sku.Code, ReadOptionValues(sku.OptionValues), sku.Price, sku.CompareAtPrice, sku.Stock > 0))
            .ToList();

        var options = JsonSerializer.Deserialize<List<ProductOptionInfo>>(
            product.Options.RootElement.GetRawText(), PickoryJsonDefaults.Options) ?? [];

        return new ProductDetail(
            product.Id,
            product.Slug,
            product.Name,
            StatusName(product.Status),
            product.ShortDesc,
            // Sao chép phần tử JSON để phản hồi không phụ thuộc vòng đời tài liệu của entity.
            product.Description.RootElement.Clone(),
            product.WarrantyMonths,
            new BrandRef(product.Brand!.Id, product.Brand.Name, product.Brand.Slug),
            category,
            categories.FindParent(product.CategoryId),
            SpecEntries(product, categories),
            options,
            skus,
            OrderedImages(product).Select(image => ToImageInfo(image, credentials)).ToList(),
            product.MinPrice,
            product.InStock);
    }

    public static string StatusName(ProductStatus status) => status.ToString().ToLowerInvariant();

    public static IReadOnlyList<ProductImage> OrderedImages(Product product) =>
        product.Images.OrderBy(image => image.Position).ThenBy(image => image.Id).ToList();

    public static ProductImageInfo ToImageInfo(ProductImage image, CloudinaryCredentials credentials) =>
        new(ProductImageUrl.Build(credentials, image.PublicId), image.Alt, image.SkuId);

    public static JsonElement? ReadSpecValue(JsonDocument specs, string key)
    {
        // Giá trị `null` trong JSON coi như chưa nhập. Giao diện chỉ cần phân biệt có và không có giá trị.
        if (specs.RootElement.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null)
        {
            return value.Clone();
        }

        return null;
    }

    public static Dictionary<string, string> ReadOptionValues(JsonDocument optionValues) =>
        optionValues.Deserialize<Dictionary<string, string>>()!;

    // Mỗi thuộc tính hiệu lực có một dòng, kể cả khi sản phẩm chưa có giá trị. Bảng thông số nhờ đó đủ dòng.
    private static List<SpecEntry> SpecEntries(Product product, CategoryAttributeIndex categories) =>
        categories.EffectiveDefinitions(product.CategoryId)
            .Select(definition => new SpecEntry(
                definition.Key, definition.Label, definition.Group, definition.Type, definition.Unit,
                ReadSpecValue(product.Specs, definition.Key)))
            .ToList();
}
