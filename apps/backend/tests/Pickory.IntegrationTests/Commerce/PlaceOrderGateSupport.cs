using System.Collections.Concurrent;
using Pickory.Modules.Shipping.Contracts.Addresses;
using Pickory.Shared.Email;

namespace Pickory.IntegrationTests.Commerce;

// Lớp hỗ trợ của test COM-28 trong `PlaceOrderTests`: cổng chặn lần tra địa chỉ đầu tiên và hộp thư đếm được.

// Cổng chặn lần tra danh mục địa chỉ đầu tiên của host test.
// Request bị chặn đã tra khóa đặt đơn và chưa đọc giỏ, vì `CalculateAsync` tra danh mục trước khi đọc giỏ.
internal sealed class AddressLookupGate
{
    // Mọi lần chờ có hạn 30 giây. Test đỏ rõ ràng thay vì treo khi thứ tự bị đổi.
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _lookups;

    public async Task PassAsync()
    {
        // Chỉ lần tra đầu tiên bị chặn. `Interlocked` giữ đúng khi hai request tra cùng lúc.
        if (Interlocked.Increment(ref _lookups) != 1)
        {
            return;
        }

        _entered.SetResult();
        await _released.Task.WaitAsync(WaitLimit);
    }

    public Task WaitUntilEnteredAsync() => _entered.Task.WaitAsync(WaitLimit);

    public void Open() => _released.TrySetResult();
}

// Bọc danh mục địa chỉ thật. Chỉ thêm cổng trước khi chuyển lời gọi, nên kết quả tra không đổi.
internal sealed class GatedAddressDirectory(IAddressDirectory inner, AddressLookupGate gate) : IAddressDirectory
{
    public async Task<AddressNames?> FindAsync(string provinceCode, string wardCode, CancellationToken cancellationToken)
    {
        await gate.PassAsync();
        return await inner.FindAsync(provinceCode, wardCode, cancellationToken);
    }
}

// Sender giả ghi mã đơn của mỗi thư đã gửi. Host dựng riêng có hộp thư riêng, nên test đếm thư ở đây.
internal sealed class CountingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<string> _orderCodes = new();

    public IReadOnlyList<string> OrderCodes => [.. _orderCodes];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _orderCodes.Enqueue(message.Metadata["orderCode"]);
        return Task.CompletedTask;
    }
}
