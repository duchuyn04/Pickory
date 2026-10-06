using System.Text.RegularExpressions;

namespace Pickory.Modules.Catalog.Attributes;

// Kiểm tra định nghĩa thuộc tính trước khi lưu vào danh mục, giống `attributes.ts` của bản Node.
public static partial class AttributeDefinitionRules
{
    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex KeyPattern();

    public static AttributeValidationResult Validate(AttributeDefinition attribute)
    {
        var errors = new List<AttributeError>();

        // Khóa sai mẫu làm hỏng truy vấn JSON và tên tham số của bộ lọc.
        if (string.IsNullOrEmpty(attribute.Key) || !KeyPattern().IsMatch(attribute.Key))
        {
            errors.Add(new AttributeError(attribute.Key, $"Khóa thuộc tính \"{attribute.Key}\" không đúng định dạng ^[a-z][a-z0-9_]*$."));
        }

        // Giao diện cần nhãn để hiển thị tên thuộc tính.
        if (string.IsNullOrWhiteSpace(attribute.Label))
        {
            errors.Add(new AttributeError(attribute.Key, "Nhãn thuộc tính không được để trống."));
        }

        // Bảng thông số chia theo nhóm, nên nhóm không được rỗng.
        if (string.IsNullOrWhiteSpace(attribute.Group))
        {
            errors.Add(new AttributeError(attribute.Key, "Nhóm thuộc tính không được để trống."));
        }

        // Kiểu lạ không có luật validate thông số tương ứng.
        if (!AttributeTypes.All.Contains(attribute.Type))
        {
            errors.Add(new AttributeError(
                attribute.Key,
                $"Kiểu thuộc tính \"{attribute.Type}\" không hợp lệ, chỉ nhận text, number, enum hoặc boolean."));
        }

        if (attribute.Type == AttributeTypes.Enum)
        {
            ValidateEnumOptions(attribute, errors);
        }

        return new AttributeValidationResult(errors);
    }

    public static AttributeValidationResult ValidateAll(IReadOnlyList<AttributeDefinition> attributes)
    {
        var errors = new List<AttributeError>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attribute in attributes)
        {
            errors.AddRange(Validate(attribute).Errors);
            // Hai thuộc tính cùng khóa ghi đè giá trị của nhau trong cột `specs`.
            if (!seenKeys.Add(attribute.Key))
            {
                errors.Add(new AttributeError(attribute.Key, $"Khóa thuộc tính \"{attribute.Key}\" bị lặp lại trong danh sách."));
            }
        }

        return new AttributeValidationResult(errors);
    }

    private static void ValidateEnumOptions(AttributeDefinition attribute, List<AttributeError> errors)
    {
        // `enum` không có lựa chọn thì không có giá trị hợp lệ nào.
        if (attribute.Options is null || attribute.Options.Count == 0)
        {
            errors.Add(new AttributeError(attribute.Key, $"Thuộc tính \"{attribute.Key}\" kiểu enum bắt buộc phải có danh sách options."));
            return;
        }

        var seenOptions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in attribute.Options)
        {
            // Lựa chọn rỗng tạo một dòng trống trong danh sách chọn của giao diện.
            if (string.IsNullOrWhiteSpace(option))
            {
                errors.Add(new AttributeError(attribute.Key, $"Tùy chọn của thuộc tính \"{attribute.Key}\" không được để trống."));
                return;
            }

            // So sánh sau khi cắt khoảng trắng. `Đen` và ` Đen ` là cùng một lựa chọn.
            var trimmedOption = option.Trim();
            if (!seenOptions.Add(trimmedOption))
            {
                errors.Add(new AttributeError(attribute.Key, $"Tùy chọn \"{trimmedOption}\" của thuộc tính \"{attribute.Key}\" bị lặp lại."));
                return;
            }
        }
    }
}
