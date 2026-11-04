using System.Text.Json;
using FluentValidation;
using Pickory.Shared.Validation;

namespace Pickory.Modules.SupportDesk.Callbacks;

// Body của `POST /api/support/callback-requests`. Mọi trường giữ nguyên `JsonElement`,
// vì kiểu chuỗi làm lỗi đọc body (500) khi client gửi số hoặc mảng.
internal sealed record CreateCallbackRequest(JsonElement ConversationId, JsonElement Phone, JsonElement Note)
{
    public const int MaxNoteLength = 500;

    // Chỉ chuỗi JSON mới là giá trị nhập. Thiếu trường, `null`, số hay mảng đều cho `null` để validator trả 400.
    public string? ConversationIdText => ReadString(ConversationId);
    public string? PhoneText => ReadString(Phone);
    public string? NoteText => ReadString(Note);

    // Ghi chú rỗng hoặc bỏ qua đều hợp lệ. Số, boolean, mảng hay object là sai kiểu dữ liệu.
    public bool HasAllowedNoteKind =>
        Note.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.String;

    // Ghi chú cắt khoảng trắng hai đầu. Chuỗi rỗng sau khi cắt lưu thành `null` thay vì chuỗi rỗng.
    public string? TrimmedNote
    {
        get
        {
            var text = NoteText?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
    }

    // Số chuẩn hóa dạng `0xxxxxxxxx` theo luật của `ContactFieldRules`. Validator chạy trước, nên kết quả khác rỗng.
    public string NormalizedPhone => ContactFieldRules.NormalizePhone(PhoneText) ?? string.Empty;

    private static string? ReadString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;
}

internal sealed class CreateCallbackRequestValidator : AbstractValidator<CreateCallbackRequest>
{
    public CreateCallbackRequestValidator()
    {
        // Một luật cho mọi trường hợp sai. Tên ghi đè giữ khóa lỗi khớp với trường trong body.
        RuleFor(request => request.ConversationIdText)
            .Must(text => !string.IsNullOrWhiteSpace(text))
            .OverridePropertyName("conversationId")
            .WithMessage("Mã hội thoại là bắt buộc.");
        RuleFor(request => request.PhoneText)
            .Cascade(CascadeMode.Stop)
            // `GetPhoneError` coi số trống là hợp lệ, nên luật này phải tự chặn số trống.
            .Must(phone => !string.IsNullOrWhiteSpace(phone))
            .WithMessage("Số điện thoại là bắt buộc.")
            .Must(phone => ContactFieldRules.GetPhoneError(phone) is null)
            .WithMessage(ContactFieldRules.PhoneInvalidMessage)
            .OverridePropertyName("phone");
        RuleFor(request => request.NoteText)
            .Cascade(CascadeMode.Stop)
            // Chặn kiểu JSON sai trước khi đo độ dài, vì `NoteText` trả `null` cho cả số và mảng.
            .Must((request, _) => request.HasAllowedNoteKind)
            .WithMessage("Ghi chú phải là chuỗi hoặc rỗng.")
            .Must(note => note is null || note.Trim().Length <= CreateCallbackRequest.MaxNoteLength)
            .WithMessage($"Ghi chú tối đa {CreateCallbackRequest.MaxNoteLength} ký tự.")
            .OverridePropertyName("note");
    }
}
