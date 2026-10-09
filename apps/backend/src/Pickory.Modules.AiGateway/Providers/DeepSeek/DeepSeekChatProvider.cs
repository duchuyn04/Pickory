using System.ClientModel;
using System.ClientModel.Primitives;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Resilience;
using AiChatResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat;

namespace Pickory.Modules.AiGateway.Providers.DeepSeek;

// Adapter chat cho DeepSeek qua API tương thích OpenAI. Adapter chỉ chạy một bước và không chạy tool.
public sealed class DeepSeekChatProvider : IChatProvider
{
    private readonly IChatClient _chatClient;

    public DeepSeekChatProvider(string apiKey, string baseUrl, string model, HttpClient httpClient)
    {
        // Không có model mặc định. Người vận hành phải chọn ID model đã có bảng giá.
        var modelName = model.Trim();
        if (modelName.Length == 0)
        {
            throw new ProviderException(400, retryable: false);
        }

        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(baseUrl),
            Transport = new HttpClientPipelineTransport(httpClient),
            // SDK không tự thử lại. Gateway quyết định số lần gọi ra ngoài ở một chỗ.
            RetryPolicy = new ClientRetryPolicy(0),
        };
        _chatClient = new ChatClient(modelName, new ApiKeyCredential(apiKey), options).AsIChatClient();
        Model = modelName;
    }

    public string Name => "deepseek";
    public string Model { get; }

    public async Task<Contracts.Chat.ChatResult> ChatAsync(ProviderChatRequest request, CancellationToken cancellationToken)
    {
        ChatResponse response;
        try
        {
            response = await _chatClient.GetResponseAsync(
                DeepSeekMessageConverter.ToAiMessages(request.Messages),
                CreateOptions(request),
                cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw ProviderErrorMapper.Map(exception);
        }

        var toolCalls = response.Messages
            .SelectMany(message => message.Contents)
            .OfType<FunctionCallContent>()
            .Select(DeepSeekMessageConverter.ToToolCall)
            .ToList();
        return new Contracts.Chat.ChatResult(
            response.Text,
            DeepSeekUsageReader.Read(response.Usage, response.RawRepresentation),
            toolCalls.Count > 0 ? toolCalls : null);
    }

    public async IAsyncEnumerable<ChatChunk> StreamAsync(
        ProviderChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        TokenUsage? usage = null;
        var toolCalls = new List<ToolCall>();
        var updates = _chatClient.GetStreamingResponseAsync(
            DeepSeekMessageConverter.ToAiMessages(request.Messages),
            CreateOptions(request),
            cancellationToken);
        var enumerator = updates.GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    throw ProviderErrorMapper.Map(exception);
                }

                if (!hasNext)
                {
                    break;
                }

                var update = enumerator.Current;
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextContent { Text.Length: > 0 } text:
                            yield return new TextChatChunk(text.Text);
                            break;
                        case UsageContent usageContent:
                            usage = DeepSeekUsageReader.Read(usageContent.Details, update.RawRepresentation);
                            break;
                        case FunctionCallContent call:
                            toolCalls.Add(DeepSeekMessageConverter.ToToolCall(call));
                            break;
                    }
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        // Thiếu usage thì không biết chi phí. Gateway không được ghi chi phí bằng 0 cho bước này.
        if (usage is null)
        {
            throw new ProviderException(null, retryable: false);
        }

        yield return new FinishChatChunk(usage, toolCalls.Count > 0 ? toolCalls : null);
    }

    private static ChatOptions CreateOptions(ProviderChatRequest request)
    {
        return new ChatOptions
        {
            Temperature = request.Temperature is { } temperature ? (float)temperature : null,
            ResponseFormat = request.Json ? AiChatResponseFormat.Json : null,
            Tools = request.Tools?.Select(tool => (AITool)ToolSchemaBuilder.ToDeclaration(tool)).ToList(),
            RawRepresentationFactory = _ => CreateRawOptions(request.MaxTokens),
        };
    }

    // `Patch` của SDK còn ở dạng thử nghiệm. Đây là cách duy nhất để gửi trường riêng của DeepSeek.
#pragma warning disable SCME0001
    private static ChatCompletionOptions CreateRawOptions(int? maxTokens)
    {
        var options = new ChatCompletionOptions();
        // Tắt chế độ thinking. Chế độ này làm mất `reasoning_content` giữa các bước tool do gateway điều khiển.
        options.Patch.Set("$.thinking"u8, BinaryData.FromString("""{"type":"disabled"}"""));
        // DeepSeek đọc giới hạn output từ `max_tokens`. Trường `max_completion_tokens` của SDK OpenAI không áp dụng.
        if (maxTokens is { } limit)
        {
            options.Patch.Set("$.max_tokens"u8, BinaryData.FromString(limit.ToString(CultureInfo.InvariantCulture)));
        }

        return options;
    }
#pragma warning restore SCME0001
}
