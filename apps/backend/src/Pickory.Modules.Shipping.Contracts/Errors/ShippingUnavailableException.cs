using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Shipping.Contracts.Errors;

// Đơn vị vận chuyển lỗi, quá hạn hoặc shop cấu hình sai. Bên gọi có thể thử lại sau.
public sealed class ShippingUnavailableException()
    : AppException(ErrorCode, 503, "Đơn vị vận chuyển đang gián đoạn, vui lòng thử lại sau.")
{
    public const string ErrorCode = "SHIPPING_UNAVAILABLE";
}
