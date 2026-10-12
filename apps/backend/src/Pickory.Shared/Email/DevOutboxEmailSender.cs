using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Pickory.Shared.Email;

// Dự án chưa nối dịch vụ gửi thư thật. Bộ gửi này ghi thư ra file để người dev mở file lấy link.
public sealed partial class DevOutboxEmailSender(string outboxDirectory, TimeProvider timeProvider, ILogger<DevOutboxEmailSender> logger)
    : IEmailSender
{
    private static readonly JsonSerializerOptions FileJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            // Lần gửi đầu tiên thư mục chưa tồn tại.
            Directory.CreateDirectory(outboxDirectory);
            // Tên file bắt đầu bằng thời gian để xếp theo thứ tự gửi. Ký tự lạ thành gạch dưới để hợp lệ trên mọi hệ điều hành.
            var fileName = $"{timeProvider.GetUtcNow().ToUnixTimeMilliseconds()}-{UnsafeFileNameCharacters().Replace(message.To, "_")}.json";
            var filePath = Path.Combine(outboxDirectory, fileName);
            await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(message, FileJsonOptions), cancellationToken);
            logger.LogInformation("Email lưu vào outbox dev {FilePath} cho {Subject}.", filePath, message.Subject);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Lỗi ghi file dev không được làm hỏng đăng ký hoặc quên mật khẩu.
            logger.LogError(exception, "Ghi email vào outbox dev thất bại.");
        }
    }

    [GeneratedRegex("[^a-zA-Z0-9]")]
    private static partial Regex UnsafeFileNameCharacters();
}
