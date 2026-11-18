namespace Pickory.ArchitectureTests;

// Quy tắc: chỉ `Pickory.Shared` được dùng ClosedXML. Hai hàm nhận dữ liệu thuần để test chứng minh được hàm báo vi phạm.
public static class ClosedXmlBoundaryValidator
{
    private const string ClosedXmlName = "ClosedXML";

    // Báo vi phạm khi assembly module tham chiếu assembly tên `ClosedXML`. Module dùng kiểu ClosedXML qua API sẽ rơi vào trường hợp này.
    public static IReadOnlyList<string> FindAssemblyViolations(string assemblyName, IEnumerable<string> referencedAssemblyNames)
    {
        // So khớp đúng tên để không báo nhầm assembly khác có tên chứa chữ `ClosedXML`, ví dụ `ClosedXML.Parser`.
        return referencedAssemblyNames
            .Where(referenced => string.Equals(referenced, ClosedXmlName, StringComparison.Ordinal))
            .Select(_ => $"Assembly '{assemblyName}' tham chiếu '{ClosedXmlName}'. Chỉ 'Pickory.Shared' được dùng gói này.")
            .ToList();
    }

    // Báo vi phạm khi file project của module nhắc tới ClosedXML. Lớp này bắt module khai báo gói nhưng chưa dùng.
    public static IReadOnlyList<string> FindProjectFileViolations(string projectFileName, string projectFileContent)
    {
        if (!projectFileContent.Contains(ClosedXmlName, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return [$"File project '{projectFileName}' nhắc tới '{ClosedXmlName}'. Chỉ 'Pickory.Shared' được khai báo gói này."];
    }
}
