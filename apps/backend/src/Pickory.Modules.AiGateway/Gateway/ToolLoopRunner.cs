using System.Runtime.CompilerServices;
using System.Text;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Providers;
using Pickory.Modules.AiGateway.Resilience;
using Pickory.Modules.AiGateway.Usage;

namespace Pickory.Modules.AiGateway.Gateway;

// Chạy vòng lặp tool nhiều bước. Mỗi bước qua kiểm tra ngân sách, ẩn PII và ghi đúng một dòng log.
public sealed class ToolLoopRunner(StepServices steps)
{
    private static readonly TimeSpan ChatTimeout = TimeSpan.FromSeconds(30);

    public async Task<ChatResult> RunAsync(
        IChatProvider provider,
        ChatRequest request,
        CallContext context,
        CancellationToken cancellationToken)
    {
        var maxSteps = ResolveMaxSteps(request);
        var tools = request.Tools ?? [];
        var conversation = request.Messages.ToList();
        var redactedTools = MessageRedactor.RedactTools(request.Tools);
        var totalUsage = TokenUsage.Zero;

        for (var step = 1; ; step++)
        {
            await steps.AssertStepAllowedAsync(provider, context, cancellationToken);

            // Ẩn PII ở mỗi bước. Kết quả tool và câu trả lời vừa sinh cũng có thể chứa PII.
            var payload = new ProviderChatRequest(
                MessageRedactor.RedactMessages(conversation),
                redactedTools,
                request.Temperature,
                request.MaxTokens,
                Json: false);

            var startTimestamp = steps.Time.GetTimestamp();
            ChatResult stepResult;
            try
            {
                stepResult = await steps.Reliability.RunAsync(
                    provider.Name,
                    ChatTimeout,
                    token => provider.ChatAsync(payload, token),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                // Bước lỗi vẫn có một dòng log để đối soát sự cố.
                await steps.LogAsync(
                    provider,
                    context,
                    UsageStatus.Error,
                    AiErrorCodes.From(exception),
                    latencyMs: steps.ElapsedMilliseconds(startTimestamp));
                throw;
            }

            await steps.LogAsync(
                provider,
                context,
                UsageStatus.Ok,
                usage: stepResult.Usage,
                latencyMs: steps.ElapsedMilliseconds(startTimestamp));
            totalUsage = Add(totalUsage, stepResult.Usage);

            // Dừng khi model không gọi tool nữa hoặc đã tới số bước tối đa.
            var toolCalls = stepResult.ToolCalls;
            if (toolCalls is null || toolCalls.Count == 0 || step == maxSteps)
            {
                return new ChatResult(stepResult.Text, totalUsage, toolCalls);
            }

            // Model thấy lại yêu cầu gọi tool của mình và kết quả của từng tool ở bước sau.
            conversation.Add(new AssistantChatMessage(stepResult.Text, toolCalls));
            foreach (var toolCall in toolCalls)
            {
                var output = await ToolCallExecutor.ExecuteAsync(toolCall, tools, cancellationToken);
                conversation.Add(new ToolChatMessage(toolCall.Id, toolCall.Name, output));
            }
        }
    }

    // Bản stream của vòng lặp tool. Text của mỗi bước được phát ngay khi provider gửi tới.
    public async IAsyncEnumerable<ChatChunk> StreamAsync(
        IChatProvider provider,
        ChatRequest request,
        CallContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var maxSteps = ResolveMaxSteps(request);
        var tools = request.Tools ?? [];
        var conversation = request.Messages.ToList();
        var redactedTools = MessageRedactor.RedactTools(request.Tools);
        var totalUsage = TokenUsage.Zero;

        for (var step = 1; ; step++)
        {
            await steps.AssertStepAllowedAsync(provider, context, cancellationToken);

            var payload = new ProviderChatRequest(
                MessageRedactor.RedactMessages(conversation),
                redactedTools,
                request.Temperature,
                request.MaxTokens,
                Json: false);
            var startTimestamp = steps.Time.GetTimestamp();
            var stepText = new StringBuilder();
            var stepUsage = TokenUsage.Zero;
            var usageKnown = false;
            var stepLogged = false;
            IReadOnlyList<ToolCall>? stepToolCalls = null;

            var stream = steps.Reliability.StreamAsync(
                provider.Name,
                token => provider.StreamAsync(payload, token),
                cancellationToken);
            var enumerator = stream.GetAsyncEnumerator(cancellationToken);
            try
            {
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await enumerator.MoveNextAsync();
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                    {
                        // Stream đứt giữa chừng vẫn có một dòng log. Người gọi hủy thì khối `finally` ghi log hủy.
                        stepLogged = true;
                        await LogBrokenStreamAsync(provider, context, payload, stepText.ToString(), stepUsage, usageKnown, exception, startTimestamp);
                        throw;
                    }

                    if (!hasNext)
                    {
                        break;
                    }

                    switch (enumerator.Current)
                    {
                        case TextChatChunk textChunk:
                            stepText.Append(textChunk.Text);
                            yield return textChunk;
                            break;
                        case FinishChatChunk finishChunk:
                            stepUsage = finishChunk.Usage;
                            usageKnown = true;
                            stepToolCalls = finishChunk.ToolCalls;
                            break;
                    }
                }

                stepLogged = true;
                await steps.LogAsync(
                    provider,
                    context,
                    UsageStatus.Ok,
                    usage: stepUsage,
                    latencyMs: steps.ElapsedMilliseconds(startTimestamp));
            }
            finally
            {
                // Người đọc dừng sớm, ví dụ client ngắt kết nối. Bước này vẫn phải có một dòng log.
                if (!stepLogged)
                {
                    var partial = stepText.Length > 0 && !usageKnown;
                    await steps.LogAsync(
                        provider,
                        context,
                        UsageStatus.Error,
                        partial ? "STREAM_CANCELLED_PARTIAL_ESTIMATED" : "STREAM_CANCELLED",
                        partial ? StreamUsageEstimator.Estimate(payload, stepText.ToString()) : stepUsage,
                        steps.ElapsedMilliseconds(startTimestamp));
                }

                await enumerator.DisposeAsync();
            }

            totalUsage = Add(totalUsage, stepUsage);
            // Chunk kết thúc mang tổng token của mọi bước, giống `ChatAsync`.
            if (stepToolCalls is null || stepToolCalls.Count == 0 || step == maxSteps)
            {
                yield return new FinishChatChunk(totalUsage, stepToolCalls);
                yield break;
            }

            conversation.Add(new AssistantChatMessage(stepText.ToString(), stepToolCalls));
            foreach (var toolCall in stepToolCalls)
            {
                var output = await ToolCallExecutor.ExecuteAsync(toolCall, tools, cancellationToken);
                conversation.Add(new ToolChatMessage(toolCall.Id, toolCall.Name, output));
            }
        }
    }

    // Thiếu usage của provider thì dùng số byte làm ước tính. Mã lỗi có tiền tố để tách số liệu ước tính khỏi số liệu thật.
    private Task LogBrokenStreamAsync(
        IChatProvider provider,
        CallContext context,
        ProviderChatRequest payload,
        string receivedText,
        TokenUsage stepUsage,
        bool usageKnown,
        Exception exception,
        long startTimestamp)
    {
        var estimated = receivedText.Length > 0 && !usageKnown;
        var errorCode = AiErrorCodes.From(exception);
        return steps.LogAsync(
            provider,
            context,
            UsageStatus.Error,
            estimated ? $"STREAM_PARTIAL_ESTIMATED_{errorCode}" : errorCode,
            estimated ? StreamUsageEstimator.Estimate(payload, receivedText) : stepUsage,
            steps.ElapsedMilliseconds(startTimestamp));
    }

    // Có tool thì mặc định 5 bước. Không có tool thì chỉ cần 1 bước.
    public static int ResolveMaxSteps(ChatRequest request)
    {
        return request.MaxSteps ?? (request.Tools is { Count: > 0 } ? 5 : 1);
    }

    public static TokenUsage Add(TokenUsage left, TokenUsage right)
    {
        return new TokenUsage(
            left.InputTokens + right.InputTokens,
            left.OutputTokens + right.OutputTokens,
            left.CachedTokens + right.CachedTokens);
    }
}
