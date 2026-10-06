using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Catalog.Features.Products;

// Khai báo các endpoint HTTP quản trị cho sản phẩm và biến thể.
public static class ProductsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Quyền đọc sản phẩm cho phép nhân viên tra cứu danh sách ở mọi trạng thái.
        endpoints.MapGet("/api/admin/products", GetProductsAsync)
            .RequirePermission(Permissions.ProductsRead);
        // Quyền đọc sản phẩm ngăn người dùng không có thẩm quyền truy cập thông tin chi tiết.
        endpoints.MapGet("/api/admin/products/{id}", GetProductByIdAsync)
            .RequirePermission(Permissions.ProductsRead);
        // Quyền ghi sản phẩm ngăn người dùng không có thẩm quyền tạo mới dữ liệu bán hàng.
        endpoints.MapPost("/api/admin/products", CreateProductAsync)
            .RequirePermission(Permissions.ProductsWrite);
        // Quyền ghi sản phẩm bảo vệ thông tin giá và thông số kỹ thuật khỏi can thiệp trái phép.
        endpoints.MapPatch("/api/admin/products/{id}", UpdateProductAsync)
            .RequirePermission(Permissions.ProductsWrite);
    }

    private static async Task<IResult> GetProductsAsync(
        HttpContext context,
        ProductManagementHandler handler)
    {
        // Kiểm tra tính hợp lệ của tham số phân trang trước khi truy vấn.
        var failures = new List<ValidationFailure>();
        var page = PageQuery.Read(context.Request.Query, failures);
        // Đọc và kiểm tra trạng thái sản phẩm nếu người dùng cung cấp bộ lọc.
        var status = ReadStatus(context.Request.Query, failures);
        // Đọc cờ lọc tồn kho thấp để phát hiện sản phẩm sắp hết hàng.
        var lowStock = ReadLowStock(context.Request.Query, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // Giữ nguyên truy vấn tìm kiếm để handler áp dụng trên tên và mã SKU.
        var search = context.Request.Query["search"].FirstOrDefault();
        // Mã danh mục giữ nguyên để handler lọc cả danh mục gốc và danh mục lá.
        var categoryId = context.Request.Query["categoryId"].FirstOrDefault();

        // Gửi toàn bộ tham số hợp lệ sang handler để thực thi truy vấn phân trang.
        var query = new ProductListQuery(page, search, categoryId, status, lowStock);
        var result = await handler.GetListAsync(query, context.RequestAborted);
        return Results.Ok(new ApiResponse<PagedResponse<ProductResponse>>(result));
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

    private static async Task<IResult> GetProductByIdAsync(
        HttpContext context,
        string id,
        ProductManagementHandler handler)
    {
        // Nạp toàn bộ thông tin sản phẩm và SKU để quản trị viên kiểm tra trước khi chỉnh sửa.
        var product = await handler.GetAsync(id, context.RequestAborted);
        return Results.Ok(new ApiResponse<ProductResponse>(product));
    }

    private static async Task<IResult> CreateProductAsync(
        HttpContext context,
        CreateProductRequest? request,
        IValidator<CreateProductRequest> validator,
        ProductManagementHandler handler)
    {
        // Xác thực dữ liệu gửi lên trước khi tạo mới bản ghi trong cơ sở dữ liệu.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        try
        {
            // Ủy quyền cho tầng nghiệp vụ khởi tạo sản phẩm và sinh các SKU ban đầu trong một transaction.
            var created = await handler.CreateAsync(context.GetActor(), validRequest, context.RequestAborted);
            return Results.Json(new ApiResponse<ProductResponse>(created), statusCode: StatusCodes.Status201Created);
        }
        catch (ProductSpecsException specsException)
        {
            return SpecsInvalidResult(specsException);
        }
    }

    private static async Task<IResult> UpdateProductAsync(
        HttpContext context,
        string id,
        UpdateProductRequest? request,
        IValidator<UpdateProductRequest> validator,
        ProductManagementHandler handler)
    {
        // Xác thực tính hợp lệ của dữ liệu cập nhật từng phần trước khi áp dụng thay đổi.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        try
        {
            // Gửi dữ liệu cập nhật xuống tầng nghiệp vụ để đồng bộ biến thể và lưu vết lịch sử giá.
            var updated = await handler.UpdateAsync(context.GetActor(), id, validRequest, context.RequestAborted);
            return Results.Ok(new ApiResponse<ProductResponse>(updated));
        }
        catch (ProductSpecsException specsException)
        {
            return SpecsInvalidResult(specsException);
        }
    }

    private static IResult SpecsInvalidResult(ProductSpecsException specsException)
    {
        // Trả mã lỗi 400 cùng danh sách chi tiết lỗi thuộc tính để giao diện đánh dấu ô nhập.
        return Results.BadRequest(new
        {
            error = new
            {
                code = specsException.Code,
                message = specsException.Message,
                details = specsException.Errors
            }
        });
    }
}
