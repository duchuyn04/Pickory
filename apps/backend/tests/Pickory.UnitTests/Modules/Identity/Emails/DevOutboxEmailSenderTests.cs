using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pickory.Modules.Identity.Emails;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Emails;

// Kiểm tra outbox dev. Người dev mở file thư để lấy link xác thực hoặc link đặt lại mật khẩu.
public sealed class DevOutboxEmailSenderTests : IDisposable
{
    private readonly string _outboxDirectory = Path.Combine(Path.GetTempPath(), $"pickory-outbox-{Guid.NewGuid():N}");
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    // Thư được ghi thành file JSON trong thư mục outbox. Thư mục chưa tồn tại cũng không làm hỏng lần gửi đầu tiên.
    public async Task SendAsync_WritesMessageAsJsonFile()
    {
        var sender = new DevOutboxEmailSender(_outboxDirectory, _time, NullLogger<DevOutboxEmailSender>.Instance);
        var message = new EmailMessage(
            "Khach+1@pickory.local",
            "Đặt lại mật khẩu Pickory",
            "Link đặt lại mật khẩu",
            new Dictionary<string, string> { ["token"] = "token-1", ["purpose"] = "reset" });

        await sender.SendAsync(message, CancellationToken.None);

        // Tên file bắt đầu bằng thời điểm gửi. Ký tự lạ trong email thành gạch dưới để hợp lệ trên mọi hệ điều hành.
        var filePath = Assert.Single(Directory.GetFiles(_outboxDirectory));
        Assert.Equal($"{_time.GetUtcNow().ToUnixTimeMilliseconds()}-Khach_1_pickory_local.json", Path.GetFileName(filePath));

        // Khóa JSON dùng camelCase giống file của bản Node. Script E2E đọc `to` và `metadata.token` từ file này.
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
        Assert.Equal("Khach+1@pickory.local", document.RootElement.GetProperty("to").GetString());
        Assert.Equal("Đặt lại mật khẩu Pickory", document.RootElement.GetProperty("subject").GetString());
        Assert.Equal("token-1", document.RootElement.GetProperty("metadata").GetProperty("token").GetString());
    }

    [Fact]
    // Lỗi ghi file chỉ bị ghi log. Đăng ký hoặc quên mật khẩu không thất bại vì outbox dev.
    public async Task SendAsync_SwallowsWriteFailure()
    {
        // Một file thường chiếm đúng đường dẫn thư mục outbox, nên hệ điều hành không tạo được thư mục.
        await File.WriteAllTextAsync(_outboxDirectory, "khong-phai-thu-muc");
        var sender = new DevOutboxEmailSender(_outboxDirectory, _time, NullLogger<DevOutboxEmailSender>.Instance);

        await sender.SendAsync(new EmailMessage("a@pickory.local", "Tiêu đề", "Nội dung", new Dictionary<string, string>()), CancellationToken.None);

        // Đường dẫn vẫn là file cũ. Hàm gửi kết thúc mà không ném lỗi.
        Assert.True(File.Exists(_outboxDirectory));
    }

    // Xóa thư mục tạm sau mỗi test. Thư của test không còn lại trên máy dev.
    public void Dispose()
    {
        if (Directory.Exists(_outboxDirectory))
        {
            Directory.Delete(_outboxDirectory, recursive: true);
        }

        if (File.Exists(_outboxDirectory))
        {
            File.Delete(_outboxDirectory);
        }
    }
}
