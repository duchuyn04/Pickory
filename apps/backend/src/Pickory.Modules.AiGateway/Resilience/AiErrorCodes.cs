using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.AiGateway.Resilience;

// Đổi exception thành mã lỗi ghi vào `ai_usage_logs`.
public static class AiErrorCodes
{
    public static string From(Exception exception)
    {
        // Không dùng tên hoặc thông báo của exception. Nội dung đó có thể do SDK hoặc dữ liệu ngoài kiểm soát sinh ra.
        return exception switch
        {
            AiUnavailableException unavailable => unavailable.ReasonCode,
            AppException appException => appException.Code,
            ProviderException providerException => providerException.Code,
            _ => "AI_UNEXPECTED_ERROR",
        };
    }
}
