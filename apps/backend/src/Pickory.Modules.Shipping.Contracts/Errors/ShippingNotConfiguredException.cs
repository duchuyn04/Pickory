using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Shipping.Contracts.Errors;

// Admin chưa cấu hình địa chỉ lấy hàng, nên shop chưa giao được. Thông báo không nêu chi tiết cấu hình cho khách.
public sealed class ShippingNotConfiguredException()
    : AppException(ErrorCode, 503, "Cửa hàng chưa sẵn sàng giao hàng, vui lòng thử lại sau.")
{
    public const string ErrorCode = "SHIPPING_NOT_CONFIGURED";
}
