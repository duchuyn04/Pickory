using System.Reflection;

namespace Pickory.ArchitectureTests;

// Bộ kiểm tra kiến trúc bảo đảm interface hợp đồng không để lộ kiểu nội bộ hay entity.
public static class InterfaceContractValidator
{
    // Duyệt đệ quy kiểu dữ liệu tham số và giá trị trả về của mọi phương thức trong interface.
    public static IReadOnlyList<string> ValidateInterfaceContracts(Type interfaceType, string allowedContractNamespace)
    {
        var violations = new List<string>();
        var visitedTypes = new HashSet<Type>();

        // Duyệt từng phương thức công khai được định nghĩa trên interface.
        foreach (var method in interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            InspectType(method.ReturnType, $"{interfaceType.Name}.{method.Name}() return", allowedContractNamespace, visitedTypes, violations);

            // Duyệt từng tham số của phương thức.
            foreach (var parameter in method.GetParameters())
            {
                InspectType(parameter.ParameterType, $"{interfaceType.Name}.{method.Name}({parameter.Name})", allowedContractNamespace, visitedTypes, violations);
            }
        }

        return violations;
    }

    private static void InspectType(
        Type type,
        string path,
        string allowedContractNamespace,
        HashSet<Type> visitedTypes,
        List<string> violations)
    {
        // Bỏ qua kiểu trả về void của phương thức.
        if (type == typeof(void))
        {
            return;
        }

        // Bóc tách kiểu gốc khi là mảng hoặc tham chiếu con trỏ.
        if (type.HasElementType)
        {
            InspectType(type.GetElementType()!, path, allowedContractNamespace, visitedTypes, violations);
            return;
        }

        // Kiểm tra đối số generic khi kiểu dữ liệu là dạng generic như Task hoặc List.
        if (type.IsGenericType)
        {
            var genericDefinition = type.GetGenericTypeDefinition();
            CheckNamespace(genericDefinition, path, allowedContractNamespace, violations);

            // Duyệt đệ quy từng đối số kiểu generic.
            foreach (var argument in type.GetGenericArguments())
            {
                InspectType(argument, $"{path}->{argument.Name}", allowedContractNamespace, visitedTypes, violations);
            }

            // Với kiểu generic thuộc hợp đồng, tiếp tục duyệt các thuộc tính bên trong.
            if (IsContractType(genericDefinition, allowedContractNamespace))
            {
                InspectProperties(type, path, allowedContractNamespace, visitedTypes, violations);
            }

            return;
        }

        // Kiểm tra namespace của kiểu dữ liệu thông thường.
        CheckNamespace(type, path, allowedContractNamespace, violations);

        // Duyệt đệ quy các thuộc tính của kiểu dữ liệu thuộc hợp đồng.
        if (IsContractType(type, allowedContractNamespace))
        {
            InspectProperties(type, path, allowedContractNamespace, visitedTypes, violations);
        }
    }

    private static void InspectProperties(
        Type type,
        string path,
        string allowedContractNamespace,
        HashSet<Type> visitedTypes,
        List<string> violations)
    {
        // Tránh vòng lặp vô hạn khi kiểu dữ liệu có thuộc tính tham chiếu vòng tròn.
        if (!visitedTypes.Add(type))
        {
            return;
        }

        // Duyệt mọi thuộc tính công khai của đối tượng hợp đồng.
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            InspectType(property.PropertyType, $"{path}.{property.Name}", allowedContractNamespace, visitedTypes, violations);
        }
    }

    private static void CheckNamespace(
        Type type,
        string path,
        string allowedContractNamespace,
        List<string> violations)
    {
        var ns = type.Namespace ?? string.Empty;

        // Chỉ cho phép kiểu dữ liệu thuộc namespace hợp đồng hoặc của hệ thống System.
        var isAllowed = ns == allowedContractNamespace
            || ns.StartsWith(allowedContractNamespace + ".", StringComparison.Ordinal)
            || ns == "System"
            || ns.StartsWith("System.", StringComparison.Ordinal);

        if (!isAllowed)
        {
            violations.Add($"{path}: Kiểu '{type.FullName}' thuộc namespace không được phép '{ns}'.");
        }
    }

    private static bool IsContractType(Type type, string allowedContractNamespace)
    {
        var ns = type.Namespace ?? string.Empty;
        return ns == allowedContractNamespace
            || ns.StartsWith(allowedContractNamespace + ".", StringComparison.Ordinal);
    }
}
