using System.Text.Json;
using Pickory.Modules.Catalog.Entities;
using Pickory.Shared.Exports;

namespace Pickory.Modules.Catalog.Features.Products;

// Một biến thể (SKU) đọc từ DB cho file xuất, kèm sản phẩm chứa nó. Bên gọi phải gọi `Dispose` cho `OptionValues` sau khi dựng file.
public sealed record ProductExportSource(
    string ProductName,
    string SkuCode,
    JsonDocument OptionValues,
    int Price,
    int Stock,
    string? BrandName,
    string? CategoryName,
    ProductStatus Status,
    DateTime CreatedAt);

// Một dòng ở sheet "Sản phẩm". Mọi giá trị đã ở dạng hiển thị, nên cột chỉ đọc thuộc tính mà không tính thêm.
public sealed record ProductExportRow(
    string ProductName,
    string SkuCode,
    string? OptionText,
    string? BrandName,
    string? CategoryName,
    int Price,
    int Stock,
    string StatusLabel,
    DateTime CreatedAt);

// Tên sheet, tiêu đề và thứ tự cột theo mục "File sản phẩm" của `spec/SPEC-exports-printing.md`.
public static class ProductExportSheets
{
    public const string SheetName = "Sản phẩm";

    private static readonly IReadOnlyList<ExportColumn<ProductExportRow>> Columns =
    [
        ExportColumn<ProductExportRow>.Text("Tên sản phẩm", 36, row => row.ProductName),
        ExportColumn<ProductExportRow>.Text("Mã SKU", 20, row => row.SkuCode),
        // Ba cột dưới đây là null khi sản phẩm không có phân loại, thương hiệu hoặc danh mục. Ô để trống.
        ExportColumn<ProductExportRow>.Text("Phân loại", 28, row => row.OptionText),
        ExportColumn<ProductExportRow>.Text("Thương hiệu", 18, row => row.BrandName),
        ExportColumn<ProductExportRow>.Text("Danh mục", 22, row => row.CategoryName),
        ExportColumn<ProductExportRow>.Money("Giá bán", 16, row => row.Price),
        // Cột `skus.stock` là số bán được. Spec catalog định nghĩa như vậy, nên tên cột không dùng chữ "tồn kho".
        ExportColumn<ProductExportRow>.Integer("Số bán được", 14, row => row.Stock),
        ExportColumn<ProductExportRow>.Text("Trạng thái", 14, row => row.StatusLabel),
        ExportColumn<ProductExportRow>.DateTime("Ngày tạo", 20, row => row.CreatedAt),
    ];

    public static ExportSheet BuildSheet(IEnumerable<ProductExportRow> rows) =>
        ExportSheet.Create(SheetName, Columns, rows);

    // Đổi biến thể đọc từ DB thành dòng hiển thị. Phân loại và nhãn trạng thái đổi sang chữ nhân viên đọc được.
    public static ProductExportRow BuildRow(ProductExportSource source) =>
        new(
            source.ProductName,
            source.SkuCode,
            ProductExportOptionText.Join(source.OptionValues.RootElement),
            source.BrandName,
            source.CategoryName,
            source.Price,
            source.Stock,
            ProductExportLabels.ForStatus(source.Status),
            source.CreatedAt);
}
