using System.Text.Json;

namespace Pickory.Modules.Catalog.Features.Products;

// Nối giá trị phân loại của biến thể (SKU) thành một chuỗi. Nhân viên đọc "Đen, 256 GB" thay vì JSON.
public static class ProductExportOptionText
{
    private const string Separator = ", ";

    // Trả null khi không có giá trị nào để nối, để ô Excel trống thay vì chứa chuỗi rỗng.
    public static string? Join(JsonElement optionValues)
    {
        // Dữ liệu không phải object không có cặp khóa và giá trị. Ô để trống thay vì làm hỏng cả file xuất.
        if (optionValues.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var values = new List<string>();
        foreach (var property in optionValues.EnumerateObject())
        {
            var text = ReadValueText(property.Value);

            // Giá trị rỗng hoặc toàn khoảng trắng bị bỏ. Nếu không, chuỗi kết quả có dấu nối thừa như ", Đen".
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            values.Add(text);
        }

        return values.Count == 0 ? null : string.Join(Separator, values);
    }

    private static string ReadValueText(JsonElement value)
    {
        return value.ValueKind switch
        {
            // Chữ giữ nguyên nội dung, không thêm dấu ngoặc kép của JSON.
            JsonValueKind.String => value.GetString()!,
            // Giá trị null coi như rỗng, vì chữ "null" không có nghĩa với người đọc file.
            JsonValueKind.Null => string.Empty,
            // Số, giá trị logic, mảng và object giữ dạng JSON để nhân viên không mất thông tin.
            _ => value.GetRawText(),
        };
    }
}
