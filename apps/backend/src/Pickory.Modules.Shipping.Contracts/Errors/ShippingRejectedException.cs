using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Shipping.Contracts.Errors;

// Đơn vị vận chuyển từ chối yêu cầu, ví dụ địa chỉ không phục vụ. Thử lại cùng yêu cầu sẽ bị từ chối tiếp.
public sealed class ShippingRejectedException : AppException
{
    public const string ErrorCode = "SHIPPING_REJECTED";

    // Thông báo của đơn vị vận chuyển được nối sau tiền tố chung.
    public ShippingRejectedException(string? carrierMessage)
        : base(ErrorCode, 422, BuildMessage(carrierMessage))
    {
    }

    private ShippingRejectedException(string finalMessage, ShippingRejectionKind kind)
        : base(ErrorCode, 422, finalMessage)
    {
        Kind = kind;
    }

    // Lý do từ chối. Là `Other` khi không nhận ra lý do cụ thể.
    public ShippingRejectionKind Kind { get; } = ShippingRejectionKind.Other;

    // Dùng khi bên gọi đã có câu tiếng Việt hoàn chỉnh. Hàm này không thêm tiền tố, nên câu không bị lặp ý.
    public static ShippingRejectedException WithFinalMessage(string finalMessage, ShippingRejectionKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalMessage);
        return new ShippingRejectedException(finalMessage, kind);
    }

    private static string BuildMessage(string? carrierMessage)
    {
        return string.IsNullOrWhiteSpace(carrierMessage)
            ? "Đơn vị vận chuyển từ chối yêu cầu."
            : $"Đơn vị vận chuyển từ chối yêu cầu: {carrierMessage.Trim()}";
    }
}
