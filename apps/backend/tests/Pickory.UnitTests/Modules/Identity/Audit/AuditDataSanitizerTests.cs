using System.Text.Json.Nodes;
using Pickory.Modules.Identity.Audit;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Audit;

// Port từ test ID-04 của bản Node. Nhật ký thao tác không bao giờ được lưu bí mật.
public class AuditDataSanitizerTests
{
    [Fact]
    // Trường có tên chứa bí mật bị giấu giá trị. Tên trường vẫn còn để người xem biết trường đó từng đổi.
    public void Sanitize_RedactsSensitiveFieldsAtEveryDepth()
    {
        var input = new Dictionary<string, object?>
        {
            ["name"] = "Nguyen Van A",
            ["email"] = "a@example.com",
            ["password"] = "plain_password",
            ["passwordHash"] = "$argon2id$something",
            ["token"] = "session_token_xyz",
            ["tokenHash"] = "sha256_hash",
            ["apiKey"] = "sk-12345",
            ["secret"] = "my_secret",
            ["cardNumber"] = "1234567812345678",
            ["nested"] = new Dictionary<string, object?>
            {
                ["normalField"] = 123,
                ["api_key"] = "nested_key",
                ["password_hash"] = "nested_hash",
            },
            ["roles"] = new[] { "sales", "support" },
        };

        var sanitized = AuditDataSanitizer.Sanitize(input)!.AsObject();

        // Trường thường giữ nguyên giá trị.
        Assert.Equal("Nguyen Van A", sanitized["name"]!.GetValue<string>());
        Assert.Equal("a@example.com", sanitized["email"]!.GetValue<string>());
        Assert.Equal(new[] { "sales", "support" }, sanitized["roles"]!.AsArray().Select(role => role!.GetValue<string>()));
        // Mọi trường nhạy cảm ở cấp ngoài bị giấu.
        foreach (var sensitiveKey in new[] { "password", "passwordHash", "token", "tokenHash", "apiKey", "secret", "cardNumber" })
        {
            Assert.Equal("[REDACTED]", sanitized[sensitiveKey]!.GetValue<string>());
        }

        // Gạch dưới trong tên trường không giúp bí mật lọt qua ở cấp lồng nhau.
        var nested = sanitized["nested"]!.AsObject();
        Assert.Equal(123, nested["normalField"]!.GetValue<int>());
        Assert.Equal("[REDACTED]", nested["api_key"]!.GetValue<string>());
        Assert.Equal("[REDACTED]", nested["password_hash"]!.GetValue<string>());
    }

    [Fact]
    // Bí mật nằm trong phần tử của mảng cũng bị giấu.
    public void Sanitize_RedactsInsideArrays()
    {
        var input = new[] { new Dictionary<string, object?> { ["secret"] = "x", ["id"] = "1" } };

        var sanitized = AuditDataSanitizer.Sanitize(input)!.AsArray();

        Assert.Equal("[REDACTED]", sanitized[0]!["secret"]!.GetValue<string>());
        Assert.Equal("1", sanitized[0]!["id"]!.GetValue<string>());
    }

    [Fact]
    // Thời gian thành chuỗi ISO có `Z`. Cột `before` và `after` là JSON, giống `toISOString()` của bản Node.
    public void Sanitize_WritesDatesAsIsoUtcStrings()
    {
        var input = new Dictionary<string, object?> { ["lockedAt"] = new DateTime(2026, 10, 5, 7, 8, 9, 123, DateTimeKind.Utc) };

        var sanitized = AuditDataSanitizer.Sanitize(input)!.AsObject();

        Assert.Equal("2026-10-05T07:08:09.123Z", sanitized["lockedAt"]!.GetValue<string>());
    }

    [Fact]
    // Giá trị `null` giữ nguyên. Nhật ký phân biệt được "không có giá trị" với "có giá trị".
    public void Sanitize_KeepsNullValues()
    {
        var input = new Dictionary<string, object?> { ["lockedAt"] = null };

        var sanitized = AuditDataSanitizer.Sanitize(input)!.AsObject();

        Assert.True(sanitized.ContainsKey("lockedAt"));
        Assert.Null(sanitized["lockedAt"]);
    }
}
