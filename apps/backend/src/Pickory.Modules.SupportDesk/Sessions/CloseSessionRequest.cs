using System.Text.Json;
using FluentValidation;

namespace Pickory.Modules.SupportDesk.Sessions;

// Body của `PATCH /api/admin/support/sessions/{id}`. Dùng `JsonElement` để giá trị không phải chuỗi trả 400 thay vì 500.
internal sealed record CloseSessionRequest(JsonElement Status)
{
    // Lấy giá trị chuỗi nếu đúng kiểu chuỗi trong JSON.
    public string? StatusText => Status.ValueKind == JsonValueKind.String ? Status.GetString() : null;
}

internal sealed class CloseSessionRequestValidator : AbstractValidator<CloseSessionRequest>
{
    public CloseSessionRequestValidator()
    {
        // Trạng thái kết thúc phiên chỉ chấp nhận giá trị closed.
        RuleFor(request => request.StatusText)
            .Must(status => status == "closed")
            .OverridePropertyName("status")
            .WithMessage("Trạng thái phải là 'closed'.");
    }
}
