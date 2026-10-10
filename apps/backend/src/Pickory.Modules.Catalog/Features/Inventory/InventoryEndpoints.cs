using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Catalog.Features.Inventory;

// Khai báo các endpoint HTTP quản trị tồn kho.
public static class InventoryEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Mỗi lần điều chỉnh tạo một dòng biến động, nên route là `POST` trên tập hợp `movements`.
        // Điều chỉnh dùng quyền riêng, vì sai số ở đây làm lệch hàng bán được.
        endpoints.MapPost("/api/admin/skus/{id}/movements", AdjustStockAsync)
            .RequirePermission(Permissions.InventoryWrite);
        // Xem lịch sử và danh sách sắp hết hàng chỉ đọc, nên dùng quyền đọc sản phẩm. `GET` và `POST` cùng đường dẫn có quyền khác nhau.
        endpoints.MapGet("/api/admin/skus/{id}/movements", GetMovementsAsync)
            .RequirePermission(Permissions.ProductsRead);
        endpoints.MapGet("/api/admin/inventory/low-stock", GetLowStockAsync)
            .RequirePermission(Permissions.ProductsRead);
    }

    private static async Task<IResult> AdjustStockAsync(
        HttpContext context,
        string id,
        AdjustStockRequest? request,
        IValidator<AdjustStockRequest> validator,
        InventoryHandler handler)
    {
        // Kiểm body trước khi mở transaction, để body sai không giữ khóa dòng SKU.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        var adjusted = await handler.AdjustStockAsync(context.GetActor(), id, validRequest, context.RequestAborted);
        // Route vừa tạo một dòng biến động mới, nên trả 201 thay cho 200.
        return Results.Json(new ApiResponse<AdjustStockResponse>(adjusted), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetMovementsAsync(HttpContext context, string id, InventoryHandler handler)
    {
        var page = ReadPage(context);
        var movements = await handler.GetMovementsAsync(id, page, context.RequestAborted);
        return Results.Ok(new ApiResponse<PagedResponse<InventoryMovementResponse>>(movements));
    }

    private static async Task<IResult> GetLowStockAsync(HttpContext context, InventoryHandler handler)
    {
        var page = ReadPage(context);
        var lowStock = await handler.GetLowStockAsync(page, context.RequestAborted);
        return Results.Ok(new ApiResponse<PagedResponse<LowStockSkuResponse>>(lowStock));
    }

    private static PageQuery ReadPage(HttpContext context)
    {
        // Gom mọi lỗi query string thành một lỗi `VALIDATION_ERROR`, giống danh sách sản phẩm.
        var failures = new List<ValidationFailure>();
        var page = PageQuery.Read(context.Request.Query, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return page;
    }
}
