using Xunit.Sdk;

namespace Pickory.IntegrationTests.Infrastructure;

// Gom mọi chỗ không khớp của một lần so. Helper báo một lần, không dừng ở lỗi đầu tiên.
internal sealed class FixtureMismatches
{
    private readonly List<string> lines = [];

    public void Add(string location, string problem)
    {
        lines.Add($"- {location}: {problem}");
    }

    public void ThrowIfAny(string title)
    {
        // Không có lỗi nào thì không ném gì. Test của người dùng helper chạy tiếp.
        if (lines.Count == 0)
        {
            return;
        }

        // Dùng `XunitException` để runner hiện thông báo tiếng Việt này làm lý do test đỏ.
        throw new XunitException($"{title}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");
    }
}
