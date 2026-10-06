namespace Pickory.IntegrationTests.Infrastructure;

// Chờ điều kiện đúng bằng thời gian thật, tối đa 5 giây. Code async chạy tiếp trên luồng khác.
public static class Eventually
{
    private static readonly TimeSpan RealWaitLimit = TimeSpan.FromSeconds(5);

    public static async Task UntilAsync(Func<bool> condition)
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
}
