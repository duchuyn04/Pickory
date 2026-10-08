using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using Pickory.Modules.Catalog.Attributes;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Đổi tham số `attr.*` thành bộ lọc theo Decision 11. Chỉ thuộc tính `filterable` của danh mục được nhận.
public static class AttributeFilterReader
{
    public const string ParameterPrefix = "attr.";

    private const string MinSuffix = "_min";
    private const string MaxSuffix = "_max";

    public static IReadOnlyList<AttributeFilter> Read(
        IReadOnlyDictionary<string, string> attributeParameters,
        IReadOnlyList<AttributeDefinition> filterableAttributes)
    {
        var definitionsByKey = filterableAttributes.ToDictionary(definition => definition.Key, StringComparer.Ordinal);
        var filters = new List<AttributeFilter>();
        var rangeBounds = new Dictionary<string, RangeBounds>(StringComparer.Ordinal);
        var unknownKeys = new List<string>();
        var failures = new List<ValidationFailure>();

        // Duyệt theo tên tham số để thứ tự lỗi và thứ tự bộ lọc không phụ thuộc thứ tự trên URL.
        foreach (var (parameterName, rawValue) in attributeParameters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var keyText = parameterName[ParameterPrefix.Length..];

            // Khóa khớp nguyên văn thắng cách đọc hậu tố, nên thuộc tính tên `price_min` vẫn lọc được.
            if (definitionsByKey.TryGetValue(keyText, out var definition))
            {
                var filter = ReadWholeKeyFilter(parameterName, rawValue, definition, failures);
                if (filter is not null)
                {
                    filters.Add(filter);
                }

                continue;
            }

            // Khóa còn lại chỉ hợp lệ khi là cận `_min` hoặc `_max` của một thuộc tính `number`.
            if (TrySplitRangeKey(keyText, out var baseKey, out var isMinimum)
                && definitionsByKey.TryGetValue(baseKey, out var numberDefinition)
                && numberDefinition.Type == AttributeTypes.Number)
            {
                ReadRangeBound(parameterName, rawValue, baseKey, isMinimum, rangeBounds, failures);
                continue;
            }

            unknownKeys.Add(keyText);
        }

        // Khóa lạ báo trước lỗi giá trị, vì sửa khóa mới có ý nghĩa để sửa giá trị.
        if (unknownKeys.Count > 0)
        {
            throw new UnknownFilterException(unknownKeys);
        }

        AddRangeFilters(rangeBounds, filters, failures);

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return filters;
    }

    private static AttributeFilter? ReadWholeKeyFilter(
        string parameterName, string rawValue, AttributeDefinition definition, List<ValidationFailure> failures)
    {
        switch (definition.Type)
        {
            case AttributeTypes.Number:
                // Số chỉ lọc theo khoảng, nên khóa trần không có nghĩa.
                failures.Add(new ValidationFailure(parameterName, $"Thuộc tính số dùng {parameterName}{MinSuffix} và {parameterName}{MaxSuffix}."));
                return null;

            case AttributeTypes.Boolean:
                // Chỉ nhận đúng `true` hoặc `false` để giá trị lạ không âm thầm đổi tập kết quả.
                if (rawValue is "true" or "false")
                {
                    return new BooleanFilter(definition.Key, rawValue == "true");
                }

                failures.Add(new ValidationFailure(parameterName, "Giá trị phải là true hoặc false."));
                return null;

            default:
                // Danh sách rỗng khớp mọi thứ hoặc không khớp gì, cả hai đều dễ hiểu nhầm, nên từ chối.
                var values = SplitList(rawValue);
                if (values.Count == 0)
                {
                    failures.Add(new ValidationFailure(parameterName, "Danh sách giá trị không được rỗng."));
                    return null;
                }

                return new ValueListFilter(definition.Key, values);
        }
    }

    private static void ReadRangeBound(
        string parameterName, string rawValue, string baseKey, bool isMinimum,
        Dictionary<string, RangeBounds> rangeBounds, List<ValidationFailure> failures)
    {
        // Chỉ nhận số thập phân viết theo kiểu invariant, vì dấu phẩy trên URL đã dành cho danh sách.
        if (!decimal.TryParse(rawValue, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
        {
            failures.Add(new ValidationFailure(parameterName, "Giá trị phải là một số."));
            return;
        }

        // Cận `_min` và `_max` đến riêng lẻ, nên ghép vào cùng một mục theo khóa gốc.
        var bounds = rangeBounds.GetValueOrDefault(baseKey) ?? new RangeBounds();
        rangeBounds[baseKey] = isMinimum
            ? bounds with { Min = number, MinParameter = parameterName }
            : bounds with { Max = number };
    }

    private static void AddRangeFilters(
        Dictionary<string, RangeBounds> rangeBounds, List<AttributeFilter> filters, List<ValidationFailure> failures)
    {
        foreach (var (key, bounds) in rangeBounds.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            // Cận dưới lớn hơn cận trên luôn cho tập rỗng, nên coi là lỗi nhập liệu.
            if (bounds.Min > bounds.Max)
            {
                failures.Add(new ValidationFailure(
                    bounds.MinParameter ?? $"{ParameterPrefix}{key}{MinSuffix}",
                    $"Giá trị {ParameterPrefix}{key}{MinSuffix} không được lớn hơn {ParameterPrefix}{key}{MaxSuffix}."));
                continue;
            }

            filters.Add(new NumberRangeFilter(key, bounds.Min, bounds.Max));
        }
    }

    private static bool TrySplitRangeKey(string keyText, out string baseKey, out bool isMinimum)
    {
        isMinimum = keyText.EndsWith(MinSuffix, StringComparison.Ordinal);
        var suffix = isMinimum ? MinSuffix : MaxSuffix;
        var hasSuffix = isMinimum || keyText.EndsWith(MaxSuffix, StringComparison.Ordinal);
        baseKey = hasSuffix ? keyText[..^suffix.Length] : string.Empty;
        return hasSuffix;
    }

    // Tách danh sách theo dấu phẩy, cắt khoảng trắng và bỏ mục rỗng.
    public static IReadOnlyList<string> SplitList(string rawValue) =>
        rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private sealed record RangeBounds(decimal? Min = null, decimal? Max = null, string? MinParameter = null);
}
