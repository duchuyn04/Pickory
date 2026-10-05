using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Pickory.IntegrationTests;

// Nhà máy tạo ứng dụng web kiểm thử nạp cấu hình DB test từ môi trường hoặc tệp .env.
public class PickoryWebApplicationFactory : WebApplicationFactory<Program>
{
    // Lấy chuỗi kết nối cơ sở dữ liệu kiểm thử từ biến môi trường hoặc tệp .env gốc repo.
    public static string ResolveTestDatabaseUrl()
    {
        var envValue = Environment.GetEnvironmentVariable("TEST_DATABASE_URL");
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue.Trim();
        }

        // Tìm tệp .env tại thư mục gốc của repository.
        var repoRoot = FindRepoRootDirectory();
        if (repoRoot != null)
        {
            var envFilePath = Path.Combine(repoRoot, ".env");
            if (File.Exists(envFilePath))
            {
                foreach (var line in File.ReadAllLines(envFilePath))
                {
                    var trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("#") || !trimmedLine.Contains('='))
                    {
                        continue;
                    }

                    var segments = trimmedLine.Split('=', 2);
                    if (segments[0].Trim() == "TEST_DATABASE_URL")
                    {
                        var urlValue = segments[1].Trim();
                        if (!string.IsNullOrWhiteSpace(urlValue))
                        {
                            return urlValue;
                        }
                    }
                }
            }
        }

        // Báo lỗi rõ ràng khi không tìm thấy thông tin cơ sở dữ liệu kiểm thử.
        throw new InvalidOperationException("Thiếu biến môi trường TEST_DATABASE_URL bắt buộc cho kiểm thử tích hợp.");
    }

    private static string? FindRepoRootDirectory()
    {
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);

        // Duyệt ngược lên trên cây thư mục để tìm điểm mốc của repository.
        while (currentDir != null)
        {
            if (File.Exists(Path.Combine(currentDir.FullName, "package.json")))
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        return null;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var testDatabaseUrl = ResolveTestDatabaseUrl();

        // Ghi đè cấu hình cơ sở dữ liệu trên WebHost trước khi ứng dụng khởi chạy.
        builder.UseSetting("DATABASE_URL", testDatabaseUrl);

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            // Bổ sung cấu hình bộ nhớ với thông số của cơ sở dữ liệu kiểm thử.
            var testConfig = new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = testDatabaseUrl,
            };
            configBuilder.AddInMemoryCollection(testConfig);
        });

        builder.ConfigureServices(services =>
        {
            // Đăng ký bộ lọc khởi động gắn các route phục vụ kiểm thử tích hợp.
            services.AddSingleton<IStartupFilter, TestEndpointsStartupFilter>();
        });
    }
}
