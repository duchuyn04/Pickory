namespace Pickory.Modules.Shipping.Entities;

// Phường hoặc xã đồng bộ từ GHN. Địa chỉ chỉ có hai cấp nên không có quận hoặc huyện.
public sealed class AddressWard
{
    public string Code { get; set; } = string.Empty;
    public string ProvinceCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime SyncedAt { get; set; }

    public AddressProvince? Province { get; set; }
}
