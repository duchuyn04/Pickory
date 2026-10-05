using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.Identity.Emails;
using Pickory.Modules.Identity.GoogleOAuth;
using Pickory.Shared.Events;

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
        // `ConfigureTestServices` chạy sau đăng ký của app. Bản giả thay được dịch vụ thật đã đăng ký.
        builder.ConfigureTestServices(services =>
        {
            // Test đọc thư trong bộ nhớ thay vì ghi file vào outbox dev.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<InMemoryEmailSender>();
            // Trỏ `IEmailSender` về cùng một instance. Test đọc đúng hộp thư mà API đã ghi.
            services.AddSingleton<IEmailSender>(provider => provider.GetRequiredService<InMemoryEmailSender>());

            // Test không gọi Google thật. Bản giả trả thông tin người dùng do test đặt.
            services.RemoveAll<IGoogleOAuthClient>();
            services.AddSingleton<FakeGoogleOAuthClient>();
            // Trỏ `IGoogleOAuthClient` về cùng một instance. Thông tin test đặt là thông tin API nhận.
            services.AddSingleton<IGoogleOAuthClient>(provider => provider.GetRequiredService<FakeGoogleOAuthClient>());

            // Ghi lại sự kiện của identity để test kiểm tra số lần phát.
            services.AddSingleton<RecordedDomainEvents>();
            services.AddSingleton<IDomainEventHandler<UserLinkedEvent>, RecordingDomainEventHandler<UserLinkedEvent>>();
            services.AddSingleton<IDomainEventHandler<UserDeletedEvent>, RecordingDomainEventHandler<UserDeletedEvent>>();
        });
    }

    // Cung cấp hộp thư cho test. Test lấy token xác minh hoặc đặt lại mật khẩu từ thư.
    public InMemoryEmailSender EmailSender => Services.GetRequiredService<InMemoryEmailSender>();

    // Cung cấp bản giả Google cho test. Test đặt thông tin người dùng trước khi gọi callback.
    public FakeGoogleOAuthClient GoogleClient => Services.GetRequiredService<FakeGoogleOAuthClient>();

    // Cung cấp danh sách sự kiện cho test. Test kiểm tra sự kiện phát đúng số lần.
    public RecordedDomainEvents DomainEvents => Services.GetRequiredService<RecordedDomainEvents>();
}
