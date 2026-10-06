using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.AiGateway.Gateway;

// Kiểm tra hợp đồng của module gọi trước khi gửi bất kỳ request nào ra ngoài.
public static class CallValidator
{
    public static void ValidateContext(CallContext context)
    {
        // Chat phải có định danh ổn định. Thiếu định danh thì hạn mức ngày bị bỏ qua.
        if (context.Feature == AiFeature.ShoppingAssistantChat && string.IsNullOrWhiteSpace(context.ActorKey))
        {
            throw new AppException("INVALID_ACTOR_KEY", 400, "actorKey là bắt buộc cho tính năng trò chuyện.");
        }

        if (context.ActorKey is null)
        {
            return;
        }

        // Actor có tiền tố sai hoặc định danh rỗng làm hạn mức và log đối soát sai người.
        var key = context.ActorKey.Trim();
        var hasKnownPrefix = key.StartsWith("user:", StringComparison.Ordinal) || key.StartsWith("anon:", StringComparison.Ordinal);
        if (!hasKnownPrefix || key[(key.IndexOf(':') + 1)..].Trim().Length == 0)
        {
            throw new AppException(
                "INVALID_ACTOR_KEY",
                400,
                "actorKey sai định dạng: cần tiền tố user: hoặc anon: và định danh không rỗng.");
        }
    }

    public static void ValidateRequest(ChatRequest request)
    {
        // Số bước không dương làm vòng lặp tool không chạy bước nào nhưng vẫn giữ lượt chat.
        if (request.MaxSteps is <= 0)
        {
            throw new AppException("INVALID_MAX_STEPS", 400, "maxSteps phải là số nguyên dương.");
        }
    }
}
