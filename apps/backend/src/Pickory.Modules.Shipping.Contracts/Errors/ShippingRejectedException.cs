using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Shipping.Contracts.Errors;

// Đơn vị vận chuyển từ chối yêu cầu, ví dụ địa chỉ không phục vụ. Thử lại cùng yêu cầu sẽ bị từ chối tiếp.
public sealed class ShippingRejectedException(string? carrierMessage)
    : AppException(ErrorCode, 422, BuildMessage(carrierMessage))
{
    public const string ErrorCode = "SHIPPING_REJECTED";

    private static string BuildMessage(string? carrierMessage)
    {
        return string.IsNullOrWhiteSpace(carrierMessage)
            ? "Đơn vị vận chuyển từ chối yêu cầu."
            : $"Đơn vị vận chuyển từ chối yêu cầu: {carrierMessage.Trim()}";
    }
}
