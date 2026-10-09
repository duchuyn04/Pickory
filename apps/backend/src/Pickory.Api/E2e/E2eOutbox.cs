using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pickory.Api.E2e;

// Thư mục outbox riêng của suite E2E. Lệnh chỉ xóa thư của tài khoản E2E và giữ thư dev của người phát triển.
public static partial class E2eOutbox
{
    public static void ClearOwnedMessages(string outboxDirectory)
    {
        // Chỉ dọn `.dev-outbox/e2e-identity`. Cấu hình sai không làm lệnh xóa file ở thư mục khác.
        var directory = new DirectoryInfo(Path.GetFullPath(outboxDirectory));
        if (directory.Name != "e2e-identity" || directory.Parent?.Name != ".dev-outbox")
        {
            throw new InvalidOperationException("Từ chối dọn outbox E2E nằm ngoài thư mục được phép.");
        }

        // Lần chạy đầu tiên chưa có thư mục. Bộ gửi thư cũng cần thư mục này tồn tại.
        directory.Create();
        // Thư mục là liên kết tượng trưng có thể trỏ ra ngoài cây dự án, nên lệnh từ chối dọn.
        if (directory.LinkTarget is not null)
        {
            throw new InvalidOperationException("Từ chối dọn outbox E2E nằm ngoài thư mục được phép.");
        }

        foreach (var file in directory.EnumerateFiles("*.json"))
        {
            // Bỏ qua liên kết tượng trưng. Lệnh không xóa file nằm ngoài outbox.
            if (file.LinkTarget is null && IsOwnedMessage(file.FullName))
            {
                file.Delete();
            }
        }
    }

    private static bool IsOwnedMessage(string filePath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(filePath));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("to", out var recipient)
                && recipient.ValueKind == JsonValueKind.String
                && SuiteRecipientPattern().IsMatch(recipient.GetString()!);
        }
        catch (JsonException)
        {
            // File không đọc được không phải thư của suite. Lệnh giữ nguyên file đó.
            return false;
        }
    }

    // Người nhận của suite có dạng cố định trong `e2e/environment.ts`.
    [GeneratedRegex(@"^e2e-identity-(admin|customer|sales)@pickory\.local$")]
    private static partial Regex SuiteRecipientPattern();
}
