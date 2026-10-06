using Pickory.Shared.Exceptions;

namespace Pickory.Modules.AiGateway.Contracts.Errors;

// Provider lỗi, hết thời gian chờ hoặc mạch đang mở. Module gọi chọn phương án dự phòng theo mã lỗi ổn định.
public sealed class AiUnavailableException(string reasonCode = "AI_UNAVAILABLE")
    : AppException("AI_UNAVAILABLE", 503, "Dịch vụ AI tạm thời không khả dụng, vui lòng thử lại sau.")
{
    // Mã lý do ghi vào `ai_usage_logs`. Mã này không chứa thông báo của SDK, nên không lộ khóa.
    public string ReasonCode { get; } = reasonCode;
}
