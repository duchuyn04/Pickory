using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Pickory.Modules.Catalog.Entities;
using Pickory.Shared.Http;

namespace Pickory.Modules.Catalog.Features.Products;

// Bộ lọc danh sách sản phẩm quản trị phục vụ tìm kiếm, phân loại và cảnh báo tồn kho.
// Route danh sách và route xuất file cùng đọc tham số bằng `Read`, nên hai route luôn hiểu bộ lọc giống nhau.
public sealed record ProductListQuery(
    PageQuery Page,
    SearchTerm? Search = null,
    string? CategoryId = null,
    ProductStatus? Status = null,
    bool LowStock = false)
{
    // Lỗi được thêm vào `failures`. Route gom mọi lỗi của query string thành một lỗi `VALIDATION_ERROR`.
    public static ProductListQuery Read(IQueryCollection query, List<ValidationFailure> failures)
    {
        // Kiểm tra tính hợp lệ của tham số phân trang trước khi truy vấn.
        var page = PageQuery.Read(query, failures);
        // Đọc và kiểm tra trạng thái sản phẩm nếu người dùng cung cấp bộ lọc.
        var status = ReadStatus(query, failures);
        // Đọc cờ lọc tồn kho thấp để phát hiện sản phẩm sắp hết hàng.
        var lowStock = ReadLowStock(query, failures);
        // Từ khóa lặp lại hoặc dài quá giới hạn nhận lỗi, thay vì âm thầm bỏ bộ lọc.
        var search = SearchTerm.Read(query, failures);

        // Mã danh mục giữ nguyên để truy vấn lọc cả danh mục gốc và danh mục lá.
        var categoryId = query["categoryId"].FirstOrDefault();

        return new ProductListQuery(page, search, categoryId, status, lowStock);
    }

    private static ProductStatus? ReadStatus(IQueryCollection query, List<ValidationFailure> failures)
    {
        // Bỏ qua lọc trạng thái khi query string không chứa tham số này.
        if (!query.TryGetValue("status", out var rawValues))
        {
            return null;
        }

        // Từ chối tham số lặp vì bộ lọc chỉ nhận một trạng thái.
        if (rawValues.Count != 1)
        {
            failures.Add(new ValidationFailure("status", "Trạng thái sản phẩm không hợp lệ."));
            return null;
        }

        // Giá trị rỗng không kích hoạt bộ lọc trạng thái.
        var rawValue = rawValues[0]?.Trim();
        if (string.IsNullOrEmpty(rawValue))
        {
            return null;
        }

        // Chỉ chấp nhận tên trạng thái đơn để chặn danh sách và giá trị số của enum.
        if (string.Equals(rawValue, "draft", StringComparison.OrdinalIgnoreCase)) return ProductStatus.Draft;
        if (string.Equals(rawValue, "active", StringComparison.OrdinalIgnoreCase)) return ProductStatus.Active;
        if (string.Equals(rawValue, "hidden", StringComparison.OrdinalIgnoreCase)) return ProductStatus.Hidden;
        if (string.Equals(rawValue, "discontinued", StringComparison.OrdinalIgnoreCase)) return ProductStatus.Discontinued;

        failures.Add(new ValidationFailure("status", "Trạng thái sản phẩm không hợp lệ."));
        return null;
    }

    private static bool ReadLowStock(IQueryCollection query, List<ValidationFailure> failures)
    {
        // Chỉ dùng tham số lowStock của hợp đồng API để tránh hai tên cho cùng một bộ lọc.
        if (!query.TryGetValue("lowStock", out var rawValues))
        {
            return false;
        }

        // Chặn giá trị không phải boolean để URL sai không âm thầm lọc nhầm dữ liệu.
        if (bool.TryParse(rawValues.ToString(), out var lowStock))
        {
            return lowStock;
        }

        failures.Add(new ValidationFailure("lowStock", "Tham số lowStock phải là true hoặc false."));
        return false;
    }
}
