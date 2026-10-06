using System.Text.Json;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Shared.Privacy;

namespace Pickory.Modules.AiGateway.Gateway;

// Chạy một yêu cầu gọi tool và đổi kết quả thành text an toàn cho model.
public static class ToolCallExecutor
{
    public static async Task<string> ExecuteAsync(
        ToolCall toolCall,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        var tool = tools.FirstOrDefault(candidate => candidate.Name == toolCall.Name);
        // Báo rõ cho model khi tool không tồn tại. Model có thể chọn tool khác ở bước sau.
        if (tool is null)
        {
            return PiiRedactor.Redact($"Lỗi: Công cụ \"{toolCall.Name}\" không tồn tại.");
        }

        try
        {
            // Tool cục bộ nhận input gốc. Bản đã ẩn PII chỉ dùng cho lịch sử gửi provider.
            var result = await tool.InvokeAsync(toolCall.Input, cancellationToken);
            return result switch
            {
                ToolInputRejected rejected => PiiRedactor.Redact(
                    $"Lỗi tham số đầu vào cho công cụ \"{toolCall.Name}\": {rejected.Details}"),
                ToolCompleted completed => FormatCompletedValue(completed.Value),
                _ => throw new InvalidOperationException("Kết quả tool không được hỗ trợ."),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Không đưa exception vào prompt. Thông báo lỗi có thể chứa khóa API hoặc chuỗi kết nối.
            return PiiRedactor.Redact($"Lỗi: Thao tác thực thi công cụ \"{toolCall.Name}\" không thành công.");
        }
    }

    private static string FormatCompletedValue(object? value)
    {
        if (value is string text)
        {
            return PiiRedactor.Redact(text);
        }

        // Ẩn theo cây trước khi serialize. Thay regex trên chuỗi JSON làm hỏng cú pháp khi số đổi thành chuỗi.
        var json = JsonSerializer.SerializeToNode(value, PromptJson.Options);
        var redactedJson = PiiRedactor.RedactValue(json);
        var serialized = redactedJson?.ToJsonString(PromptJson.Options) ?? "null";
        return PiiRedactor.Redact(serialized);
    }
}
