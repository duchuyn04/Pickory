using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pickory.Shared.Configuration;

namespace Pickory.Modules.Shipping.Ghn;

// Chọn client GHN theo cấu hình: bản thật khi có `GHN_TOKEN`, bản giả ở dev và test. Production thiếu token đã dừng ở bước kiểm cấu hình.
internal static class GhnRegistration
{
    public const string HttpClientName = "shipping-ghn";

    public static IServiceCollection AddGhnClient(this IServiceCollection services, AppConfig config)
    {
        // Thiếu token thì dùng client giả không gọi mạng, để dev và test chạy được mà không cần tài khoản GHN.
        if (string.IsNullOrWhiteSpace(config.GhnToken))
        {
            services.AddSingleton<FakeGhnClient>();
            services.AddSingleton<IGhnClient>(provider => provider.GetRequiredService<FakeGhnClient>());
            return services;
        }

        // Adapter tự đặt timeout 5 giây cho từng lần gọi, nên `HttpClient` không đặt giới hạn riêng. Che giá trị mọi header trong log của `HttpClient` để `Token` không hiện ở mức Trace.
        services.AddHttpClient(HttpClientName, client =>
            {
                client.BaseAddress = new Uri(config.GhnApiBase);
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .RedactLoggedHeaders(_ => true);

        services.AddTransient<IGhnClient>(provider => new GhnHttpClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            config.GhnToken,
            config.GhnShopId,
            provider.GetRequiredService<ILogger<GhnHttpClient>>()));
        return services;
    }
}
