using Pickory.Modules.Catalog.Entities;

namespace Pickory.Modules.Catalog.Features.Products;

// Nhãn tiếng Việt của trạng thái sản phẩm trong file xuất. Bốn nhãn trùng với `product-status.ts` của trang quản trị.
public static class ProductExportLabels
{
    public static string ForStatus(ProductStatus status)
    {
        return status switch
        {
            ProductStatus.Draft => "Bản nháp",
            ProductStatus.Active => "Đang bán",
            ProductStatus.Hidden => "Đã ẩn",
            ProductStatus.Discontinued => "Ngừng bán",
            // Giá trị lạ giữ nguyên dạng số. Nhân viên không mất thông tin và file vẫn xuất được.
            _ => status.ToString(),
        };
    }
}
