using FluentValidation;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Chat;

namespace Pickory.Modules.AiGateway.Contracts;

// Cửa duy nhất để module khác gọi AI, không phụ thuộc provider đang chạy.
// Module gọi tự bắt 3 lỗi trong `Errors/` và tự hiển thị phương án dự phòng.
public interface IAiGateway
{
    Task<ChatResult> ChatAsync(ChatRequest request, CallContext context, CancellationToken cancellationToken = default);

    IAsyncEnumerable<ChatChunk> ChatStreamAsync(
        ChatRequest request,
        CallContext context,
        CancellationToken cancellationToken = default);

    // Model trả JSON. Gateway đọc JSON thành `T`, rồi kiểm tra bằng `validator` nếu có.
    Task<T> ChatJsonAsync<T>(
        ChatRequest request,
        CallContext context,
        IValidator<T>? validator = null,
        CancellationToken cancellationToken = default);

    // Trả một vector 1536 chiều cho mỗi text, đúng thứ tự input.
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CallContext context,
        CancellationToken cancellationToken = default);
}
