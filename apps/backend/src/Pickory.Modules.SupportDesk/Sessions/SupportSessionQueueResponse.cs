namespace Pickory.Modules.SupportDesk.Sessions;

// Bản ghi đại diện một phiên hỗ trợ trong hàng chờ hoặc danh sách của nhân viên.
internal sealed record SupportSessionQueueItemResponse(
    string Id,
    string ConversationId,
    string Preview,
    long WaitingSeconds,
    DateTime HandoffAt,
    DateTime? ClaimedAt,
    string? TransferNote);
