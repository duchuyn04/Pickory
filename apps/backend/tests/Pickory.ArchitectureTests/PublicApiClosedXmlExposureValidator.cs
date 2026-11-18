using System.Reflection;

namespace Pickory.ArchitectureTests;

// Tìm kiểu public lộ ClosedXML ra ngoài. Module gọi API như vậy sẽ kéo assembly ClosedXML vào module.
public static class PublicApiClosedXmlExposureValidator
{
    private const string ClosedXmlAssemblyName = "ClosedXML";

    public static IReadOnlyList<string> FindViolations(IEnumerable<Type> publicTypes)
    {
        var violations = new List<string>();
        foreach (var type in publicTypes)
        {
            // Kiểu cha và interface là một phần của API. Module dùng kiểu này cũng dùng luôn kiểu cha.
            AddIfClosedXml(violations, type, "kiểu cha", type.BaseType);
            // Ràng buộc `where T : IXLCell` của kiểu generic cũng bắt module dùng ClosedXML.
            foreach (var typeParameter in type.IsGenericTypeDefinition ? type.GetGenericArguments() : [])
            {
                AddIfClosedXml(violations, type, $"tham số kiểu '{typeParameter.Name}'", typeParameter);
            }

            foreach (var implementedInterface in type.GetInterfaces())
            {
                AddIfClosedXml(violations, type, "interface", implementedInterface);
            }

            // Chỉ xét member module thấy được: public và protected. Member internal và private không lộ ra ngoài.
            const BindingFlags visibleMembers = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var member in type.GetMembers(visibleMembers).Where(IsVisibleOutsideAssembly))
            {
                foreach (var usedType in GetSignatureTypes(member))
                {
                    AddIfClosedXml(violations, type, $"member '{member.Name}'", usedType);
                }
            }
        }

        return violations;
    }

    private static bool IsVisibleOutsideAssembly(MemberInfo member)
    {
        return member switch
        {
            // Thuộc tính và sự kiện không cần nhánh riêng. Hàm truy cập `get_`, `set_`, `add_`, `remove_` là method và đã được duyệt.
            MethodBase method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly,
            FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
            _ => false,
        };
    }

    private static IEnumerable<Type?> GetSignatureTypes(MemberInfo member)
    {
        switch (member)
        {
            case MethodInfo method:
                yield return method.ReturnType;
                // Ràng buộc của tham số kiểu trên hàm generic cũng là một phần chữ ký.
                if (method.IsGenericMethodDefinition)
                {
                    foreach (var typeParameter in method.GetGenericArguments())
                    {
                        yield return typeParameter;
                    }
                }

                foreach (var parameter in method.GetParameters())
                {
                    yield return parameter.ParameterType;
                }

                break;
            case ConstructorInfo constructor:
                foreach (var parameter in constructor.GetParameters())
                {
                    yield return parameter.ParameterType;
                }

                break;
            case FieldInfo field:
                yield return field.FieldType;
                break;
        }
    }

    private static void AddIfClosedXml(List<string> violations, Type owner, string where, Type? usedType)
    {
        foreach (var closedXmlType in ExpandType(usedType, []).Where(IsClosedXmlType))
        {
            violations.Add($"Kiểu public '{owner.FullName}' lộ '{closedXmlType.FullName}' ở {where}.");
        }
    }

    // Mở kiểu mảng, tham chiếu `ref` và tham số kiểu generic. `List<IXLCell>` cũng lộ ClosedXML dù kiểu ngoài là của .NET.
    // Tham số kiểu mở thêm ràng buộc của nó. Tập `visited` chặn vòng lặp vô hạn ở ràng buộc như `T : IComparable<T>`.
    private static IEnumerable<Type> ExpandType(Type? type, HashSet<Type> visited)
    {
        if (type is null || !visited.Add(type))
        {
            yield break;
        }

        if (type.HasElementType)
        {
            foreach (var inner in ExpandType(type.GetElementType(), visited))
            {
                yield return inner;
            }

            yield break;
        }

        yield return type;
        if (type.IsGenericParameter)
        {
            foreach (var constraint in type.GetGenericParameterConstraints().SelectMany(c => ExpandType(c, visited)))
            {
                yield return constraint;
            }
        }
        else if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments().SelectMany(a => ExpandType(a, visited)))
            {
                yield return argument;
            }
        }
    }

    private static bool IsClosedXmlType(Type type)
    {
        return string.Equals(type.Assembly.GetName().Name, ClosedXmlAssemblyName, StringComparison.Ordinal);
    }
}
