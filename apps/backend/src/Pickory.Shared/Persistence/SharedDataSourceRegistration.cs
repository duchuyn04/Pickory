using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Pickory.Shared.Persistence;

// Đăng ký một `NpgsqlDataSource` duy nhất cho cả API. Mỗi module bổ sung ánh xạ enum của mình trước khi data source được dựng.
public static class SharedDataSourceRegistration
{
    // Giữ phần cấu hình của một module. Dịch vụ này được resolve cùng lúc để ghép mọi ánh xạ vào cùng một data source.
    private sealed class DataSourceConfigurator(Action<NpgsqlDataSourceBuilder> configure)
    {
        public void Configure(NpgsqlDataSourceBuilder builder) => configure(builder);
    }

    // Module gọi hàm này trong `AddXModule`. Lời gọi đầu tiên chọn chuỗi kết nối, các lời gọi sau chỉ thêm ánh xạ enum.
    public static IServiceCollection AddSharedDataSource(
        this IServiceCollection services, string connectionString, Action<NpgsqlDataSourceBuilder> configure)
    {
        services.AddSingleton(new DataSourceConfigurator(configure));
        // Mỗi data source có pool kết nối riêng. Chỉ một data source nên `DATABASE_POOL_MAX` áp cho cả tiến trình.
        // Đăng ký bằng factory để container giải phóng data source khi ứng dụng tắt.
        services.TryAddSingleton(provider =>
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            foreach (var configurator in provider.GetServices<DataSourceConfigurator>())
            {
                configurator.Configure(builder);
            }

            return builder.Build();
        });
        return services;
    }
}
