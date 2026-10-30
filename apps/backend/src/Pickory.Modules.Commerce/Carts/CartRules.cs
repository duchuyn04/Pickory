using Pickory.Modules.Catalog.Contracts.Reading;

namespace Pickory.Modules.Commerce.Carts;

// Nhãn lý do một dòng giỏ không mua được. Giá trị là chuỗi cố định mà frontend đọc.
public static class CartUnavailableReason
{
    public const string SkuDisabled = "sku_disabled";
    public const string ProductUnavailable = "product_unavailable";
    public const string OutOfStock = "out_of_stock";
    public const string InsufficientStock = "insufficient_stock";
    public const string Removed = "removed";
}

// Kết quả kiểm một SKU trước khi thêm vào giỏ.
public enum AddCheck
{
    Ok,
    NotFound,
    Unavailable,
}

// Luật thuần của giỏ hàng, tách khỏi DB và HTTP để unit test kiểm từng giá trị biên.
public static class CartRules
{
    // Trạng thái sản phẩm mà khách không được thấy. `ICatalogReader.GetSkusAsync` vẫn trả các sản phẩm này.
    private static bool IsHiddenFromCustomer(string productStatus) =>
        productStatus is "draft" or "hidden";

    // Trả `null` khi dòng mua được. Thứ tự kiểm đi từ nguyên nhân gốc đến nguyên nhân phụ.
    public static string? FindUnavailableReason(SkuSnapshot? sku, int quantity)
    {
        // SKU không có trong catalog, hoặc sản phẩm khách không được thấy, thì dòng không còn dữ liệu để hiện.
        if (sku is null || sku.ProductDeleted || IsHiddenFromCustomer(sku.ProductStatus))
        {
            return CartUnavailableReason.Removed;
        }

        // Sản phẩm ngừng bán là nguyên nhân gốc, nên thắng nhãn SKU tắt.
        if (sku.ProductStatus != "active")
        {
            return CartUnavailableReason.ProductUnavailable;
        }

        if (!sku.Enabled)
        {
            return CartUnavailableReason.SkuDisabled;
        }

        if (sku.Stock <= 0)
        {
            return CartUnavailableReason.OutOfStock;
        }

        // Giỏ không giữ hàng, nên số lượng trong giỏ có thể vượt tồn kho sau khi khách thêm.
        if (quantity > sku.Stock)
        {
            return CartUnavailableReason.InsufficientStock;
        }

        return null;
    }

    // Kiểm SKU lúc thêm vào giỏ. Số lượng không so với tồn kho, vì giỏ không giữ hàng.
    public static AddCheck CheckAddable(SkuSnapshot? sku)
    {
        // SKU lạ và sản phẩm nháp cùng trả "không tìm thấy". Khách không phân biệt được sản phẩm nháp với id sai.
        if (sku is null || sku.ProductDeleted || IsHiddenFromCustomer(sku.ProductStatus))
        {
            return AddCheck.NotFound;
        }

        if (sku.ProductStatus != "active" || !sku.Enabled || sku.Stock <= 0)
        {
            return AddCheck.Unavailable;
        }

        return AddCheck.Ok;
    }

    public static bool ExceedsQuantityLimit(int totalQuantity, int maxQuantityPerSku) =>
        totalQuantity > maxQuantityPerSku;

    public static bool ExceedsLineLimit(int lineCount, int maxCartLines) =>
        lineCount > maxCartLines;
}
