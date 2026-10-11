using Pickory.Modules.Catalog.Contracts.Reading;

namespace Pickory.Modules.Commerce.Carts;

// Một dòng giỏ đọc từ DB. Dòng chỉ giữ SKU và số lượng, giá luôn lấy từ catalog.
public readonly record struct CartLineInput(string SkuId, int Quantity);

// Một dòng giỏ trả cho client. Dòng `removed` có mọi trường sản phẩm và giá bằng `null`.
public sealed record CartLineResponse(
    string SkuId,
    int Quantity,
    string? ProductId,
    string? ProductName,
    string? ProductSlug,
    IReadOnlyDictionary<string, string>? OptionValues,
    ProductImageInfo? Image,
    int? UnitPrice,
    int? CompareAtPrice,
    int? LineTotal,
    int? Stock,
    bool Purchasable,
    string? UnavailableReason);

// Giỏ trả cho client. `Subtotal` chỉ cộng các dòng mua được.
public sealed record CartResponse(
    IReadOnlyList<CartLineResponse> Items,
    int Subtotal,
    bool HasUnavailableItems,
    int MaxQuantityPerSku,
    int MaxCartLines);
