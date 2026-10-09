using Pickory.Shared.Exceptions;

namespace Pickory.Modules.AiGateway.Contracts.Errors;

// Vượt ngân sách tháng hoặc hạn mức chat theo ngày. Mã 429 cho biết hạn mức chặn yêu cầu, không phải nội dung sai.
public sealed class AiBudgetExceededException(string message = "Đã đạt giới hạn sử dụng AI, vui lòng thử lại sau.")
    : AppException("AI_BUDGET_EXCEEDED", 429, message);
