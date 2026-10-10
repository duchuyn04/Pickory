using Pickory.Modules.Shipping.Settings;

namespace Pickory.Modules.Shipping.Features.AdminSettings;

// Thân response của GET và PUT. `GhnShopId` chỉ để admin xem, PUT không nhận trường này.
internal sealed record ShippingSettingsResponse(ShippingSettingsView? Settings, string? GhnShopId);
