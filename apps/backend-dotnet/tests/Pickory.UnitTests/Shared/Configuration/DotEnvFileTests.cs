using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests.Shared.Configuration;

// `pnpm dev` cần đọc `.env` ở gốc repo, giống `shared/load-env.ts` của bản Node.
public sealed class DotEnvFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pickory-dotenv-{Guid.NewGuid():N}");

    [Fact]
    // Đọc cặp khóa và giá trị. Dòng trống, dòng comment và dòng không có dấu bằng bị bỏ qua.
    public void Parse_ReadsKeyValuePairsAndSkipsComments()
    {
        string[] lines =
        [
            "# Sao chép thành .env",
            "",
            "PORT=3000",
            "  WEB_ORIGIN = http://localhost:5173  ",
            "khong-co-dau-bang",
            "LLM_API_KEY=",
        ];

        var values = DotEnvFile.Parse(lines);

        Assert.Equal("3000", values["PORT"]);
        Assert.Equal("http://localhost:5173", values["WEB_ORIGIN"]);
        Assert.Equal(string.Empty, values["LLM_API_KEY"]);
        Assert.Equal(3, values.Count);
    }

    [Fact]
    // Giá trị trong dấu nháy giữ nguyên nội dung bên trong. Dấu bằng trong URL không cắt giá trị.
    public void Parse_StripsMatchingQuotesAndKeepsEqualsInValue()
    {
        string[] lines =
        [
            "DATABASE_URL=\"postgresql://pickory:pickory@localhost:5432/pickory?sslmode=disable\"",
            "ADMIN_NAME='Quản trị viên'",
        ];

        var values = DotEnvFile.Parse(lines);

        Assert.Equal("postgresql://pickory:pickory@localhost:5432/pickory?sslmode=disable", values["DATABASE_URL"]);
        Assert.Equal("Quản trị viên", values["ADMIN_NAME"]);
    }

    [Fact]
    // Tìm `.env` ở thư mục gốc repo từ thư mục build sâu bên dưới. Gốc repo có file `pnpm-workspace.yaml`.
    public void FindRepoEnvFile_WalksUpToWorkspaceRoot()
    {
        var buildDirectory = Path.Combine(_root, "apps", "backend-dotnet", "src", "Pickory.Api", "bin", "Debug");
        Directory.CreateDirectory(buildDirectory);
        File.WriteAllText(Path.Combine(_root, "pnpm-workspace.yaml"), "packages: []");
        File.WriteAllText(Path.Combine(_root, ".env"), "PORT=3000");

        Assert.Equal(Path.Combine(_root, ".env"), DotEnvFile.FindRepoEnvFile(buildDirectory));
    }

    [Fact]
    // Production không có `.env`. Hàm trả null và API dùng biến môi trường có sẵn.
    public void FindRepoEnvFile_ReturnsNullWithoutEnvFile()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "pnpm-workspace.yaml"), "packages: []");

        Assert.Null(DotEnvFile.FindRepoEnvFile(_root));
    }

    public void Dispose()
    {
        // Xóa thư mục tạm sau mỗi test. File thử không còn lại trên máy.
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
