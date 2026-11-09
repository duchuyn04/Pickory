using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Realtime;

namespace Pickory.IntegrationTests.SupportDesk;

// Dụng cụ chung cho test SSE của nhân viên và khách.
// Công cụ dựng host có đồng hồ giả, đọc ghi bảng `staff_presence` và chờ số kết nối của hai hub.
internal static class SupportDeskTestKit
{
    // Cột `lastSeenAt` có độ chính xác mili giây. Giờ bắt đầu cắt đến mili giây để so sánh giờ đã ghi với đồng hồ giả không lệch.
    public static DateTimeOffset NowAtMillisecondPrecision()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
    }

    // Xóa mọi dòng trực. Các test dùng chung một DB, nên mỗi test bắt đầu và kết thúc từ bảng rỗng.
    public static async Task ClearPresenceAsync(IntegrationDatabaseFixture database)
    {
        await using var context = database.CreateSupportDeskContext();
        await context.StaffPresences.ExecuteDeleteAsync();
    }

    public static async Task SetPresenceRowAsync(
        IntegrationDatabaseFixture database, string staffId, StaffPresenceStatus status, DateTimeOffset lastSeenAt)
    {
        await using var context = database.CreateSupportDeskContext();
        context.StaffPresences.Add(new StaffPresence
        {
            StaffId = staffId,
            Status = status,
            LastSeenAt = lastSeenAt.UtcDateTime,
        });
        await context.SaveChangesAsync();
    }

    public static async Task<StaffPresence?> ReadPresenceRowAsync(IntegrationDatabaseFixture database, string staffId)
    {
        await using var context = database.CreateSupportDeskContext();
        return await context.StaffPresences.AsNoTracking().SingleOrDefaultAsync(row => row.StaffId == staffId);
    }

    // Host riêng với đồng hồ giả bắt đầu ở giờ thật. Phiên đăng nhập tạo bằng giờ thật vẫn còn hạn trong host này.
    // `configureServices` cho test đăng ký thêm dịch vụ thử, ví dụ handler theo dõi thứ tự commit.
    public static WebApplicationFactory<Program> CreateHostWithClock(
        PickoryWebApplicationFactory factory,
        FakeTimeProvider clock,
        Action<IServiceCollection>? configureServices = null)
    {
        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Đồng hồ giả thay đồng hồ thật của cả host, nên nhịp tim và job `away` chỉ chạy khi test tiến đồng hồ.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            configureServices?.Invoke(services);
        }));
    }

    // Chờ hub nhân viên có đúng số kết nối mong muốn.
    public static Task WaitForConnectionCountAsync(SupportEventHub hub, int expectedCount) =>
        WaitForConnectionCountAsync(() => hub.ConnectionCount, "Hub nhân viên", expectedCount);

    // Chờ hub phía khách có đúng số kết nối mong muốn. Mọi hội thoại dùng chung một hub.
    public static Task WaitForConnectionCountAsync(CustomerEventHub hub, int expectedCount) =>
        WaitForConnectionCountAsync(() => hub.ConnectionCount, "Hub phía khách", expectedCount);

    // Chờ tới khi số kết nối bằng mong muốn, tối đa 5 giây. Vòng chờ ngắn thay cho chờ cố định, nên test nhanh khi máy nhanh.
    private static async Task WaitForConnectionCountAsync(Func<int> readCount, string hubName, int expectedCount)
    {
        var deadline = DateTime.UtcNow + SseTestStream.DefaultTimeout;
        while (readCount() != expectedCount)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"{hubName} có {readCount()} kết nối, test chờ {expectedCount}.");
            }

            await Task.Delay(10);
        }
    }
}
