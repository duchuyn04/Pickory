using Pickory.Modules.Catalog.Contracts.Stock;

namespace Pickory.IntegrationTests.Commerce;

// Hàng rào đặt trước bước trả kho, để hai yêu cầu hủy đồng thời cùng đến bước này.
// Khi đơn bị khóa dòng, chỉ một yêu cầu đến được, nên hàng rào hết hạn và yêu cầu đó đi tiếp một mình.
internal sealed class StockReleaseGate
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(3);

    private TaskCompletionSource _allArrived = NewSource();
    private int _participants;
    private int _arrived;

    public int ReleaseCalls => Volatile.Read(ref _releaseCalls);

    private int _releaseCalls;

    // Đặt số yêu cầu cần đến hàng rào. Số 0 là tắt hàng rào.
    public void Arm(int participants)
    {
        _allArrived = NewSource();
        _arrived = 0;
        _releaseCalls = 0;
        _participants = participants;
    }

    public void Disarm() => _participants = 0;

    public async Task PassAsync()
    {
        Interlocked.Increment(ref _releaseCalls);
        if (_participants == 0)
        {
            return;
        }

        if (Interlocked.Increment(ref _arrived) >= _participants)
        {
            _allArrived.TrySetResult();
        }

        // Chờ đủ người hoặc hết hạn. Hết hạn nghĩa là yêu cầu kia đang bị khóa dòng chặn lại.
        await Task.WhenAny(_allArrived.Task, Task.Delay(WaitLimit));
    }

    private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

// Bọc `IStockService` thật. Chỉ thêm bước chờ ở `ReleaseAsync`, mọi hành vi khác giữ nguyên.
internal sealed class GatedStockService(IStockService inner, StockReleaseGate gate) : IStockService
{
    public Task<ReserveResult> ReserveAsync(
        string orderId, IReadOnlyList<StockItem> items, StockCallerTransaction? caller, CancellationToken cancellationToken) =>
        inner.ReserveAsync(orderId, items, caller, cancellationToken);

    public async Task ReleaseAsync(string orderId, StockCallerTransaction? caller, CancellationToken cancellationToken)
    {
        await gate.PassAsync();
        await inner.ReleaseAsync(orderId, caller, cancellationToken);
    }
}
