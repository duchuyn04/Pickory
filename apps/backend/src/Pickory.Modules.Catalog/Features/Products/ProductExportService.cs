using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Catalog.Products;
using Pickory.Shared.Exports;

namespace Pickory.Modules.Catalog.Features.Products;

// File `.xlsx` đã dựng xong trong bộ nhớ, cùng tên file trả cho nhân viên.
internal sealed record ProductExportFile(byte[] Content, string FileName);

// Xuất sản phẩm ra Excel, mỗi dòng một biến thể (SKU). Dùng chung bộ lọc với danh sách, nhưng đọc mọi sản phẩm khớp thay vì một trang.
internal sealed class ProductExportService(CatalogDbContext database, ExportOptions exportOptions, TimeProvider clock)
{
    public const string FileNamePrefix = "san-pham";

    public async Task<ProductExportFile> ExportAsync(ProductListQuery filter, CancellationToken cancellationToken)
    {
        // Đọc `MaxRows + 1` biến thể. Dòng thứ `MaxRows + 1` chứng minh file vượt giới hạn mà không cần câu `COUNT`.
        var sources = await ReadSkusAsync(filter, exportOptions.MaxRows + 1, cancellationToken);
        try
        {
            // Giới hạn tính theo dòng của file, tức biến thể. Sản phẩm nhiều biến thể vượt giới hạn dù có ít sản phẩm.
            ExportLimits.EnsureWithinLimit(sources.Count, exportOptions.MaxRows);

            // Lấy giờ từ `clock` thay cho giờ hệ thống. Test đặt cố định được tên file.
            var fileName = ExportFileName.Create(FileNamePrefix, clock.GetUtcNow());

            // Dựng file ngay trong bộ nhớ. Số dòng đã nằm trong giới hạn nên file không quá lớn.
            var content = XlsxWorkbookWriter.Write(
            [
                ProductExportSheets.BuildSheet(sources.Select(ProductExportSheets.BuildRow)),
            ]);
            return new ProductExportFile(content, fileName);
        }
        finally
        {
            // Giải phóng mọi `JsonDocument` đã đọc, kể cả khi vượt giới hạn hoặc dựng file lỗi.
            foreach (var source in sources)
            {
                source.OptionValues.Dispose();
            }
        }
    }

    private async Task<List<ProductExportSource>> ReadSkusAsync(
        ProductListQuery filter,
        int takeCount,
        CancellationToken cancellationToken)
    {
        var products = ProductListFilter.Apply(database.Products.AsNoTracking(), filter);

        // Chỉ xuất biến thể đang bật, vì biến thể tắt là tổ hợp phân loại đã bỏ. Khi lọc `lowStock`, chỉ giữ biến thể sắp hết.
        Expression<Func<Sku, bool>> exportedSkus = filter.LowStock ? LowStockExpressions.IsLowStock : sku => sku.Enabled;

        // Một truy vấn phẳng cho mọi biến thể của các sản phẩm khớp bộ lọc, nên không có truy vấn riêng cho từng sản phẩm.
        return await products
            .SelectMany(product => product.Skus.AsQueryable().Where(exportedSkus), (product, sku) => new { product, sku })
            // Thứ tự sản phẩm giống danh sách: mới tạo lên đầu, rồi theo `Id`. Trong một sản phẩm, biến thể theo `Code`.
            .OrderByDescending(pair => pair.product.CreatedAt)
            .ThenBy(pair => pair.product.Id)
            .ThenBy(pair => pair.sku.Code)
            .Take(takeCount)
            // Chỉ chiếu cột cần ghi ra file. Ảnh, mô tả và tùy chọn của sản phẩm không bao giờ rời DB.
            .Select(pair => new ProductExportSource(
                pair.product.Name,
                pair.sku.Code,
                pair.sku.OptionValues,
                pair.sku.Price,
                pair.sku.Stock,
                // Đọc thương hiệu và danh mục bằng truy vấn con. Phép nối bắt buộc làm mất sản phẩm có thương hiệu hoặc danh mục đã xóa mềm.
                database.Brands.Where(brand => brand.Id == pair.product.BrandId).Select(brand => brand.Name).FirstOrDefault(),
                database.Categories.Where(category => category.Id == pair.product.CategoryId).Select(category => category.Name).FirstOrDefault(),
                pair.product.Status,
                pair.product.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
