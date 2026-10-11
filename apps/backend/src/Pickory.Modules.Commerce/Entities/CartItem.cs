namespace Pickory.Modules.Commerce.Entities;

// Một dòng của giỏ. `SkuId` trỏ tới catalog bằng giá trị, không có khóa ngoại giữa hai module.
public sealed class CartItem
{
    public string CartId { get; set; } = string.Empty;
    public string SkuId { get; set; } = string.Empty;
    public int Quantity { get; set; }

    public Cart? Cart { get; set; }
}
