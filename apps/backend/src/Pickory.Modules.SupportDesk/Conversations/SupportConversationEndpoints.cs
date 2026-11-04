using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.SupportDesk.Realtime;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.SupportDesk.Conversations;

// Bốn route chat của khách, không cần quyền vì khách vãng lai cũng chat được.
// Route ghi đi qua kiểm `Origin` của actor middleware.
internal static class SupportConversationEndpoints
{
    private const string ConversationsPath = "/api/support/conversations";
    private const string MessagesPath = "/api/support/conversations/{id}/messages";
    private const string EventsPath = "/api/support/conversations/{id}/events";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(ConversationsPath, OpenAsync);
        endpoints.MapGet(MessagesPath, ListMessagesAsync);
        endpoints.MapPost(MessagesPath, SendMessageAsync);
        // Luồng SSE không kết thúc, client đóng kết nối khi rời trang `/support`.
        endpoints.MapGet(EventsPath, OpenEventStreamAsync)
            // Khai báo kiểu nội dung để tài liệu OpenAPI mô tả đúng luồng SSE. Hàm trả `IResult` nên OpenAPI không tự suy ra.
            .Produces<string>(StatusCodes.Status200OK, "text/event-stream");
    }

    internal static async Task<IResult> OpenAsync(HttpContext context, SupportConversationService service)
    {
        // Hội thoại và tin là dữ liệu cá nhân, nên không cho trình duyệt hay proxy lưu bản sao.
        context.Response.Headers.CacheControl = "no-store";
        var actor = context.GetActor();
        ConversationOwnership.RejectStaff(actor);

        var (conversation, latestSession, created) = await service.OpenAsync(actor, context.RequestAborted);
        // Khách chỉ thấy thông báo lưu trữ ở lần tạo đầu, không thấy lại khi mở lại hội thoại cũ.
        var response = ConversationResponse.From(conversation, latestSession, showRetentionNotice: created);
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

    internal static async Task<IResult> OpenEventStreamAsync(
        string id,
        HttpContext context,
        SupportConversationService service,
        CustomerEventStreamer streamer)
    {
        var actor = context.GetActor();
        // Nhân viên gọi route phía khách nhận 403, giống ba route hội thoại của SD-15.
        ConversationOwnership.RejectStaff(actor);

        // Kiểm chủ trước khi mở response. Sau khi đã ghi header thì không đổi được response sang 404.
        await service.EnsureOwnerAsync(actor, id, context.RequestAborted);

        // Kết nối chỉ vào hub sau bước kiểm chủ ở trên, nên người lạ không chiếm được kênh.
        return new CustomerEventStreamResult(streamer.StreamAsync(id, context.RequestAborted));
    }
}
