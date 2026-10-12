using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Pickory.Shared.Email;

// Đăng ký cổng email dùng chung. Mọi module gửi thư qua `IEmailSender`, nên chỉ cần một nơi chọn bộ gửi.
public static class EmailServiceCollectionExtensions
{
    // Dự án chưa nối dịch vụ gửi thư thật. Thư được ghi ra file để người dev mở link.
    public static IServiceCollection AddDevOutboxEmail(this IServiceCollection services)
    {
        // Bộ gửi cần đồng hồ để đặt tên file. `TryAdd` giữ đồng hồ giả mà test đã đăng ký trước.
        services.TryAddSingleton(TimeProvider.System);
        // `TryAdd` để test và môi trường khác thay `IEmailSender` mà không bị bản dev ghi đè.
        services.TryAddSingleton<IEmailSender>(CreateDevOutboxEmailSender);
        return services;
    }

    // `DEV_OUTBOX_DIR` cho phép E2E tách thư riêng. Mặc định là `.dev-outbox` trong thư mục chạy API, giống bản Node.
    private static DevOutboxEmailSender CreateDevOutboxEmailSender(IServiceProvider provider)
    {
        var configuredDirectory = provider.GetRequiredService<IConfiguration>()["DEV_OUTBOX_DIR"];
        var outboxDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(Directory.GetCurrentDirectory(), ".dev-outbox")
            : configuredDirectory;
        return new DevOutboxEmailSender(
            outboxDirectory,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<DevOutboxEmailSender>>());
    }
}
