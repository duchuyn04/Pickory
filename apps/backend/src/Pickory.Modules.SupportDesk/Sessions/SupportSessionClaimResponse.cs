namespace Pickory.Modules.SupportDesk.Sessions;

// Bản ghi dữ liệu phản hồi khi nhân viên nhận phiên hỗ trợ thành công (201 Created).
internal sealed record SupportSessionClaimResponse(
    string SessionId,
    string StaffId,
    DateTime ClaimedAt);
