using System.Globalization;

namespace Pickory.Modules.SupportDesk.Sessions;

// Nội dung tin hệ thống của phiên chờ. Chuỗi nằm một chỗ để lời báo cho khách không lệch giữa các lần mở phiên.
internal static class SupportSystemMessages
{
    // Mã form mà trang `/support` mở kèm tin hệ thống. Giá trị khớp cột `action` trong hợp đồng API.
    public const string CallbackAction = "callback_form";
    // Mã form CSAT mà trang hỗ trợ mở kèm tin hệ thống khi kết thúc phiên.
    public const string CsatAction = "csat_form";

    // Khách chờ trong giờ thì chỉ cần biết thời gian chờ dự kiến, không cần để lại số điện thoại.
    public static string WaitingInHours(int estimateMinutes) =>
        $"Đã chuyển cho nhân viên CSKH. Thời gian chờ dự kiến khoảng {estimateMinutes} phút.";

    // Ngoài giờ hoặc không ai sẵn sàng thì khách để lại số điện thoại. Không có nhân viên nào trả lời ngay được.
    public static string OutsideHours(TimeOnly opensAt, TimeOnly closesAt) =>
        $"Hiện ngoài giờ hỗ trợ ({FormatClock(opensAt)} đến {FormatClock(closesAt)}). Bạn để lại SĐT, CSKH sẽ gọi lại.";

    // Phiên chờ quá lâu trong giờ thì khách được chọn để lại số điện thoại thay vì chờ tiếp.
    public const string WaitReminder = "Nhân viên CSKH đang bận. Bạn có thể để lại SĐT để CSKH gọi lại.";
    // Thông báo cho khách khi nhân viên CSKH nhận phiên hỗ trợ và chuẩn bị trả lời.
    public const string SessionClaimed = "Nhân viên CSKH đã nhận hội thoại và sẽ trả lời bạn ngay.";
    // Thông báo cho khách khi phiên hỗ trợ kết thúc để mời chấm điểm dịch vụ.
    public const string SessionClosed = "Phiên hỗ trợ đã kết thúc. Bạn chấm điểm giúp CSKH nhé.";

    // Giờ viết không có số 0 đứng đầu, ví dụ `8:00` và `21:00`, giống mẫu câu trong spec.
    private static string FormatClock(TimeOnly time) => time.ToString("H:mm", CultureInfo.InvariantCulture);
}
