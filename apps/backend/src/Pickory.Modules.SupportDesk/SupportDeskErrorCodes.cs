namespace Pickory.Modules.SupportDesk;

// Mã lỗi máy đọc được của module. Frontend dựa vào mã này để chọn thông báo, nên không đổi tên khi đã phát hành.
internal static class SupportDeskErrorCodes
{
    public const string Forbidden = "FORBIDDEN";
    public const string ConversationNotFound = "SUPPORT_CONVERSATION_NOT_FOUND";
    public const string ConversationClosed = "SUPPORT_CONVERSATION_CLOSED";
    public const string MessageRateLimited = "SUPPORT_MESSAGE_RATE_LIMITED";
    public const string CallbackLimitReached = "CALLBACK_LIMIT_REACHED";
    public const string CallbackRequestNotFound = "CALLBACK_REQUEST_NOT_FOUND";
    // Các mã lỗi cho nghiệp vụ hàng chờ và nhận phiên hỗ trợ.
    public const string SessionNotFound = "SUPPORT_SESSION_NOT_FOUND";
    public const string SessionClosed = "SUPPORT_SESSION_CLOSED";
    public const string SessionAlreadyClaimed = "SUPPORT_SESSION_ALREADY_CLAIMED";
}
