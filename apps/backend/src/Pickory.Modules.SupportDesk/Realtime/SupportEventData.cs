namespace Pickory.Modules.SupportDesk.Realtime;

// Dữ liệu của sự kiện `presence.changed`. Client dùng `StaffId` để cập nhật đúng người trong danh sách.
internal sealed record PresenceChangedEventData(string StaffId, string Status);

// Dữ liệu của sự kiện `heartbeat`. `At` cho client biết kết nối còn sống và giờ máy chủ.
internal sealed record HeartbeatEventData(DateTime At);
