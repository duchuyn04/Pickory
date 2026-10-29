namespace Pickory.Modules.SupportDesk.Entities;

// Một lượt khách cần nhân viên. Phiên mở khi `ClosedAt` rỗng, và mỗi hội thoại có tối đa một phiên mở.
internal sealed class SupportSession
{
    public string Id { get; set; } = null!;
    public string ConversationId { get; set; } = string.Empty;
    public DateTime HandoffAt { get; set; }

    // Phiên có trong giờ hay không lúc mở: nằm trong khung giờ và có nhân viên sẵn sàng. Báo cáo không phải tính lại lịch cũ.
    public bool HandoffInHours { get; set; }

    // Hai cột định danh cho phép xóa dữ liệu khi module nhận `UserDeletedEvent`.
    public string? CustomerUserId { get; set; }
    public string AnonymousId { get; set; } = string.Empty;
    public string? StaffId { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime? FirstResponseAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    // Mã nhân viên đóng phiên, hoặc `auto` khi hệ thống tự đóng.
    public string? ClosedBy { get; set; }
    public int? Csat { get; set; }

    // Ghi chú nội bộ khi chuyển phiên. Khách không đọc được ghi chú này.
    public string? TransferNote { get; set; }

    // Lúc hệ thống nhắc lại phiên chờ, để mỗi phiên chỉ nhắc một lần.
    public DateTime? NudgedAt { get; set; }
}
