using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
        // Trạng thái là tài nguyên con của sản phẩm, và `PUT` đặt cả giá trị nên gửi lại cùng giá trị không đổi kết quả.
        // Đổi trạng thái là thao tác ghi, nên dùng chung quyền ghi sản phẩm.
        endpoints.MapPut("/api/admin/products/{id}/status", ChangeProductStatusAsync)
            .RequirePermission(Permissions.ProductsWrite);
        // Sản phẩm đã có đơn được xóa mềm, nên nhân viên có quyền ghi mới dùng được thao tác này.
        endpoints.MapDelete("/api/admin/products/{id}", DeleteProductAsync)
            .RequirePermission(Permissions.ProductsWrite);
        // Khôi phục là tạo lại trạng thái hoạt động từ dấu xóa mềm, nên là tài nguyên con dạng danh từ.
        endpoints.MapPost("/api/admin/products/{id}/restoration", RestoreProductAsync)
            .RequirePermission(Permissions.ProductsWrite);
    }

    private static async Task<IResult> GetProductsAsync(
        HttpContext context,
        ProductManagementHandler handler)
    {
        // Đọc mọi tham số lọc và phân trang bằng hàm dùng chung với route xuất file.
        var failures = new List<ValidationFailure>();
        var query = ProductListQuery.Read(context.Request.Query, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // Gửi toàn bộ tham số hợp lệ sang handler để thực thi truy vấn phân trang.
        var result = await handler.GetListAsync(query, context.RequestAborted);
        return Results.Ok(new ApiResponse<PagedResponse<ProductResponse>>(result));
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

    private static async Task<IResult> ChangeProductStatusAsync(
        HttpContext context,
        string id,
        ChangeProductStatusRequest? request,
        IValidator<ChangeProductStatusRequest> validator,
        ProductManagementHandler handler)
    {
        // Kiểm tra tên trạng thái trước khi đọc sản phẩm trong cơ sở dữ liệu.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        // Validator đã đảm bảo tên hợp lệ, nên phép chuyển đổi luôn thành công.
        ProductStatusNames.TryParse(validRequest.Status, out var target);
        try
        {
            var changed = await handler.ChangeStatusAsync(context.GetActor(), id, target, context.RequestAborted);
            return Results.Ok(new ApiResponse<ProductResponse>(changed));
        }
        catch (ProductNotPublishableException notPublishable)
        {
            // Trả danh sách lý do trong `details`, cùng dạng với `SPECS_INVALID`, để giao diện hiển thị đủ một lần.
            return Results.Json(new
            {
                error = new
                {
                    code = notPublishable.Code,
                    message = notPublishable.Message,
                    details = notPublishable.Reasons
                }
            }, statusCode: notPublishable.StatusCode);
        }
    }

    private static async Task<IResult> DeleteProductAsync(
        HttpContext context,
        string id,
        ProductManagementHandler handler)
    {
        var result = await handler.DeleteAsync(id, context.RequestAborted);
        return Results.Ok(new ApiResponse<DeleteProductResponse>(result));
    }

    private static async Task<IResult> RestoreProductAsync(
        HttpContext context,
        string id,
        ProductManagementHandler handler)
    {
        var restored = await handler.RestoreAsync(id, context.RequestAborted);
        return Results.Ok(new ApiResponse<ProductResponse>(restored));
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
