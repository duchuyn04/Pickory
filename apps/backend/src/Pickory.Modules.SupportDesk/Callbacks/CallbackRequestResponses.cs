using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.Modules.SupportDesk.Callbacks;

// Response 201 của route khách. Hợp đồng chỉ có ba khóa này.
internal sealed record CallbackRequestCreatedResponse(string Id, string Status, DateTime CreatedAt)
{
    public static CallbackRequestCreatedResponse From(CallbackRequest request) =>
        new(request.Id, CallbackRequestWire.ToWire(request.Status), request.CreatedAt);
}

// Response của hai route quản trị. Không có `handledBy`, `anonymousId` hay `customerUserId` theo hợp đồng.
internal sealed record CallbackRequestResponse(
    string Id,
    string? ConversationId,
    string? Phone,
    string? Note,
    string Status,
    DateTime CreatedAt,
    DateTime? HandledAt)
{
    // Trường rỗng vẫn có mặt với giá trị `null`. Bộ tuần tự hóa không bỏ khóa của record này.
    public static CallbackRequestResponse From(CallbackRequest request) =>
        new(
            request.Id,
            request.ConversationId,
            request.Phone,
            request.Note,
            CallbackRequestWire.ToWire(request.Status),
            request.CreatedAt,
            request.HandledAt);
}
