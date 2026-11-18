using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Features.Products;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Products;

// Nhãn tiếng Việt của trạng thái sản phẩm trong file xuất, trùng bốn nhãn của trang quản trị.
public sealed class ProductExportLabelsTests
{
    [Theory]
    [InlineData(ProductStatus.Draft, "Bản nháp")]
    [InlineData(ProductStatus.Active, "Đang bán")]
    [InlineData(ProductStatus.Hidden, "Đã ẩn")]
    [InlineData(ProductStatus.Discontinued, "Ngừng bán")]
    // Mỗi trạng thái có đúng một nhãn. Đổi chữ của một nhãn làm đúng một dòng đỏ.
    public void ForStatus_KnownValue_ReturnsVietnameseLabel(ProductStatus status, string expectedLabel)
    {
        Assert.Equal(expectedLabel, ProductExportLabels.ForStatus(status));
    }

    [Fact]
    // Mọi giá trị của enum đều có nhãn khác tên enum. Thêm trạng thái mới mà quên nhãn thì test này đỏ.
    public void ForStatus_EveryEnumValue_HasLabelDifferentFromEnumName()
    {
        foreach (var status in Enum.GetValues<ProductStatus>())
        {
            Assert.NotEqual(status.ToString(), ProductExportLabels.ForStatus(status));
        }
    }

    [Fact]
    // Giá trị lạ giữ nguyên dạng số và không ném lỗi, để file vẫn xuất được.
    public void ForStatus_UnknownValue_ReturnsRawNumber()
    {
        Assert.Equal("99", ProductExportLabels.ForStatus((ProductStatus)99));
    }
}
