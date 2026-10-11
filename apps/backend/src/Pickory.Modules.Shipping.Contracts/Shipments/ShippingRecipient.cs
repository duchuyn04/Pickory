using Pickory.Modules.Shipping.Contracts.Addresses;

namespace Pickory.Modules.Shipping.Contracts.Shipments;

// Người nhận hàng. `Phone` được kiểm theo luật số điện thoại Việt Nam và gửi GHN ở dạng `0xxxxxxxxx`.
public sealed record ShippingRecipient(string Name, string Phone, ShippingAddress Address);
