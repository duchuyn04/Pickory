using System.Text.Json;

namespace Pickory.Modules.Commerce.Entities;

// Địa chỉ trong sổ địa chỉ của tài khoản. `UserId` trỏ tới identity bằng giá trị, không có khóa ngoại giữa hai module.
public sealed class SavedAddress
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public JsonDocument Address { get; set; } = null!;
    public bool IsDefault { get; set; }
}
