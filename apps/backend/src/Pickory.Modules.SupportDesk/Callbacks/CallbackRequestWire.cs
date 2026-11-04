using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.Modules.SupportDesk.Callbacks;

// Chuyển đổi giữa enum lưu trong DB và chuỗi của hợp đồng API. Giá trị liệt kê viết thường theo quy ước chung của API.
internal static class CallbackRequestWire
{
    public static string ToWire(CallbackRequestStatus status) => status switch
    {
        CallbackRequestStatus.Pending => "pending",
        CallbackRequestStatus.Done => "done",
        CallbackRequestStatus.Unreachable => "unreachable",
        // Enum đóng nên trạng thái lạ là lỗi lập trình. Bỏ qua sẽ làm client nhận sai trạng thái.
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Trạng thái yêu cầu gọi lại không hợp lệ."),
    };

    // Chỉ nhận `done` hoặc `unreachable`. `pending` là trạng thái khởi tạo nên route `PATCH` không nhận giá trị này.
    public static bool TryParseHandleStatus(string? statusText, out CallbackRequestStatus status)
    {
        switch (statusText)
        {
            case "done":
                status = CallbackRequestStatus.Done;
                return true;
            case "unreachable":
                status = CallbackRequestStatus.Unreachable;
                return true;
            default:
                status = CallbackRequestStatus.Pending;
                return false;
        }
    }
}
