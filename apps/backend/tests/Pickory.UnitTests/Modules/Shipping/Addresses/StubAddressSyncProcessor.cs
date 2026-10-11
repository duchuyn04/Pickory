using Pickory.Modules.Shipping.Addresses;

namespace Pickory.UnitTests.Modules.Shipping.Addresses;

// Bộ đồng bộ giả cho test worker và lệnh. Test đặt kết quả hoặc lỗi cho từng lần gọi và đếm số lần gọi.
internal sealed class StubAddressSyncProcessor : IAddressSyncProcessor
{
    private int _callCount;

    // Kết quả trả về mặc định khi test không đặt `OnSync`.
    public AddressSyncResult Result { get; set; } = AddressSyncResult.Success(0, 0);

    // Nhận số thứ tự lần gọi, bắt đầu từ 1. Test ném lỗi hoặc trả kết quả khác nhau theo lần gọi.
    public Func<int, AddressSyncResult>? OnSync { get; set; }

    public int CallCount => Volatile.Read(ref _callCount);

    public Task<AddressSyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        var callNumber = Interlocked.Increment(ref _callCount);
        return Task.FromResult(OnSync is null ? Result : OnSync(callNumber));
    }
}
