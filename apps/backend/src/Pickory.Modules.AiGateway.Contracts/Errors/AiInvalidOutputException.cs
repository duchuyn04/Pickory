using Pickory.Shared.Exceptions;

namespace Pickory.Modules.AiGateway.Contracts.Errors;

// Provider trả JSON sai schema sau 2 lần thử. Đây là lỗi phía provider, nên API trả 502 thay vì 400.
public sealed class AiInvalidOutputException()
    : AppException("AI_INVALID_OUTPUT", 502, "Dịch vụ AI trả về dữ liệu không hợp lệ.");
