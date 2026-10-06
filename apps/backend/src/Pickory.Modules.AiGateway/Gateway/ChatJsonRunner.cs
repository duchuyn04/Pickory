using System.Text.Json;
using System.Text.Json.Nodes;
using FluentValidation;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Modules.AiGateway.Providers;
using Pickory.Modules.AiGateway.Resilience;
using Pickory.Modules.AiGateway.Usage;
using Pickory.Shared.Privacy;

namespace Pickory.Modules.AiGateway.Gateway;

// Gọi model ở chế độ JSON, đọc kết quả thành kiểu C#, thử lại đúng một lần khi sai schema.
public sealed class ChatJsonRunner(StepServices steps)
{
    private const int MaxAttempts = 2;
    private static readonly TimeSpan ChatTimeout = TimeSpan.FromSeconds(30);

    public async Task<T> RunAsync<T>(
        IChatProvider provider,
        ChatRequest request,
        CallContext context,
        IValidator<T>? validator,
        CancellationToken cancellationToken)
    {
        var conversation = MessageRedactor.RedactMessages(request.Messages);
        // DeepSeek yêu cầu từ `json` trong prompt khi bật chế độ JSON.
        var hasJsonInstruction = conversation.Any(message =>
            message is SystemChatMessage && message.Content.Contains("json", StringComparison.OrdinalIgnoreCase));
        if (!hasJsonInstruction)
        {
            conversation.Insert(0, new SystemChatMessage("Respond with a valid JSON object matching the requested schema."));
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await steps.AssertStepAllowedAsync(provider, context, cancellationToken);

            var payload = new ProviderChatRequest(conversation.ToList(), null, request.Temperature, request.MaxTokens, Json: true);
            var startTimestamp = steps.Time.GetTimestamp();
            ChatResult response;
            try
            {
                response = await steps.Reliability.RunAsync(
                    provider.Name,
                    ChatTimeout,
                    token => provider.ChatAsync(payload, token),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                await steps.LogAsync(
                    provider,
                    context,
                    UsageStatus.Error,
                    AiErrorCodes.From(exception),
                    latencyMs: steps.ElapsedMilliseconds(startTimestamp));
                throw;
            }

            var latencyMs = steps.ElapsedMilliseconds(startTimestamp);
            var (read, failureReason) = ReadResponse(response.Text, validator);
            // Chỉ ghi `ok` khi đúng schema. Dashboard không bỏ sót lỗi output của model.
            await steps.LogAsync(
                provider,
                context,
                read.Succeeded ? UsageStatus.Ok : UsageStatus.Error,
                read.Succeeded ? null : "AI_INVALID_OUTPUT",
                response.Usage,
                latencyMs);

            if (read.Succeeded)
            {
                return read.Value!;
            }

            if (attempt == MaxAttempts)
            {
                break;
            }

            // Lần thử thứ hai kèm lý do lỗi. Output sai cũng được ẩn PII trước khi gửi lại.
            conversation.Add(new AssistantChatMessage(PiiRedactor.Redact(response.Text)));
            conversation.Add(new UserChatMessage(PiiRedactor.Redact(
                $"Dữ liệu trả về chưa đúng định dạng. Lý do: {failureReason}. Vui lòng sửa lại và trả về đúng đối tượng JSON hợp lệ theo đúng cấu trúc yêu cầu.")));
        }

        throw new AiInvalidOutputException();
    }

    private static (AiJsonReadResult<T> Read, string FailureReason) ReadResponse<T>(string text, IValidator<T>? validator)
    {
        JsonNode? json;
        try
        {
            json = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return (new AiJsonReadResult<T>(false, default, null), "Phản hồi không phải là chuỗi JSON hợp lệ.");
        }

        var read = AiJsonReader.Read(json, validator);
        // Lý do chỉ gồm đường dẫn trường và mô tả lỗi, không gồm giá trị model đã gửi.
        var failureReason = read.Succeeded ? string.Empty : $"Dữ liệu không khớp schema: {PiiRedactor.Redact(read.Details ?? string.Empty)}";
        return (read, failureReason);
    }
}
