namespace Pickory.Modules.SupportDesk.Realtime;

// Dữ liệu sự kiện `message.created`. Không có `staffId` hay ghi chú nội bộ, vì kênh này dành cho khách.
internal sealed record CustomerMessageCreatedEventData(
    string Id, string Role, string Body, string? Action, DateTime CreatedAt);

// Dữ liệu của ba sự kiện trạng thái phiên. Trạng thái nằm trong tên sự kiện, nên khách chỉ cần mã phiên.
internal sealed record CustomerSessionEventData(string SessionId);
