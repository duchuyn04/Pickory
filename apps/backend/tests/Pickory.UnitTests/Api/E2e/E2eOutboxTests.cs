using Pickory.Api.E2e;
using Xunit;

namespace Pickory.UnitTests.Api.E2e;

// Dọn outbox của suite E2E. Thư dev của người phát triển trong cùng thư mục không bị xóa.
public sealed class E2eOutboxTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pickory-e2e-{Guid.NewGuid():N}");

    [Fact]
    // Chỉ xóa file JSON có người nhận là tài khoản E2E. Thư khác và file không phải JSON được giữ.
    public void ClearOwnedMessages_DeletesOnlySuiteMessages()
    {
        var outbox = CreateOutboxDirectory();
        File.WriteAllText(Path.Combine(outbox, "1-admin.json"), """{"to":"e2e-identity-admin@pickory.local"}""");
        File.WriteAllText(Path.Combine(outbox, "2-sales.json"), """{"to":"e2e-identity-sales@pickory.local"}""");
        File.WriteAllText(Path.Combine(outbox, "3-dev.json"), """{"to":"dev@pickory.local"}""");
        File.WriteAllText(Path.Combine(outbox, "4-note.txt"), "e2e-identity-admin@pickory.local");
        File.WriteAllText(Path.Combine(outbox, "5-broken.json"), "khong-phai-json");

        E2eOutbox.ClearOwnedMessages(outbox);

        // File hỏng cũng được giữ. Lệnh không xóa file mà lệnh không đọc được.
        Assert.Equal(["3-dev.json", "4-note.txt", "5-broken.json"], Directory.GetFiles(outbox).Select(Path.GetFileName).Order());
    }

    [Fact]
    // Thư mục outbox chưa tồn tại được tạo mới. Lần chạy E2E đầu tiên không lỗi.
    public void ClearOwnedMessages_CreatesMissingDirectory()
    {
        var outbox = Path.Combine(_root, ".dev-outbox", "e2e-identity");

        E2eOutbox.ClearOwnedMessages(outbox);

        Assert.True(Directory.Exists(outbox));
    }

    [Theory]
    [InlineData("khac", "e2e-identity")]
    [InlineData(".dev-outbox", "khac")]
    // Lệnh chỉ dọn thư mục `.dev-outbox/e2e-identity`. Cấu hình sai không làm lệnh xóa file ở thư mục khác.
    public void ClearOwnedMessages_RejectsOtherDirectories(string parentName, string directoryName)
    {
        var directory = Path.Combine(_root, parentName, directoryName);

        var exception = Assert.Throws<InvalidOperationException>(() => E2eOutbox.ClearOwnedMessages(directory));

        Assert.Equal("Từ chối dọn outbox E2E nằm ngoài thư mục được phép.", exception.Message);
    }

    public void Dispose()
    {
        // Xóa thư mục tạm sau mỗi test. File thử không còn lại trên máy.
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreateOutboxDirectory()
    {
        var outbox = Path.Combine(_root, ".dev-outbox", "e2e-identity");
        Directory.CreateDirectory(outbox);
        return outbox;
    }
}
