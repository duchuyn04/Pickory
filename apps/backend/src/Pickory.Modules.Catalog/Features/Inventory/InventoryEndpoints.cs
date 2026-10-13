using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Stock;
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
        // Kiểm mọi tham số trước khi chạm DB. Tham số sai nhận 400 dù SKU có tồn tại hay không.
        var failures = new List<ValidationFailure>();
        var page = PageQuery.Read(context.Request.Query, failures);
        var type = ReadMovementType(context.Request.Query, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var movements = await handler.GetMovementsAsync(id, page, type, context.RequestAborted);
        return Results.Ok(new ApiResponse<PagedResponse<InventoryMovementResponse>>(movements));
    }

    private static async Task<IResult> GetLowStockAsync(HttpContext context, InventoryHandler handler)
    {
        var failures = new List<ValidationFailure>();
        var page = PageQuery.Read(context.Request.Query, failures);
        var search = SearchTerm.Read(context.Request.Query, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var lowStock = await handler.GetLowStockAsync(page, search, context.RequestAborted);
        return Results.Ok(new ApiResponse<PagedResponse<LowStockSkuResponse>>(lowStock));
    }

    // Chỉ nhận đúng bốn tên của API. Giá trị lạ nhận lỗi, vì danh sách không lọc làm nhân viên hiểu sai lịch sử.
    private static InventoryMovementType? ReadMovementType(IQueryCollection query, List<ValidationFailure> failures)
    {
        var raw = QueryParameter.ReadSingle(query, "type", failures);
        if (raw is null)
        {
            return null;
        }

        if (!MovementTypeNames.TryParse(raw, out var type))
        {
            failures.Add(new ValidationFailure("type", "Loại biến động phải là `manual`, `order_reserve`, `order_release` hoặc `return`."));
            return null;
        }

        return type;
    }
}
