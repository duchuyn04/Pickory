using System.Reflection;
using Mono.Cecil;
using NetArchTest.Rules;

namespace Pickory.ArchitectureTests;

// Kết quả kiểm tra tính hợp lệ về ranh giới kiến trúc của module.
public record ArchitectureValidationResult(bool IsSuccessful, IReadOnlyList<string> Violations);

// Bộ kiểm tra kiến trúc dùng chung áp dụng cho mọi module trong giải pháp.
public static class ModuleArchitectureValidator
{
    // Lấy danh sách toàn bộ assembly của các module thực thi (không bao gồm Contracts).
    public static IReadOnlyList<Assembly> GetModuleAssemblies()
    {
        // Quét và nạp toàn bộ assembly module trong thư mục thực thi để tránh nạp lười.
        var baseDir = AppContext.BaseDirectory;
        foreach (var dll in Directory.GetFiles(baseDir, "Pickory.Modules.*.dll"))
        {
            var fileName = Path.GetFileNameWithoutExtension(dll);
            if (!fileName.EndsWith(".Contracts", StringComparison.Ordinal))
            {
                try
                {
                    Assembly.LoadFrom(dll);
                }
                catch
                {
                    // Bỏ qua lỗi nạp để tiếp tục quét các tệp assembly khác.
                }
            }
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly =>
            {
                var name = assembly.GetName().Name;
                return name != null
                    && name.StartsWith("Pickory.Modules.", StringComparison.Ordinal)
                    && !name.EndsWith(".Contracts", StringComparison.Ordinal);
            })
            .Distinct()
            .ToList();
    }

    // Kiểm tra toàn diện ranh giới của một assembly module theo quy ước kiến trúc.
    public static ArchitectureValidationResult ValidateModule(Assembly moduleAssembly)
    {
        var violations = new List<string>();
        var currentModuleName = moduleAssembly.GetName().Name ?? string.Empty;

        // 1. Kiểm tra tham chiếu assembly: chỉ được tham chiếu Pickory.Shared và *.Contracts.
        foreach (var reference in moduleAssembly.GetReferencedAssemblies())
        {
            var refName = reference.Name ?? string.Empty;
            if (!refName.StartsWith("Pickory.", StringComparison.Ordinal))
            {
                continue;
            }

            if (refName == "Pickory.Shared" || refName == currentModuleName)
            {
                continue;
            }

            if (refName.StartsWith("Pickory.Modules.", StringComparison.Ordinal)
                && refName.EndsWith(".Contracts", StringComparison.Ordinal))
            {
                continue;
            }

            // Ghi nhận vi phạm khi tham chiếu assembly nội bộ không thuộc hợp đồng.
            violations.Add($"Assembly '{currentModuleName}' tham chiếu trực tiếp assembly nội bộ '{refName}'.");
        }

        // 2. Kiểm tra phụ thuộc namespace trong mã nguồn qua quy tắc NetArchTest.
        var types = Types.InAssembly(moduleAssembly);
        var rule = new NonContractModuleDependencyRule(currentModuleName);
        var ruleResult = types.Should().MeetCustomRule(rule).GetResult();

        if (!ruleResult.IsSuccessful && ruleResult.FailingTypeNames != null)
        {
            foreach (var failingType in ruleResult.FailingTypeNames)
            {
                // Ghi nhận vi phạm khi một kiểu dữ liệu sử dụng phần nội bộ của module khác.
                violations.Add($"Kiểu '{failingType}' trong module '{currentModuleName}' phụ thuộc vào nội bộ module khác.");
            }
        }

        return new ArchitectureValidationResult(violations.Count == 0, violations);
    }

    // Kiểm tra một kiểu dữ liệu đơn lẻ với tên module giả định qua cùng quy tắc.
    public static ArchitectureValidationResult ValidateType(Type type, string assumedModuleName)
    {
        var violations = new List<string>();
        var rule = new NonContractModuleDependencyRule(assumedModuleName);
        var ruleResult = Types.InAssembly(type.Assembly)
            .That()
            .HaveName(type.Name)
            .Should()
            .MeetCustomRule(rule)
            .GetResult();

        if (!ruleResult.IsSuccessful && ruleResult.FailingTypeNames != null)
        {
            foreach (var failingType in ruleResult.FailingTypeNames)
            {
                // Báo cáo vi phạm khi kiểu dữ liệu vi phạm ranh giới cách ly.
                violations.Add($"Kiểu '{failingType}' vi phạm ranh giới cách ly module.");
            }
        }

        return new ArchitectureValidationResult(violations.Count == 0, violations);
    }
}

// Quy tắc NetArchTest cấm phụ thuộc vào phần nội bộ của module khác.
public class NonContractModuleDependencyRule : ICustomRule
{
    private readonly string _currentModuleName;

    public NonContractModuleDependencyRule(string currentModuleName)
    {
        _currentModuleName = currentModuleName;
    }

    public bool MeetsRule(TypeDefinition type)
    {
        // Duyệt toàn bộ các kiểu dữ liệu được tham chiếu bởi type hiện tại.
        var referencedTypes = CollectReferencedTypes(type);
        foreach (var referencedType in referencedTypes)
        {
            if (IsForbiddenDependency(referencedType, _currentModuleName))
            {
                // Trả về false ngay khi phát hiện phụ thuộc trái phép vào module khác.
                return false;
            }
        }

        return true;
    }

    private static bool IsForbiddenDependency(string typeFullName, string currentModuleName)
    {
        // Bỏ qua các kiểu không thuộc tiền tố Pickory.Modules.
        if (!typeFullName.StartsWith("Pickory.Modules.", StringComparison.Ordinal))
        {
            return false;
        }

        // Được phép tham chiếu nội bộ trong cùng một module.
        if (typeFullName.StartsWith(currentModuleName + ".", StringComparison.Ordinal)
            || typeFullName == currentModuleName)
        {
            return false;
        }

        // Được phép tham chiếu các kiểu dữ liệu công khai thuộc Contracts của module khác.
        if (typeFullName.Contains(".Contracts.", StringComparison.Ordinal)
            || typeFullName.EndsWith(".Contracts", StringComparison.Ordinal))
        {
            return false;
        }

        // Mọi trường hợp tham chiếu khác đều bị cấm để bảo vệ tính độc lập.
        return true;
    }

    private static IEnumerable<string> CollectReferencedTypes(TypeDefinition type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        if (type.BaseType != null)
        {
            names.Add(type.BaseType.FullName);
        }

        foreach (var iface in type.Interfaces)
        {
            names.Add(iface.InterfaceType.FullName);
        }

        foreach (var field in type.Fields)
        {
            names.Add(field.FieldType.FullName);
        }

        foreach (var prop in type.Properties)
        {
            names.Add(prop.PropertyType.FullName);
        }

        foreach (var method in type.Methods)
        {
            names.Add(method.ReturnType.FullName);

            foreach (var parameter in method.Parameters)
            {
                names.Add(parameter.ParameterType.FullName);
            }

            if (method.HasBody)
            {
                foreach (var variable in method.Body.Variables)
                {
                    names.Add(variable.VariableType.FullName);
                }

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is TypeReference typeRef)
                    {
                        names.Add(typeRef.FullName);
                    }
                    else if (instruction.Operand is MethodReference methodRef && methodRef.DeclaringType != null)
                    {
                        names.Add(methodRef.DeclaringType.FullName);
                    }
                    else if (instruction.Operand is FieldReference fieldRef && fieldRef.DeclaringType != null)
                    {
                        names.Add(fieldRef.DeclaringType.FullName);
                    }
                }
            }
        }

        return names;
    }
}
