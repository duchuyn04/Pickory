using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Sdk;

namespace Pickory.IntegrationTests.Infrastructure;

// Đọc fixture dùng chung trong `spec/fixtures`. Cùng cách tìm thư mục với `SharedFixtures` của `Pickory.UnitTests`.
public static class FixtureFiles
{
    public static JsonNode Read(string relativePath)
    {
        // Đi ngược từ thư mục build lên gốc repo. Gốc repo là thư mục có `spec/fixtures`.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "spec", "fixtures", relativePath);
            if (File.Exists(path))
            {
                return ParseText(relativePath, File.ReadAllText(path));
            }
        }

        throw new FileNotFoundException($"Không tìm thấy fixture `spec/fixtures/{relativePath}`.");
    }

    public static JsonObject FindExample(string fixtureFile, string exampleName)
    {
        return FindExampleIn(fixtureFile, Read(fixtureFile), exampleName);
    }

    internal static JsonNode ParseText(string fixtureFile, string text)
    {
        var context = new FixtureContext(fixtureFile, null);

        // File fixture hỏng không phải lỗi của route. Báo "Fixture sai" thay vì để ngoại lệ thô của bộ đọc JSON lọt ra.
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException exception)
        {
            throw context.Fault("nội dung file", $"không phải JSON hợp lệ ({exception.Message})");
        }

        // `JsonNode.Parse` trả `null` khi file chỉ chứa `null`. Chặn ở đây để các bước sau không gặp `NullReferenceException`.
        return root ?? throw context.Fault("nội dung file", "file chỉ chứa null");
    }

    internal static JsonObject FindExampleIn(string fixtureFile, JsonNode root, string exampleName)
    {
        var context = new FixtureContext(fixtureFile, null);

        // Gốc file phải là object có khóa `examples`. Gốc là mảng sẽ làm đọc khóa ném `InvalidOperationException`.
        var rootObject = context.RequireObject(root, "gốc của file");
        var examples = context.RequireArray(rootObject["examples"], "khóa `examples`");

        // So tên theo thứ tự ký tự, không phụ thuộc culture của máy chạy test. Tên không phải chuỗi bị bỏ qua, không ném lỗi thô.
        var matches = examples
            .OfType<JsonObject>()
            .Where(candidate => string.Equals(ReadName(candidate), exampleName, StringComparison.Ordinal))
            .ToList();

        // Hai ví dụ trùng tên làm test so nhầm ví dụ mà không biết. Báo lỗi fixture thay vì lấy ví dụ đầu.
        if (matches.Count > 1)
        {
            throw context.Fault("khóa `examples`", $"có {matches.Count} ví dụ cùng tên \"{exampleName}\"");
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        // Liệt kê mọi tên có trong file. Người đọc test đỏ sửa lại tên mà không cần mở fixture.
        var availableNames = examples.OfType<JsonObject>().Select(candidate => $"\"{ReadName(candidate)}\"");
        throw new XunitException(
            $"Không tìm thấy ví dụ \"{exampleName}\" trong `{fixtureFile}`. Các ví dụ có trong file: {string.Join(", ", availableNames)}.");
    }

    private static string? ReadName(JsonObject example)
    {
        return example["name"] is JsonValue value && value.TryGetValue<string>(out var name) ? name : null;
    }
}
