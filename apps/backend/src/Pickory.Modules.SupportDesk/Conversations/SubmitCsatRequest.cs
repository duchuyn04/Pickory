using System.Text.Json;
using FluentValidation;

namespace Pickory.Modules.SupportDesk.Conversations;

// Body của `PUT /api/support/conversations/{id}/csat`.
// Giữ nguyên `JsonElement` để dữ liệu sai kiểu như chuỗi hoặc mảng đều trả lỗi 400 thay vì 500.
internal sealed record SubmitCsatRequest(JsonElement Score)
{
    // Chỉ chuyển số nguyên JSON thành giá trị điểm. Dữ liệu sai kiểu trả về `null` để validator báo lỗi 400.
    public int? ScoreValue =>
        Score.ValueKind == JsonValueKind.Number && Score.TryGetInt32(out var value)
            ? value
            : null;
}

internal sealed class SubmitCsatRequestValidator : AbstractValidator<SubmitCsatRequest>
{
    public SubmitCsatRequestValidator()
    {
        // Điểm đánh giá chỉ chấp nhận số nguyên từ 1 đến 5 theo hợp đồng API.
        RuleFor(request => request.ScoreValue)
            .Must(score => score is >= 1 and <= 5)
            .OverridePropertyName("score")
            .WithMessage("Điểm đánh giá phải là số nguyên từ 1 đến 5.");
    }
}
