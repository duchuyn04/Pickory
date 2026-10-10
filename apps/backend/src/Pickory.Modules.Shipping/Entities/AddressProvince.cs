namespace Pickory.Modules.Shipping.Entities;

// Tỉnh hoặc thành phố đồng bộ từ GHN. `Code` là `_id` của GHN ở dạng chuỗi.
public sealed class AddressProvince
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime SyncedAt { get; set; }

    public List<AddressWard> Wards { get; set; } = [];
}
