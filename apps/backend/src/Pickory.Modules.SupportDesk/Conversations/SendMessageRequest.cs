using System.Text.Json;
using FluentValidation;

namespace Pickory.Modules.SupportDesk.Conversations;

// Body của `POST /api/support/conversations/{id}/messages`. `Body` giữ nguyên JSON, vì kiểu `string` làm lỗi đọc body (500) khi client gửi số hoặc mảng.
internal sealed record SendMessageRequest(JsonElement Body)
{
    public const int MaxBodyLength = 2000;

    // Chỉ chuỗi JSON mới là nội dung tin. Thiếu trường, `null`, số hay mảng đều cho `null` để validator trả 400.
    public string? BodyText => Body.ValueKind == JsonValueKind.String ? Body.GetString() : null;

    // Nội dung đã cắt khoảng trắng hai đầu. Đây là giá trị được lưu và là giá trị validator đo độ dài.
    public string TrimmedBody => BodyText?.Trim() ?? string.Empty;
}

internal sealed class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        // Một luật cho mọi trường hợp sai. Tên `Body` giữ khóa lỗi khớp với trường trong body.
        RuleFor(request => request.TrimmedBody)
            .Must(trimmedBody => trimmedBody.Length > 0 && trimmedBody.Length <= SendMessageRequest.MaxBodyLength)
            .OverridePropertyName(nameof(SendMessageRequest.Body))
            .WithMessage($"Tin nhắn phải có từ 1 đến {SendMessageRequest.MaxBodyLength} ký tự.");
    }
}
