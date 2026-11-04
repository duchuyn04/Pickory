using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.Modules.SupportDesk.Conversations;

// Response của `POST /api/support/conversations`. `ShowRetentionNotice` chỉ bằng `true` khi hội thoại vừa được tạo.
// `Session` là phiên gần nhất của hội thoại, để trang `/support` hiện trạng thái khi khách tải lại trang.
internal sealed record ConversationResponse(
    string Id, string Status, DateTime CreatedAt, bool ShowRetentionNotice, ConversationSessionResponse? Session)
{
    public static ConversationResponse From(
        SupportConversation conversation, SupportSession? latestSession, bool showRetentionNotice) => new(
        conversation.Id,
        // Tên enum viết thường khớp nhãn `open` và `closed`.
        conversation.Status.ToString().ToLowerInvariant(),
        conversation.CreatedAt,
        showRetentionNotice,
        // Hội thoại chưa có phiên nào thì khách thấy `session` rỗng.
        latestSession is null ? null : ConversationSessionResponse.From(latestSession));
}

// Phiên gần nhất của hội thoại trong response của khách. Không có mã nhân viên hay ghi chú nội bộ.
internal sealed record ConversationSessionResponse(string Id, string Status, int? CsatScore)
{
    public static ConversationSessionResponse From(SupportSession session) => new(
        session.Id,
        // Bảng phiên không có cột trạng thái. Trạng thái suy ra từ cột đóng và cột nhân viên giữ phiên.
        ReadStatus(session),
        session.Csat);

    private static string ReadStatus(SupportSession session)
    {
        // Phiên đã kết thúc thì trạng thái đóng quan trọng nhất, kể cả khi phiên có người nhận.
        if (session.ClosedAt is not null)
        {
            return "closed";
        }

        // Phiên có người nhận thì khách đang được hỗ trợ, chưa phải chờ.
        if (session.StaffId is not null)
        {
            return "assigned";
        }

        // Phiên chưa đóng và chưa có người nhận là phiên khách còn đang chờ.
        return "waiting";
    }
}

// Một tin trong lịch sử. Response không có `staffId`, vì tên nhân viên chưa được chốt.
internal sealed record MessageResponse(string Id, string Role, string Body, string? Action, DateTime CreatedAt)
{
    public static MessageResponse From(SupportMessage message) => new(
        message.Id,
        // Tên enum viết thường khớp nhãn Postgres `customer`, `staff`, `system`.
        message.Role.ToString().ToLowerInvariant(),
        message.Body,
        message.Action,
        message.CreatedAt);
}

internal sealed record MessagePageResponse(IReadOnlyList<MessageResponse> Items, string? NextCursor);
