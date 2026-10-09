using System.Text.Json.Nodes;

namespace Pickory.UnitTests.TestDoubles;

// Đọc bộ dữ liệu mẫu dùng chung trong `spec/fixtures`. Frontend cũng chạy cùng các file này.
public static class SharedFixtures
{
    public static JsonNode Read(string relativePath)
    {
        // Đi ngược từ thư mục build lên gốc repo. Gốc repo là thư mục có `spec/fixtures`.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "spec", "fixtures", relativePath);
            if (File.Exists(path))
            {
                return JsonNode.Parse(File.ReadAllText(path))!;
            }
        }

        throw new FileNotFoundException($"Không tìm thấy bộ dữ liệu mẫu `spec/fixtures/{relativePath}`.");
    }
}
