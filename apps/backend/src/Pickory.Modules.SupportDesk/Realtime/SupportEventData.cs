namespace Pickory.Modules.SupportDesk.Realtime;

// Dữ liệu của sự kiện `presence.changed`. Client dùng `StaffId` để cập nhật đúng người trong danh sách.
internal sealed record PresenceChangedEventData(string StaffId, string Status);

// Dữ liệu của sự kiện `heartbeat`. `At` cho client biết kết nối còn sống và giờ máy chủ.
internal sealed record HeartbeatEventData(DateTime At);

// Dữ liệu của sự kiện phiên gửi cho nhân viên. Tên sự kiện cho biết chuyện gì xảy ra, nên dữ liệu chỉ có mã phiên.
// Client nhận sự kiện thì đọc lại danh sách hàng chờ, không dựng dòng từ dữ liệu này.
internal sealed record SessionEventData(string SessionId);
