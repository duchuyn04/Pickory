using System.Text.Json;
using FluentValidation;

namespace Pickory.Modules.SupportDesk.Presence;

// Body của `PUT /api/admin/support/presence`. `Status` giữ nguyên JSON, vì kiểu `string` làm lỗi đọc body (500) khi client gửi số hoặc mảng.
internal sealed record SetPresenceRequest(JsonElement Status)
{
    // Chỉ chuỗi JSON mới là trạng thái hợp lệ. Thiếu trường, `null`, số hay mảng đều cho `null` để validator trả 400.
    public string? StatusText => Status.ValueKind == JsonValueKind.String ? Status.GetString() : null;
}

internal sealed class SetPresenceRequestValidator : AbstractValidator<SetPresenceRequest>
{
    public SetPresenceRequestValidator()
    {
        // Một luật đủ cho mọi trường hợp sai: thiếu, `null`, sai kiểu, hoặc giá trị lạ. Tên `Status` giữ khóa lỗi khớp với trường trong body.
        RuleFor(request => request.StatusText)
            .Must(statusText => StaffPresenceWire.TryParse(statusText, out _))
            .OverridePropertyName(nameof(SetPresenceRequest.Status))
            .WithMessage("Trạng thái phải là `available` hoặc `away`.");
    }
}
