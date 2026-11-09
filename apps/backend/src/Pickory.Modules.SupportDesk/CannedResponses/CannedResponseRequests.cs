using System.Text.Json;
using FluentValidation;

namespace Pickory.Modules.SupportDesk.CannedResponses;

// Body của `POST /api/admin/support/canned-responses`. Mọi trường giữ nguyên `JsonElement`,
// vì kiểu mạnh làm lỗi đọc body (500) khi client gửi sai kiểu JSON.
internal sealed record CreateCannedResponseRequest(JsonElement Shortcut, JsonElement Body, JsonElement Shared)
{
    public string? ShortcutText => CannedRequestJson.ReadString(Shortcut);
    public string? BodyText => CannedRequestJson.ReadString(Body);

    // Thiếu `shared` hoặc `null` nghĩa là mẫu cá nhân. Giá trị không phải boolean làm validator trả 400.
    public bool HasAllowedSharedKind =>
        Shared.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False;

    public bool IsShared => Shared.ValueKind == JsonValueKind.True;

    // Thân mẫu lưu sau khi cắt khoảng trắng hai đầu. Validator chạy trước nên chuỗi này không rỗng.
    public string TrimmedBody => BodyText?.Trim() ?? string.Empty;
}

// Body của `PATCH /api/admin/support/canned-responses/{id}`, chỉ nhận `shortcut` và `body`.
// Cờ `shared` không đổi được sau khi tạo, nên bộ đọc bỏ qua mọi trường khác.
internal sealed record UpdateCannedResponseRequest(JsonElement Shortcut, JsonElement Body)
{
    public string? ShortcutText => CannedRequestJson.ReadString(Shortcut);
    public string? BodyText => CannedRequestJson.ReadString(Body);

    // Trường có mặt khi không phải `Undefined`. `null` tính là có mặt để validator báo sai kiểu thay vì bỏ qua.
    public bool HasShortcut => Shortcut.ValueKind != JsonValueKind.Undefined;
    public bool HasBody => Body.ValueKind != JsonValueKind.Undefined;

    public string? TrimmedBody => BodyText?.Trim();
}

internal static class CannedRequestJson
{
    // Chỉ chuỗi JSON mới là giá trị nhập. Thiếu trường, `null`, số hay mảng đều cho `null`.
    public static string? ReadString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;
}

internal sealed class CreateCannedResponseRequestValidator : AbstractValidator<CreateCannedResponseRequest>
{
    public CreateCannedResponseRequestValidator()
    {
        RuleFor(request => request.ShortcutText)
            .Must(CannedShortcutRules.IsValidShortcut)
            .OverridePropertyName("shortcut")
            .WithMessage(CannedValidationMessages.Shortcut);
        RuleFor(request => request.BodyText)
            .Must(CannedShortcutRules.IsValidBody)
            .OverridePropertyName("body")
            .WithMessage(CannedValidationMessages.Body);
        RuleFor(request => request.HasAllowedSharedKind)
            .Equal(true)
            .OverridePropertyName("shared")
            .WithMessage("Trường `shared` phải là true hoặc false.");
    }
}

internal sealed class UpdateCannedResponseRequestValidator : AbstractValidator<UpdateCannedResponseRequest>
{
    public UpdateCannedResponseRequestValidator()
    {
        // Body rỗng hoặc chỉ có trường lạ không đổi gì, nên trả 400 thay vì 200 vô nghĩa.
        RuleFor(request => request.HasShortcut || request.HasBody)
            .Equal(true)
            .OverridePropertyName("shortcut")
            .WithMessage("Cần gửi ít nhất `shortcut` hoặc `body`.");
        // Trường đã gửi phải hợp lệ như lúc tạo. Trường không gửi thì giữ nguyên giá trị cũ.
        RuleFor(request => request.ShortcutText)
            .Must(CannedShortcutRules.IsValidShortcut)
            .When(request => request.HasShortcut)
            .OverridePropertyName("shortcut")
            .WithMessage(CannedValidationMessages.Shortcut);
        RuleFor(request => request.BodyText)
            .Must(CannedShortcutRules.IsValidBody)
            .When(request => request.HasBody)
            .OverridePropertyName("body")
            .WithMessage(CannedValidationMessages.Body);
    }
}

internal static class CannedValidationMessages
{
    public const string Shortcut = "Phím tắt gồm 1 đến 32 ký tự a-z, 0-9, dấu gạch ngang hoặc gạch dưới.";
    public const string Body = "Nội dung mẫu phải có từ 1 đến 2000 ký tự.";
}
