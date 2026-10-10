using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Shipping.Contracts.Errors;

// Mã vận đơn không có trong DB. Bên gọi dùng mã sai hoặc vận đơn thuộc môi trường khác.
public sealed class ShipmentNotFoundException()
    : AppException(ErrorCode, 404, "Không tìm thấy vận đơn.")
{
    public const string ErrorCode = "SHIPMENT_NOT_FOUND";
}
