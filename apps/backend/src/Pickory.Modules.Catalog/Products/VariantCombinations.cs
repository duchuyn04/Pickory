using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Catalog.Products;

// Sinh mọi tổ hợp biến thể từ các trục tùy chọn, giống `domain/variants.ts` của bản Node.
public static class VariantCombinations
{
    public const int MaxAxes = 3;
    public const int MaxCombinations = 100;

    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Build(IReadOnlyList<ProductOption> options)
    {
        // Mỗi trục nhân số biến thể lên nhiều lần. Giới hạn 3 trục giữ số SKU quản lý được.
        if (options.Count > MaxAxes)
        {
            throw Invalid($"Sản phẩm chỉ được có tối đa {MaxAxes} trục tùy chọn.");
        }

        // Không có trục nào thì vẫn có một biến thể mặc định để gắn giá và tồn kho.
        if (options.Count == 0)
        {
            return [new Dictionary<string, string>()];
        }

        var axes = SanitizeAxes(options);
        var combinationCount = axes.Aggregate(1, (count, axis) => count * axis.Values.Count);
        if (combinationCount > MaxCombinations)
        {
            throw Invalid($"Tổng số tổ hợp biến thể vượt quá giới hạn tối đa {MaxCombinations}.");
        }

        // Tích Descartes: ghép mỗi tổ hợp hiện có với từng giá trị của trục kế tiếp.
        IReadOnlyList<Dictionary<string, string>> combinations = [new Dictionary<string, string>()];
        foreach (var axis in axes)
        {
            combinations = combinations
                .SelectMany(combination => axis.Values.Select(value => new Dictionary<string, string>(combination) { [axis.Name] = value }))
                .ToList();
        }

        return combinations;
    }

    private static List<ProductOption> SanitizeAxes(IReadOnlyList<ProductOption> options)
    {
        var axes = new List<ProductOption>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in options)
        {
            // Tên trục là khóa trong `optionValues` của SKU, nên không được rỗng hoặc trùng.
            if (string.IsNullOrWhiteSpace(option.Name))
            {
                throw Invalid("Tên trục tùy chọn không được để trống.");
            }

            var name = option.Name.Trim();
            if (!seenNames.Add(name))
            {
                throw Invalid($"Tên trục tùy chọn \"{name}\" bị trùng lặp.");
            }

            // Trục không có giá trị làm tích Descartes rỗng, nên sản phẩm mất hết biến thể.
            if (option.Values.Count == 0)
            {
                throw Invalid($"Trục tùy chọn \"{name}\" phải có ít nhất 1 giá trị.");
            }

            axes.Add(new ProductOption(name, SanitizeValues(name, option.Values)));
        }

        return axes;
    }

    private static List<string> SanitizeValues(string axisName, IReadOnlyList<string> values)
    {
        var sanitized = new List<string>();
        var seenValues = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawValue in values)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                throw Invalid($"Giá trị của trục tùy chọn \"{axisName}\" không được để trống.");
            }

            // Giá trị trùng sẽ sinh hai biến thể giống hệt nhau.
            var value = rawValue.Trim();
            if (!seenValues.Add(value))
            {
                throw Invalid($"Giá trị \"{value}\" của trục tùy chọn \"{axisName}\" bị trùng lặp.");
            }

            sanitized.Add(value);
        }

        return sanitized;
    }

    private static AppException Invalid(string message)
    {
        return new AppException("INVALID_PRODUCT_OPTIONS", 400, message);
    }
}
