using Pickory.Modules.Shipping.Contracts.Addresses;
using Pickory.Modules.Shipping.Contracts.Parcels;
using Pickory.Modules.Shipping.Contracts.Quotes;
using Pickory.Modules.Shipping.Contracts.Shipments;

namespace Pickory.Modules.Shipping.Contracts;

// Cổng công khai của module shipping. `commerce` gọi cổng này, không gọi GHN trực tiếp.
public interface IShippingService
{
    // Báo phí và ngày giao dự kiến. `codAmount` và `insuredValue` tính bằng đồng.
    Task<Quote> QuoteAsync(
        ShippingAddress to, Parcel parcel, int codAmount, int insuredValue, CancellationToken cancellationToken);

    // Tạo vận đơn GHN. Không thử lại khi lỗi mạng, và gọi lại với cùng `Ref` không tạo đơn thứ hai.
    Task<Shipment> CreateShipmentAsync(CreateShipmentRequest request, CancellationToken cancellationToken);

    // Hủy vận đơn còn hủy được. Vận đơn đã hủy thì không làm gì.
    Task CancelShipmentAsync(string shipmentId, CancellationToken cancellationToken);

    // Trả `null` khi không có vận đơn.
    Task<Shipment?> GetShipmentAsync(string shipmentId, CancellationToken cancellationToken);

    // Tra vận đơn còn hiệu lực theo mã đơn gốc (`Ref`). Trả `null` khi đơn chưa có vận đơn, hoặc vận đơn của đơn đã hủy.
    Task<Shipment?> GetShipmentByRefAsync(string reference, CancellationToken cancellationToken);

    // Gộp các dòng hàng thành một kiện.
    Parcel BuildParcel(IReadOnlyList<ParcelItem> items);
}
