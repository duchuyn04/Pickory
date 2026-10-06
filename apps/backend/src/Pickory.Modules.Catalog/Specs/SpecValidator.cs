using System.Text.Json;
using System.Text.Json.Nodes;
using Pickory.Modules.Catalog.Attributes;

namespace Pickory.Modules.Catalog.Specs;

// Kiểm tra thông số của sản phẩm theo bộ thuộc tính của danh mục, giống `validate-specs.ts` của bản Node.
public static class SpecValidator
{
    public static SpecValidationResult Validate(JsonNode? specs, IReadOnlyList<AttributeDefinition> attributes)
    {
        // Thông số phải là object JSON. Mảng, chuỗi hoặc null không có khóa nào để kiểm tra.
        if (specs is not JsonObject specObject)
        {
            return new SpecValidationResult([new SpecError("specs", "Dữ liệu thông số phải là một đối tượng JSON hợp lệ.")]);
        }

        var errors = new List<SpecError>();
        var attributeKeys = attributes.Select(attribute => attribute.Key).ToHashSet(StringComparer.Ordinal);
        // Khóa lạ ngoài bộ thuộc tính làm bẩn cột `specs` và không lọc được.
        foreach (var (key, _) in specObject)
        {
            if (!attributeKeys.Contains(key))
            {
                errors.Add(new SpecError(key, $"Khóa \"{key}\" không nằm trong bộ thuộc tính của danh mục."));
            }
        }

        foreach (var attribute in attributes)
        {
            var error = ValidateValue(attribute, specObject[attribute.Key]);
            if (error is not null)
            {
                errors.Add(error);
            }
        }

        return new SpecValidationResult(errors);
    }

    private static SpecError? ValidateValue(AttributeDefinition attribute, JsonNode? value)
    {
        var name = $"\"{attribute.Label}\" ({attribute.Key})";
        // Giá trị null được coi như chưa nhập. Chỉ thuộc tính bắt buộc mới báo lỗi.
        if (value is null)
        {
            return attribute.Required ? new SpecError(attribute.Key, $"Thiếu thuộc tính bắt buộc {name}.") : null;
        }

        var kind = value.GetValueKind();
        return attribute.Type switch
        {
            AttributeTypes.Text when kind != JsonValueKind.String
                => new SpecError(attribute.Key, $"Thuộc tính {name} phải là chuỗi văn bản."),
            // Chuỗi chỉ có khoảng trắng không có gì để hiển thị.
            AttributeTypes.Text when string.IsNullOrWhiteSpace(value.GetValue<string>())
                => new SpecError(attribute.Key, $"Thuộc tính {name} không được là chuỗi rỗng."),
            // Bộ lọc theo khoảng giá trị cần số thật, không nhận số dạng chuỗi.
            AttributeTypes.Number when kind != JsonValueKind.Number
                => new SpecError(attribute.Key, $"Thuộc tính {name} phải là số hợp lệ."),
            // Bộ lọc checkbox cần `true` hoặc `false`, không nhận chuỗi `"true"`.
            AttributeTypes.Boolean when kind is not (JsonValueKind.True or JsonValueKind.False)
                => new SpecError(attribute.Key, $"Thuộc tính {name} phải là kiểu boolean."),
            AttributeTypes.Enum when kind != JsonValueKind.String || !(attribute.Options ?? []).Contains(value.GetValue<string>())
                => new SpecError(attribute.Key, $"Thuộc tính {name} nhận giá trị không nằm trong danh sách lựa chọn."),
            _ => null,
        };
    }
}
