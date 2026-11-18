namespace Pickory.Modules.Commerce.Entities;

// Cấu hình của commerce, bảng chỉ có một dòng. Khóa API của SePay nằm trong biến môi trường, không nằm trong bảng này.
public sealed class CommerceSettings
{
    public string Id { get; set; } = string.Empty;
    public int FreeShippingThreshold { get; set; }
    public int CodMaxAmount { get; set; }
    public int MaxQuantityPerSku { get; set; }
    public int MaxCartLines { get; set; }
    public string? SepayAccountNumber { get; set; }
    public string? SepayBankCode { get; set; }
    public string? ShopName { get; set; }
    public string? ShopAddress { get; set; }
    public string? ShopPhone { get; set; }
    public string? ShopTaxCode { get; set; }
    public DateTime UpdatedAt { get; set; }
}
