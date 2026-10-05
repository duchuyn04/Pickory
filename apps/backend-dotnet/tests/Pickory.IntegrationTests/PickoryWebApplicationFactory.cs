using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pickory.Shared.Configuration;

namespace Pickory.IntegrationTests;

// Nhà máy tạo ứng dụng web kiểm thử với cấu hình cơ sở dữ liệu test riêng biệt.
public class PickoryWebApplicationFactory : WebApplicationFactory<Program>
{
    // Chuỗi kết nối cơ sở dữ liệu PostgreSQL dành riêng cho môi trường kiểm thử.
    public const string TestDatabaseUrl = "postgresql://pickory:pickory@localhost:5432/pickory_test";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Thiết lập cấu hình trực tiếp trên WebHost để ghi đè trước khi ứng dụng khởi chạy.
        builder.UseSetting("DATABASE_URL", TestDatabaseUrl);

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            // Bổ sung cấu hình bộ nhớ với thông số của cơ sở dữ liệu kiểm thử.
            var testConfig = new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = TestDatabaseUrl,
                ["PORT"] = "3000",
                ["WEB_ORIGIN"] = "http://localhost:5173",
                ["API_BASE_URL"] = "http://localhost:3000",
                ["TRUST_PROXY"] = "false",
            };
            configBuilder.AddInMemoryCollection(testConfig);
        });

        builder.ConfigureTestServices(services =>
        {
            // Nạp lại cấu hình đã được ghi đè để bảo đảm tính nhất quán trong container.
            var serviceProvider = services.BuildServiceProvider();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var config = AppConfig.Load(configuration);
            services.AddSingleton(config);
        });
    }
}
