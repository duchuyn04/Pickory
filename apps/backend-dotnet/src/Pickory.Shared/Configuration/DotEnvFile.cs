using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;

namespace Pickory.Shared.Configuration;

// Đọc file `.env` ở gốc repo cho môi trường dev, giống `shared/load-env.ts` của bản Node.
public static class DotEnvFile
{
    // Gốc repo là thư mục có file này. Người dev sao chép file này thành `.env` ở cùng thư mục.
    private const string RepoRootMarkerFile = ".env.example";

    // Thêm giá trị của `.env` với độ ưu tiên thấp nhất. Biến môi trường có sẵn trong CI, production hoặc test luôn thắng.
    public static void AddAsLowestPriority(IConfigurationBuilder configuration, string startDirectory)
    {
        var envFile = FindRepoEnvFile(startDirectory);
        if (envFile is null)
        {
            return;
        }

        var values = Parse(File.ReadAllLines(envFile));
        configuration.Sources.Insert(0, new MemoryConfigurationSource { InitialData = values! });
    }

    // Trả null khi không tìm thấy gốc repo hoặc gốc repo không có `.env`. Production chạy với biến môi trường có sẵn.
    public static string? FindRepoEnvFile(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, RepoRootMarkerFile)))
            {
                continue;
            }

            var envFile = Path.Combine(directory.FullName, ".env");
            return File.Exists(envFile) ? envFile : null;
        }

        return null;
    }

    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            // Dòng trống và dòng comment không chứa cấu hình.
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            // Cắt ở dấu bằng đầu tiên. URL trong giá trị có thể chứa dấu bằng ở phần query.
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            values[key] = StripMatchingQuotes(value);
        }

        return values;
    }

    // Dấu nháy bao quanh chỉ dùng để viết giá trị có khoảng trắng. Giá trị thật không chứa dấu nháy đó.
    private static string StripMatchingQuotes(string value)
    {
        var isQuoted = value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''));
        return isQuoted ? value[1..^1] : value;
    }
}
