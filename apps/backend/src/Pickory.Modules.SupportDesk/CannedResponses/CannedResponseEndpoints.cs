using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.SupportDesk.CannedResponses;

// Route mẫu trả lời nhanh. Quyền cụ thể kiểm trong service theo loại mẫu (cá nhân hay dùng chung).
internal static class CannedResponseEndpoints
{
    private const string CannedResponsesPath = "/api/admin/support/canned-responses";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Danh sách phục vụ ô gõ `/` và màn cấu hình, nên route nhận một trong hai quyền. Service lọc mẫu cá nhân theo quyền.
        endpoints.MapGet(CannedResponsesPath, ListAsync)
            .RequireAnyOfPermissions(Permissions.ConversationsReply, Permissions.SupportSettingsManage);
        // Người chỉ có `support-settings:manage` vẫn được sửa mẫu dùng chung, nên route chỉ đòi một trong hai quyền.
        endpoints.MapPost(CannedResponsesPath, CreateAsync)
            .RequireAnyOfPermissions(Permissions.ConversationsReply, Permissions.SupportSettingsManage);
        endpoints.MapPatch($"{CannedResponsesPath}/{{id}}", UpdateAsync)
            .RequireAnyOfPermissions(Permissions.ConversationsReply, Permissions.SupportSettingsManage);
        endpoints.MapDelete($"{CannedResponsesPath}/{{id}}", DeleteAsync)
            .RequireAnyOfPermissions(Permissions.ConversationsReply, Permissions.SupportSettingsManage);
    }

    internal static async Task<IResult> ListAsync(HttpContext context, CannedResponseService service)
    {
        var items = await service.ListAsync(context.GetActor(), context.RequestAborted);
        return Results.Ok(new ApiResponse<IReadOnlyList<CannedResponseItem>>(items));
    }

    internal static async Task<IResult> CreateAsync(
        HttpContext context,
        CreateCannedResponseRequest? request,
        IValidator<CreateCannedResponseRequest> validator,
        CannedResponseService service)
    {
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        var created = await service.CreateAsync(
            context.GetActor(),
            validRequest.ShortcutText!,
            validRequest.TrimmedBody,
            validRequest.IsShared,
            context.RequestAborted);
        return Results.Json(
            new ApiResponse<CannedResponseItem>(created),
            statusCode: StatusCodes.Status201Created);
    }

    internal static async Task<IResult> UpdateAsync(
        string id,
        HttpContext context,
        UpdateCannedResponseRequest? request,
        IValidator<UpdateCannedResponseRequest> validator,
        CannedResponseService service)
    {
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        var updated = await service.UpdateAsync(
            context.GetActor(), id, validRequest.ShortcutText, validRequest.TrimmedBody, context.RequestAborted);
        return Results.Ok(new ApiResponse<CannedResponseItem>(updated));
    }

    internal static async Task<IResult> DeleteAsync(string id, HttpContext context, CannedResponseService service)
    {
        await service.DeleteAsync(context.GetActor(), id, context.RequestAborted);
        return Results.NoContent();
    }

    // Bộ lọc route đòi người gọi có ít nhất một quyền trong danh sách. Quyền chi tiết do service kiểm tiếp.
    private static TBuilder RequireAnyOfPermissions<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (filterContext, next) =>
        {
            var actor = filterContext.HttpContext.GetActor();
            // Khách chưa đăng nhập nhận 401 trước, giống `RequirePermission`.
            ActorAuthorization.EnsureAuthenticated(actor);
            if (!permissions.Any(actor.HasPermission))
            {
                throw new AppException(SupportDeskErrorCodes.Forbidden, 403, CannedResponseService.ForbiddenMessage);
            }

            return await next(filterContext);
        });
    }
}
