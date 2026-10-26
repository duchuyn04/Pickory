using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.SupportDesk.Conversations;

// Ba route chat của khách, không cần quyền vì khách vãng lai cũng chat được.
// Route ghi đi qua kiểm `Origin` của actor middleware.
internal static class SupportConversationEndpoints
{
    private const string ConversationsPath = "/api/support/conversations";
    private const string MessagesPath = "/api/support/conversations/{id}/messages";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(ConversationsPath, OpenAsync);
        endpoints.MapGet(MessagesPath, ListMessagesAsync);
        endpoints.MapPost(MessagesPath, SendMessageAsync);
    }

    internal static async Task<IResult> OpenAsync(HttpContext context, SupportConversationService service)
    {
        // Hội thoại và tin là dữ liệu cá nhân, nên không cho trình duyệt hay proxy lưu bản sao.
        context.Response.Headers.CacheControl = "no-store";
        var actor = context.GetActor();
        ConversationOwnership.RejectStaff(actor);

        var (conversation, created) = await service.OpenAsync(actor, context.RequestAborted);
        var response = new ConversationResponse(
            conversation.Id,
            // Tên enum viết thường khớp nhãn `open` và `closed`.
            conversation.Status.ToString().ToLowerInvariant(),
            conversation.CreatedAt,
            // Khách chỉ thấy thông báo lưu trữ ở lần tạo đầu, không thấy lại khi mở lại hội thoại cũ.
            ShowRetentionNotice: created);
        return Results.Json(
            new ApiResponse<ConversationResponse>(response),
            statusCode: created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }

    internal static async Task<IResult> ListMessagesAsync(
        string id, string? cursor, string? limit, HttpContext context, SupportConversationService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        var actor = context.GetActor();
        ConversationOwnership.RejectStaff(actor);

        var page = await service.ListMessagesAsync(actor, id, cursor, limit, context.RequestAborted);
        return Results.Ok(new ApiResponse<MessagePageResponse>(page));
    }

    internal static async Task<IResult> SendMessageAsync(
        string id,
        HttpContext context,
        SendMessageRequest? request,
        IValidator<SendMessageRequest> validator,
        SupportConversationService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        var actor = context.GetActor();
        // `RejectStaff` chạy trước validator, nên nhân viên gửi JSON hợp lệ nhưng sai luật vẫn nhận 403. JSON hỏng thì framework trả 400 trước bước này.
        ConversationOwnership.RejectStaff(actor);

        // Kiểm body trước khi chạm DB. Body sai không đọc hội thoại và không tính lượt giới hạn.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        var message = await service.SendMessageAsync(actor, id, validRequest.TrimmedBody, context.RequestAborted);
        return Results.Json(new ApiResponse<MessageResponse>(message), statusCode: StatusCodes.Status201Created);
    }
}
