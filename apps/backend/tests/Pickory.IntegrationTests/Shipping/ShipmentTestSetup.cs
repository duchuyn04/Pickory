using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping.Contracts.Addresses;
using Pickory.Modules.Shipping.Contracts.Parcels;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.Persistence;

// Contracts cũng có kiểu `Shipment` cho bên gọi. Helper này đọc entity của module.
using ShipmentEntity = Pickory.Modules.Shipping.Entities.Shipment;

namespace Pickory.IntegrationTests.Shipping;

// Dữ liệu và helper dùng chung cho test tạo, hủy và tra vận đơn của SHIP-07.
internal static class ShipmentTestSetup
{
    public static readonly CreateShipmentRequest SampleRequest = CreateRequest("ship07-ref-1");

    // Tên tỉnh và tên phường trong tham số cố ý khác danh mục (`Hồ Chí Minh`, `Phường Bến Nghé`).
    // Nhờ vậy test chứng minh service gửi GHN tên lấy từ danh mục.
    public static CreateShipmentRequest CreateRequest(
        string reference,
        string phone = "0987 654 321",
        int codAmount = 250_000,
        int insuredValue = 1_000_000,
        string? note = null) =>
        new(
            reference,
            new ShippingRecipient(
                "Nguyễn Văn Khách",
                phone,
                new ShippingAddress("1000001", "Tên tỉnh sai trong tham số", "1003646", "Tên phường sai trong tham số", "12 Phố Test")),
            new Parcel(1500, 30, 20, 12),
            [new ShipmentLine("Điện thoại Pickory", 2), new ShipmentLine("Ốp lưng", 1)],
            codAmount,
            insuredValue,
            note);

    // Xóa lịch sử trước vì khóa ngoại chặn xóa vận đơn còn lịch sử. Sau đó dọn cấu hình và danh mục.
    public static async Task ResetAsync(WebApplicationFactory<Program> factory)
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
            await QuoteTestSetup.ResetAsync(factory);
            await shipping.ShipmentEvents.ExecuteDeleteAsync();
            await shipping.Shipments.ExecuteDeleteAsync();
        }
    }

    public static async Task UpdateServiceTypeAsync(WebApplicationFactory<Program> factory, int? serviceTypeId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        await shipping.Settings.ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.DefaultServiceTypeId, serviceTypeId));
    }

    public static async Task DeleteSettingsAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        await shipping.Settings.ExecuteDeleteAsync();
    }

    // Chèn thẳng một vận đơn còn hiệu lực để dựng tình huống mã vận đơn GHN trùng mà không đi qua service.
    public static async Task InsertShipmentAsync(
        WebApplicationFactory<Program> factory, string reference, string trackingCode)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        shipping.Shipments.Add(new ShipmentEntity
        {
            Ref = reference,
            TrackingCode = trackingCode,
            Fee = 1000,
            CodAmount = 0,
            Parcel = new Parcel(500, 10, 10, 10),
            Status = Pickory.Modules.Shipping.Contracts.Shipments.ShipmentStatus.PendingPickup,
            RawStatus = "ready_to_pick",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await shipping.SaveChangesAsync();
    }

    public static async Task<List<ShipmentEntity>> ReadShipmentsAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        return await shipping.Shipments.AsNoTracking().OrderBy(shipment => shipment.Ref).ToListAsync();
    }

    public static async Task<List<ShipmentEvent>> ReadEventsAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        return await shipping.ShipmentEvents.AsNoTracking().OrderBy(shipmentEvent => shipmentEvent.OccurredAt).ToListAsync();
    }
}
