using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Pickory.IntegrationTests.Infrastructure;

public class PickoryWebApplicationFactory(IntegrationDatabaseFixture database) : WebApplicationFactory<Program>
{
    public static string ResolveTestDatabaseUrl()
    {
        // Không dự phòng bằng `DATABASE_URL` vì collection sẽ reset DB được chọn.
        return ResolveEnvironmentValue("TEST_DATABASE_URL")
            ?? throw new InvalidOperationException("Thiếu TEST_DATABASE_URL bắt buộc cho kiểm thử tích hợp.");
    }

    internal static string? ResolveEnvironmentValue(string name)
    {
        // Môi trường tiến trình có ưu tiên cao hơn cấu hình repo.
        var environmentValue = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue.Trim();
        }
        var repoRoot = FindRepoRootDirectory();
        var envPath = repoRoot is null ? null : Path.Combine(repoRoot, ".env");
        if (envPath is null || !File.Exists(envPath))
        {
            return null;
        }

        // Chỉ đọc khóa chính xác để URL phát triển không trở thành URL kiểm thử.
        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#') || !trimmed.Contains('='))
            {
                continue;
            }
            var parts = trimmed.Split('=', 2);
            if (parts[0].Trim() != name)
            {
                continue;
            }
            var value = parts[1].Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        return null;
    }

    internal static string? FindRepoRootDirectory()
    {
        // Điểm mốc repo cho phép tìm `.env` từ thư mục đầu ra kiểm thử.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "apps", "backend-dotnet")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        return null;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Collection phải hoàn thành migration trước khi bất kỳ nhà máy nào khởi động API.
        if (!database.Initialized)
        {
            throw new InvalidOperationException("The integration database collection must initialize before the web application factory starts.");
        }
        builder.UseSetting("DATABASE_URL", database.DatabaseUrl);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            // URL đã được kiểm tra thuộc fixture duy nhất của toàn collection.
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = database.DatabaseUrl
            });
        });
        builder.ConfigureServices(services =>
        {
            // Route kiểm thử không được đưa vào ứng dụng sản xuất.
            services.AddSingleton<IStartupFilter, TestEndpointsStartupFilter>();
        });
    }
}
