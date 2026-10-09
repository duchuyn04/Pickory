using System.Runtime.CompilerServices;
using FluentValidation;
using Pickory.Modules.AiGateway.Budget;
using Pickory.Modules.AiGateway.Contracts;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Modules.AiGateway.Embeddings;
using Pickory.Modules.AiGateway.Pricing;
using Pickory.Modules.AiGateway.Providers;
using Pickory.Modules.AiGateway.RateLimiting;
using Pickory.Modules.AiGateway.Resilience;
using Pickory.Modules.AiGateway.Settings;
using Pickory.Modules.AiGateway.Usage;

namespace Pickory.Modules.AiGateway.Gateway;

// Cửa duy nhất gọi AI. Lớp này kiểm tra hợp đồng, chọn provider, giữ lượt chat rồi giao từng bước cho runner.
public sealed class AiGatewayService : IAiGateway
{
    private readonly AiProviderSet _providers;
    private readonly ChatRateLimiter _rateLimiter;
    private readonly StepServices _steps;
    private readonly ToolLoopRunner _toolLoop;
    private readonly ChatJsonRunner _chatJson;
    private readonly EmbeddingCache _embeddingCache;

    public AiGatewayService(
        AiProviderSet providers,
        ProviderReliability reliability,
        ChatRateLimiter rateLimiter,
        AiSettingsStore settingsStore,
        BudgetGuard budgetGuard,
        UsageLogWriter usageLogWriter,
        EmbeddingCache embeddingCache,
        TimeProvider timeProvider)
    {
        _providers = providers;
        _rateLimiter = rateLimiter;
        _embeddingCache = embeddingCache;
        _steps = new StepServices(reliability, settingsStore, budgetGuard, usageLogWriter, timeProvider);
        _toolLoop = new ToolLoopRunner(_steps);
        _chatJson = new ChatJsonRunner(_steps);
    }

    public async Task<ChatResult> ChatAsync(ChatRequest request, CallContext context, CancellationToken cancellationToken = default)
    {
        var provider = await PrepareProviderAsync(_providers.Chat, context, request);
        var turnDay = await ReserveChatTurnAsync(provider, context, cancellationToken);
        try
        {
            return await _toolLoop.RunAsync(provider, request, context, cancellationToken);
        }
        catch (Exception exception)
        {
            RefundFailedTurn(context, turnDay, exception);
            throw;
        }
    }

    public async IAsyncEnumerable<ChatChunk> ChatStreamAsync(
        ChatRequest request,
        CallContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var provider = await PrepareProviderAsync(_providers.Chat, context, request);
        var turnDay = await ReserveChatTurnAsync(provider, context, cancellationToken);
        var enumerator = _toolLoop.StreamAsync(provider, request, context, cancellationToken).GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (Exception exception)
                {
                    RefundFailedTurn(context, turnDay, exception);
                    throw;
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    public async Task<T> ChatJsonAsync<T>(
        ChatRequest request,
        CallContext context,
        IValidator<T>? validator = null,
        CancellationToken cancellationToken = default)
    {
        var provider = await PrepareProviderAsync(_providers.Chat, context, request);
        var turnDay = await ReserveChatTurnAsync(provider, context, cancellationToken);
        try
        {
            return await _chatJson.RunAsync(provider, request, context, validator, cancellationToken);
        }
        catch (Exception exception)
        {
            RefundFailedTurn(context, turnDay, exception);
            throw;
        }
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CallContext context,
        CancellationToken cancellationToken = default)
    {
        var provider = await PrepareProviderAsync(_providers.Embedding, context, request: null);
        var settings = await _steps.SettingsStore.GetAsync(cancellationToken);
        return await _embeddingCache.EmbedAsync(provider, texts, context, settings, _steps, cancellationToken);
    }

    // Từ chối trước khi gọi ra ngoài. Mỗi lần từ chối vẫn có một dòng log để dashboard thấy sự cố.
    private async Task<TProvider> PrepareProviderAsync<TProvider>(TProvider? provider, CallContext context, ChatRequest? request)
        where TProvider : class, IAiProvider
    {
        try
        {
            CallValidator.ValidateContext(context);
            if (request is not null)
            {
                CallValidator.ValidateRequest(request);
            }
        }
        catch (Exception exception)
        {
            // Lỗi hợp đồng của module gọi vẫn được ghi để đối soát. Request sai không bao giờ tới provider.
            await _steps.LogAsync(provider, context, UsageStatus.Error, AiErrorCodes.From(exception), costUsd: 0);
            throw;
        }

        // Thiếu khóa provider ở production là sự cố. Gateway không tự dùng provider giả.
        if (provider is null)
        {
            await _steps.LogAsync(null, context, UsageStatus.Blocked, "AI_NOT_CONFIGURED", costUsd: 0);
            throw new AiUnavailableException();
        }

        // Model chưa có giá không được gọi. Chi phí không đối soát được làm kiểm tra ngân sách mất tác dụng.
        if (ModelPricingCatalog.Find(provider.Model) is null)
        {
            await _steps.LogAsync(provider, context, UsageStatus.Error, "UNKNOWN_MODEL_PRICING", costUsd: 0);
            throw new InvalidOperationException(
                $"Không thể sử dụng model \"{provider.Model}\": chưa được cấu hình bảng giá an toàn.");
        }

        try
        {
            // Từ chối khi mạch đang mở, trước khi đọc cấu hình hoặc tính hạn mức.
            _steps.Reliability.AssertAvailable(provider.Name);
        }
        catch (AiUnavailableException)
        {
            await _steps.LogAsync(provider, context, UsageStatus.Blocked, "CIRCUIT_OPEN", costUsd: 0);
            throw;
        }

        return provider;
    }

    // Giữ một lượt chat cho cả lời gọi. JSON sửa lại hoặc vòng lặp tool nhiều bước vẫn chỉ tính một lượt.
    private async Task<string?> ReserveChatTurnAsync(IAiProvider provider, CallContext context, CancellationToken cancellationToken)
    {
        if (context.Feature != AiFeature.ShoppingAssistantChat)
        {
            return null;
        }

        var settings = await _steps.SettingsStore.GetAsync(cancellationToken);
        try
        {
            return _rateLimiter.CheckAndIncrement(context.ActorKey, settings.ChatDailyLimitUser, settings.ChatDailyLimitAnon);
        }
        catch (AiBudgetExceededException)
        {
            await _steps.LogAsync(provider, context, UsageStatus.Blocked, "RATE_LIMIT_EXCEEDED", costUsd: 0);
            throw;
        }
    }

    // Sự cố provider hoặc chặn ngân sách không được làm mất lượt chat của người dùng.
    private void RefundFailedTurn(CallContext context, string? turnDay, Exception exception)
    {
        if (turnDay is null || context.ActorKey is null)
        {
            return;
        }

        if (exception is AiUnavailableException or AiBudgetExceededException)
        {
            _rateLimiter.Refund(context.ActorKey, turnDay);
        }
    }
}
