namespace Pickory.Modules.Commerce.Entities;

// Giỏ hàng của khách vãng lai (`AnonymousId`) hoặc của tài khoản (`UserId`). Giỏ không lưu giá.
public sealed class Cart
{
    public string Id { get; set; } = null!;
    public string? AnonymousId { get; set; }
    public string? UserId { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<CartItem> Items { get; set; } = [];
}
