using System.Text.Json;

namespace Pickory.Modules.Commerce.Addresses;

// Một địa chỉ trong sổ. `Address` có cùng dạng với địa chỉ giao hàng của đơn: mã và tên tỉnh, mã và tên phường, địa chỉ cụ thể.
public sealed record SavedAddressResponse(
    string Id, string Label, string Name, string Phone, JsonElement Address, bool IsDefault);
