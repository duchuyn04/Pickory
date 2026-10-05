using Microsoft.Extensions.DependencyInjection;
using Pickory.Shared.Events;

namespace Pickory.Shared.Extensions;

// Mở rộng IServiceCollection để đăng ký hạ tầng dùng chung cho toàn giải pháp.
public static class ServiceCollectionExtensions
{
    // Đăng ký bộ điều phối sự kiện và các dịch vụ nền tảng dùng chung.
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services)
    {
        // Gắn bộ điều phối sự kiện trong tiến trình làm dịch vụ duy nhất toàn app.
        services.AddSingleton<IEventDispatcher, InProcessEventDispatcher>();
        return services;
    }
}
