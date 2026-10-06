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
    }

    private static async Task<IResult> GetProductsAsync(
        HttpContext context,
        ProductManagementHandler handler)
    {
        // Kiểm tra tính hợp lệ của tham số phân trang trước khi truy vấn.
        var failures = new List<ValidationFailure>();
        var page = PageQuery.Read(context.Request.Query, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // Truy vấn danh sách kèm từ khóa giúp người dùng lọc nhanh sản phẩm theo tên hoặc mã SKU.
        var search = context.Request.Query["search"].FirstOrDefault();
        var result = await handler.GetListAsync(page, search, context.RequestAborted);
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
