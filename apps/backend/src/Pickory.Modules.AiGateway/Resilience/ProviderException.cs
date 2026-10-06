namespace Pickory.Modules.AiGateway.Resilience;

// Lỗi đã chuẩn hóa của provider. Lớp này không giữ exception gốc vì SDK có thể đính kèm khóa hoặc nội dung request.
public sealed class ProviderException(int? status, bool retryable = false, string? code = null)
    : Exception("AI provider request failed")
{
    public int? Status { get; } = status;

    // Chỉ lỗi 429, 5xx và lỗi mạng được thử lại. Lỗi 4xx khác lặp lại cũng không thành công.
    public bool Retryable { get; } = retryable;

    // Mã ổn định ghi vào `ai_usage_logs`, ví dụ `PROVIDER_HTTP_503` hoặc `PROVIDER_TIMEOUT`.
    public string Code { get; } = code ?? (status is null ? "PROVIDER_NETWORK_ERROR" : $"PROVIDER_HTTP_{status}");
}
