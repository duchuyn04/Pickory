using System.Text.Json;
using FluentValidation;

namespace Pickory.Modules.SupportDesk.Sessions;

// Dữ liệu yêu cầu chuyển phiên hỗ trợ CSKH cho đồng nghiệp.
internal sealed record TransferSessionRequest(JsonElement StaffId, JsonElement Note)
{
    public const int MaxNoteLength = 500;

    // Lấy chuỗi mã nhân viên nếu đúng kiểu chuỗi trong JSON.
    public string? StaffIdText => StaffId.ValueKind == JsonValueKind.String ? StaffId.GetString() : null;

    // Cắt khoảng trắng hai đầu của mã nhân viên.
    public string TrimmedStaffId => StaffIdText?.Trim() ?? string.Empty;

    // Lấy nội dung ghi chú nếu đúng kiểu chuỗi trong JSON.
    public string? NoteText => Note.ValueKind == JsonValueKind.String ? Note.GetString() : null;

    // Kiểm tra kiểu dữ liệu ghi chú có phải chuỗi hoặc null hoặc không gửi hay không.
    public bool IsNoteValidType => Note.ValueKind is JsonValueKind.String or JsonValueKind.Null or JsonValueKind.Undefined;

    // Nội dung ghi chú đã cắt khoảng trắng, hoặc null nếu không có nội dung.
    public string? TrimmedNote => string.IsNullOrWhiteSpace(NoteText) ? null : NoteText.Trim();
}

internal sealed class TransferSessionRequestValidator : AbstractValidator<TransferSessionRequest>
{
    public TransferSessionRequestValidator()
    {
        // Mã nhân viên người nhận là bắt buộc và không được để trống.
        RuleFor(request => request.TrimmedStaffId)
            .Must(staffId => !string.IsNullOrEmpty(staffId))
            .OverridePropertyName("staffId")
            .WithMessage("Mã nhân viên người nhận là bắt buộc.");

        // Kiểu dữ liệu của ghi chú phải là chuỗi hoặc null.
        RuleFor(request => request.IsNoteValidType)
            .Equal(true)
            .OverridePropertyName("note")
            .WithMessage("Ghi chú phải là chuỗi hoặc null.");

        // Ghi chú không được dài quá 500 ký tự.
        RuleFor(request => request.NoteText)
            .Must(note => note is null || note.Length <= TransferSessionRequest.MaxNoteLength)
            .OverridePropertyName("note")
            .WithMessage($"Ghi chú tối đa {TransferSessionRequest.MaxNoteLength} ký tự.");
    }
}
