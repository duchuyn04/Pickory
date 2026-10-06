using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Catalog.Features.Categories;

// Khai báo các endpoint HTTP cho danh mục sản phẩm của hệ thống.
public static class CategoriesEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/categories", GetCategoriesAsync);
        endpoints.MapGet("/api/admin/categories", GetAdminCategoriesAsync)
            .RequirePermission(Permissions.ProductsRead);
        endpoints.MapPost("/api/admin/categories", CreateCategoryAsync)
            .RequirePermission(Permissions.ProductsWrite);
        endpoints.MapPatch("/api/admin/categories/{id}", UpdateCategoryAsync)
            .RequirePermission(Permissions.ProductsWrite);
        endpoints.MapDelete("/api/admin/categories/{id}", DeleteCategoryAsync)
            .RequirePermission(Permissions.ProductsWrite);
    }

    private static async Task<IResult> GetCategoriesAsync(
        CategoryManagementHandler handler,
        CancellationToken cancellationToken)
    {
        // Trả cây danh mục hai cấp công khai cho người dùng và khách vãng lai.
        var tree = await handler.GetTreeAsync(cancellationToken);
        return Results.Ok(new ApiResponse<IReadOnlyList<CategoryTreeResponse>>(tree));
    }

    private static async Task<IResult> GetAdminCategoriesAsync(
        CategoryManagementHandler handler,
        CancellationToken cancellationToken)
    {
        // Trả danh sách phẳng toàn bộ danh mục kèm thuộc tính riêng để trang quản trị hiển thị và sửa.
        var categories = await handler.GetListAsync(cancellationToken);
        return Results.Ok(new ApiResponse<IReadOnlyList<CategoryResponse>>(categories));
    }

    private static async Task<IResult> CreateCategoryAsync(
        HttpContext context,
        CreateCategoryRequest? request,
        IValidator<CreateCategoryRequest> validator,
        CategoryManagementHandler handler)
    {
        // Kiểm tra tính hợp lệ của dữ liệu đầu vào trước khi gọi xuống nghiệp vụ.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        var created = await handler.CreateAsync(context.GetActor(), validRequest, context.RequestAborted);
        // Trả mã 201 Created cùng dữ liệu đối tượng vừa được khởi tạo.
        return Results.Json(new ApiResponse<CategoryResponse>(created), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateCategoryAsync(
        HttpContext context,
        string id,
        UpdateCategoryRequest? request,
        IValidator<UpdateCategoryRequest> validator,
        CategoryManagementHandler handler)
    {
        // Kiểm tra dữ liệu cập nhật trước khi áp dụng thay đổi vào cơ sở dữ liệu.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        var updated = await handler.UpdateAsync(context.GetActor(), id, validRequest, context.RequestAborted);
        return Results.Ok(new ApiResponse<CategoryResponse>(updated));
    }

    private static async Task<IResult> DeleteCategoryAsync(
        HttpContext context,
        string id,
        CategoryManagementHandler handler)
    {
        // Trả kết quả thành công sau khi đối tượng đã được xóa khỏi cơ sở dữ liệu.
        await handler.DeleteAsync(context.GetActor(), id, context.RequestAborted);
        return Results.Ok(new ApiResponse<CatalogSuccessResponse>(new CatalogSuccessResponse(true)));
    }
}

// Phản hồi thao tác thành công không trả dữ liệu chi tiết.
public sealed record CatalogSuccessResponse(bool Success);
