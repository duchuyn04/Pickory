using Pickory.Modules.Shipping.Sync;

namespace Pickory.UnitTests.Modules.Shipping.Sync;

// Bộ đồng bộ giả đếm số lần gọi. Test worker kiểm chu kỳ mà không cần DB hay GHN.
internal sealed class StubShipmentStatusSyncProcessor : IShipmentStatusSyncProcessor
{
    private int _callCount;

    // Nhận số thứ tự lần gọi, bắt đầu từ 1. Test ném lỗi hoặc trả kết quả khác nhau theo lần gọi.
    public Func<int, ShipmentStatusSyncResult>? OnSync { get; set; }

    public int CallCount => Volatile.Read(ref _callCount);

    public Task<ShipmentStatusSyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        var callNumber = Interlocked.Increment(ref _callCount);
        return Task.FromResult(OnSync is null ? new ShipmentStatusSyncResult(0, 0, 0) : OnSync(callNumber));
    }
}
