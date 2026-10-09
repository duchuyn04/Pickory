using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Catalog.Features.Categories;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Catalog.Features.Brands;

// Khai báo các endpoint HTTP cho thương hiệu sản phẩm của hệ thống.
public static class BrandsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/brands", GetBrandsAsync);
        endpoints.MapPost("/api/admin/brands", CreateBrandAsync)
            .RequirePermission(Permissions.ProductsWrite);
        endpoints.MapPatch("/api/admin/brands/{id}", UpdateBrandAsync)
            .RequirePermission(Permissions.ProductsWrite);
        endpoints.MapDelete("/api/admin/brands/{id}", DeleteBrandAsync)
            .RequirePermission(Permissions.ProductsWrite);
    }

    private static async Task<IResult> GetBrandsAsync(
        BrandManagementHandler handler,
        CancellationToken cancellationToken)
    {
        // Trả danh sách thương hiệu công khai cho người dùng và khách vãng lai.
        var brands = await handler.ListAsync(cancellationToken);
        return Results.Ok(new ApiResponse<IReadOnlyList<BrandResponse>>(brands));
    }

    private static async Task<IResult> CreateBrandAsync(
        HttpContext context,
        CreateBrandRequest? request,
        IValidator<CreateBrandRequest> validator,
        BrandManagementHandler handler)
    {
        // Kiểm tra dữ liệu đầu vào trước khi tiến hành tạo thương hiệu mới.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        var created = await handler.CreateAsync(context.GetActor(), validRequest, context.RequestAborted);
        // Trả mã 201 Created cùng dữ liệu đối tượng vừa được khởi tạo.
        return Results.Json(new ApiResponse<BrandResponse>(created), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateBrandAsync(
        HttpContext context,
        string id,
        UpdateBrandRequest? request,
        IValidator<UpdateBrandRequest> validator,
        BrandManagementHandler handler)
    {
        // Kiểm tra thông tin cập nhật trước khi áp dụng thay đổi vào cơ sở dữ liệu.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        var updated = await handler.UpdateAsync(context.GetActor(), id, validRequest, context.RequestAborted);
        return Results.Ok(new ApiResponse<BrandResponse>(updated));
    }

    private static async Task<IResult> DeleteBrandAsync(
        HttpContext context,
        string id,
        BrandManagementHandler handler)
    {
        // Trả kết quả thành công sau khi đối tượng đã được xóa khỏi cơ sở dữ liệu.
        await handler.DeleteAsync(context.GetActor(), id, context.RequestAborted);
        return Results.Ok(new ApiResponse<CatalogSuccessResponse>(new CatalogSuccessResponse(true)));
    }
}
