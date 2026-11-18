using Mono.Cecil;
using Xunit;

namespace Pickory.ArchitectureTests;

// Chặn ClosedXML ngoài `Pickory.Shared` ở hai lớp: assembly đã biên dịch và file project.
public class ClosedXmlBoundaryTests
{
    [Fact]
    // Đọc assembly bằng Cecil để không nạp assembly vào AppDomain, nên không cần bắt lỗi nạp. Lỗi đọc file phải làm test đỏ.
    public void MoiAssemblyModule_KhongThamChieuClosedXml()
    {
        var moduleDllPaths = Directory.GetFiles(AppContext.BaseDirectory, "Pickory.Modules.*.dll");
        // Thư mục rỗng làm test xanh giả. Khẳng định có cả assembly module và assembly Contracts.
        Assert.Contains(moduleDllPaths, path => Path.GetFileName(path) == "Pickory.Modules.Commerce.dll");
        Assert.Contains(moduleDllPaths, path => Path.GetFileName(path) == "Pickory.Modules.Commerce.Contracts.dll");

        var violations = new List<string>();
        foreach (var dllPath in moduleDllPaths)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(dllPath);
            var referencedNames = assembly.MainModule.AssemblyReferences.Select(reference => reference.Name);
            violations.AddRange(ClosedXmlBoundaryValidator.FindAssemblyViolations(assembly.Name.Name, referencedNames));
        }

        Assert.True(violations.Count == 0, string.Join("; ", violations));
    }

    [Fact]
    // Quét mọi file project của module, kể cả Contracts, vì module có thể khai báo gói mà chưa dùng tới.
    public void MoiFileProjectModule_KhongNhacToiClosedXml()
    {
        var projectFiles = Directory.GetFiles(FindBackendSourceDirectory(), "Pickory.Modules.*.csproj", SearchOption.AllDirectories);
        // Đường dẫn sai làm test xanh giả. Khẳng định tìm thấy cả module và Contracts.
        Assert.Contains(projectFiles, path => Path.GetFileName(path) == "Pickory.Modules.Commerce.csproj");
        Assert.Contains(projectFiles, path => Path.GetFileName(path) == "Pickory.Modules.Commerce.Contracts.csproj");

        var violations = new List<string>();
        foreach (var projectFile in projectFiles)
        {
            violations.AddRange(ClosedXmlBoundaryValidator.FindProjectFileViolations(
                Path.GetFileName(projectFile),
                File.ReadAllText(projectFile)));
        }

        Assert.True(violations.Count == 0, string.Join("; ", violations));
    }

    [Fact]
    // Hàm kiểm assembly phải báo vi phạm khi danh sách tham chiếu có `ClosedXML`.
    public void HamKiemTraAssembly_BaoViPham_KhiCoThamChieuClosedXml()
    {
        var violations = ClosedXmlBoundaryValidator.FindAssemblyViolations(
            "Pickory.Modules.Commerce",
            ["Pickory.Shared", "ClosedXML", "System.Runtime"]);

        Assert.Single(violations);
        Assert.Contains("Pickory.Modules.Commerce", violations[0]);
    }

    [Fact]
    // Assembly phụ như `ClosedXML.Parser` không phải là chính ClosedXML, nên không bị báo nhầm.
    public void HamKiemTraAssembly_KhongBaoViPham_KhiKhongCoClosedXml()
    {
        var violations = ClosedXmlBoundaryValidator.FindAssemblyViolations(
            "Pickory.Modules.Commerce",
            ["Pickory.Shared", "ClosedXML.Parser", "System.Runtime"]);

        Assert.Empty(violations);
    }

    [Fact]
    // Hàm kiểm file project phải báo vi phạm khi nội dung có khai báo gói ClosedXML.
    public void HamKiemTraFileProject_BaoViPham_KhiCoClosedXml()
    {
        const string content = "<Project><ItemGroup><PackageReference Include=\"ClosedXML\" /></ItemGroup></Project>";

        var violations = ClosedXmlBoundaryValidator.FindProjectFileViolations("Pickory.Modules.Commerce.csproj", content);

        Assert.Single(violations);
        Assert.Contains("Pickory.Modules.Commerce.csproj", violations[0]);
    }

    [Fact]
    public void HamKiemTraFileProject_KhongBaoViPham_KhiKhongCoClosedXml()
    {
        const string content = "<Project><ItemGroup><PackageReference Include=\"FluentValidation\" /></ItemGroup></Project>";

        Assert.Empty(ClosedXmlBoundaryValidator.FindProjectFileViolations("Pickory.Modules.Commerce.csproj", content));
    }

    // Đi ngược từ thư mục chạy test tới `apps/backend/src`. Cách này không phụ thuộc thư mục làm việc của lệnh `dotnet test`.
    private static string FindBackendSourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, "Pickory.Shared")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Không tìm thấy thư mục `src` của backend từ thư mục chạy test.");
    }
}
