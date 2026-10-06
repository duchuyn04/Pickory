using Microsoft.Extensions.Time.Testing;

namespace Pickory.UnitTests.TestDoubles;

// Đẩy đồng hồ giả và chờ continuation chạy xong. Code async tiếp tục trên luồng khác sau khi timer kích hoạt.
public static class FakeTimeDriver
{
    private static readonly TimeSpan RealWaitLimit = TimeSpan.FromSeconds(5);

    // Chờ điều kiện đúng bằng thời gian thật, tối đa 5 giây. Test không phụ thuộc thứ tự lập lịch của luồng.
    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + RealWaitLimit;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Điều kiện của test không đúng sau 5 giây thời gian thật.");
            }

            await Task.Delay(5);
        }
    }

    // Đẩy đồng hồ từng bước nhỏ tới khi task xong. Bước nhỏ giữ đúng thứ tự giữa backoff và timeout.
    public static async Task AdvanceUntilCompletedAsync(FakeTimeProvider time, Task task, TimeSpan step)
    {
        var deadline = DateTime.UtcNow + RealWaitLimit;
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Task của test không xong sau 5 giây thời gian thật.");
            }

            time.Advance(step);
            await Task.Delay(2);
        }
    }
}
