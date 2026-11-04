using System.Text.Json;
using FluentValidation;

namespace Pickory.Modules.SupportDesk.Callbacks;

// Body của `PATCH /api/admin/support/callback-requests/{id}`. `Status` giữ nguyên `JsonElement` để số hay mảng trả 400 thay vì 500.
internal sealed record UpdateCallbackRequest(JsonElement Status)
{
    // Chỉ chuỗi JSON mới là trạng thái hợp lệ. Thiếu trường, `null`, số hay mảng đều cho `null` để validator trả 400.
    public string? StatusText => Status.ValueKind == JsonValueKind.String ? Status.GetString() : null;
}

internal sealed class UpdateCallbackRequestValidator : AbstractValidator<UpdateCallbackRequest>
{
    public UpdateCallbackRequestValidator()
    {
        // Route đánh dấu kết quả gọi chỉ nhận `done` hoặc `unreachable`. Giá trị `pending` trả 400 kèm khóa `status`.
        RuleFor(request => request.StatusText)
            .Must(statusText => CallbackRequestWire.TryParseHandleStatus(statusText, out _))
            .OverridePropertyName("status")
            .WithMessage("Trạng thái phải là `done` hoặc `unreachable`.");
    }
}
