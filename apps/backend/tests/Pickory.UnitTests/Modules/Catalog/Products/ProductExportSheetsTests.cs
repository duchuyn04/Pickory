using System.Text.Json;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Features.Products;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Products;

// Dựng dòng hiển thị của sheet "Sản phẩm" từ một biến thể (SKU) đọc từ DB.
public sealed class ProductExportSheetsTests
{
    private static readonly DateTime CreatedAt = new(2026, 10, 1, 2, 0, 0, DateTimeKind.Utc);

    private static ProductExportSource Source(
        string optionValuesJson = """{"color":"Đen","storage":"256 GB"}""",
        string? brandName = "Apple",
        string? categoryName = "Điện thoại",
        ProductStatus status = ProductStatus.Active) =>
        new(
            "iPhone 16 256GB",
            "IP16-256-BLK",
            JsonDocument.Parse(optionValuesJson),
            24_990_000,
            3,
            brandName,
            categoryName,
            status,
            CreatedAt);

    [Fact]
    // Mỗi trường của nguồn đi vào đúng trường của dòng. Giá bán và số bán được không bị đổi chỗ.
    public void BuildRow_FullSku_MapsEveryField()
    {
        var row = ProductExportSheets.BuildRow(Source());

        Assert.Equal("iPhone 16 256GB", row.ProductName);
        Assert.Equal("IP16-256-BLK", row.SkuCode);
        Assert.Equal("Đen, 256 GB", row.OptionText);
        Assert.Equal("Apple", row.BrandName);
        Assert.Equal("Điện thoại", row.CategoryName);
        Assert.Equal(24_990_000, row.Price);
        Assert.Equal(3, row.Stock);
        Assert.Equal("Đang bán", row.StatusLabel);
        Assert.Equal(CreatedAt, row.CreatedAt);
    }

    [Fact]
    // Sản phẩm không có thương hiệu hoặc danh mục giữ null. Null làm ô Excel để trống, không phải chuỗi rỗng.
    public void BuildRow_WithoutBrandAndCategory_KeepsThemNull()
    {
        var row = ProductExportSheets.BuildRow(Source(brandName: null, categoryName: null));

        Assert.Null(row.BrandName);
        Assert.Null(row.CategoryName);
    }

    [Fact]
    // SKU không có phân loại có `OptionText` là null, nên ô "Phân loại" để trống.
    public void BuildRow_SkuWithoutOptions_LeavesOptionTextNull()
    {
        var row = ProductExportSheets.BuildRow(Source(optionValuesJson: "{}"));

        Assert.Null(row.OptionText);
    }

    [Fact]
    // Nhãn trạng thái lấy từ bảng nhãn chung, không phải tên enum.
    public void BuildRow_DiscontinuedProduct_UsesVietnameseStatusLabel()
    {
        var row = ProductExportSheets.BuildRow(Source(status: ProductStatus.Discontinued));

        Assert.Equal("Ngừng bán", row.StatusLabel);
    }
}
