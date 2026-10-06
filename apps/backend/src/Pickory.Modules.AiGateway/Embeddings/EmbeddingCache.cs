using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Modules.AiGateway.Gateway;
using Pickory.Modules.AiGateway.Persistence;
using Pickory.Modules.AiGateway.Providers;
using Pickory.Modules.AiGateway.Resilience;
using Pickory.Modules.AiGateway.Settings;
using Pickory.Modules.AiGateway.Usage;
using Pickory.Shared.Privacy;

namespace Pickory.Modules.AiGateway.Embeddings;

// Cache vĩnh viễn của vector embedding trong bảng `ai_embedding_cache`.
// Text giống nhau chỉ gửi provider một lần, kể cả khi trùng trong một batch hoặc giữa hai lời gọi chồng nhau.
public sealed class EmbeddingCache(IDbContextFactory<AiGatewayDbContext> databaseFactory)
{
    private static readonly TimeSpan EmbeddingTimeout = TimeSpan.FromSeconds(15);

    private readonly Dictionary<string, Task<float[]>> _inFlight = new(StringComparer.Ordinal);
    // Lời gọi song song cùng đăng ký text đang lấy. Khóa giữ mỗi text chỉ có một người lấy.
    private readonly Lock _gate = new();

    // Ranh giới `\0` ngăn hai cặp model và text khác nhau ghép thành cùng một chuỗi trước khi băm.
    public static string ComputeKey(string model, string redactedText)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{model}\0{redactedText}"));
        return Convert.ToHexStringLower(hash);
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IEmbeddingProvider provider,
        IReadOnlyList<string> texts,
        CallContext context,
        AiSettings settings,
        StepServices steps,
        CancellationToken cancellationToken)
    {
        var startTimestamp = steps.Time.GetTimestamp();
        // Ẩn PII trước khi băm và trước khi gửi. Khóa cache không được suy ra từ dữ liệu định danh thô.
        var orderedKeys = new List<string>(texts.Count);
        var textByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            var redactedText = PiiRedactor.Redact(text);
            var key = ComputeKey(provider.Model, redactedText);
            orderedKeys.Add(key);
            textByKey.TryAdd(key, redactedText);
        }

        var (vectorTasks, owned) = RegisterInFlight(textByKey);
        var usage = TokenUsage.Zero;
        var status = UsageStatus.Ok;
        string? errorCode = null;
        try
        {
            if (owned.Count > 0)
            {
                usage = await FillOwnedAsync(provider, owned, context, settings, steps, cancellationToken);
            }

            // Trả vector theo đúng thứ tự và số lần lặp của input.
            var vectors = new List<float[]>(orderedKeys.Count);
            foreach (var key in orderedKeys)
            {
                vectors.Add(await vectorTasks[key]);
            }

            return vectors;
        }
        catch (Exception exception)
        {
            status = exception is AiBudgetExceededException ? UsageStatus.Blocked : UsageStatus.Error;
            errorCode = AiErrorCodes.From(exception);
            // Lời gọi khác đang chờ cùng text nhận cùng lỗi, không chờ mãi.
            foreach (var item in owned)
            {
                item.Source.TrySetException(exception);
            }

            throw;
        }
        finally
        {
            ReleaseInFlight(owned);
            // Một dòng log cho mọi trường hợp: trúng cache, mảng rỗng, chờ chung, lỗi DB, lỗi provider và chặn ngân sách.
            await steps.LogAsync(provider, context, status, errorCode, usage, steps.ElapsedMilliseconds(startTimestamp));
        }
    }

    private (Dictionary<string, Task<float[]>> VectorTasks, List<PendingEmbedding> Owned) RegisterInFlight(
        Dictionary<string, string> textByKey)
    {
        var vectorTasks = new Dictionary<string, Task<float[]>>(StringComparer.Ordinal);
        var owned = new List<PendingEmbedding>();
        lock (_gate)
        {
            foreach (var (key, redactedText) in textByKey)
            {
                // Text đang được lời gọi khác lấy thì chờ kết quả đó. Provider không nhận request trùng.
                if (_inFlight.TryGetValue(key, out var existing))
                {
                    vectorTasks[key] = existing;
                    continue;
                }

                var source = new TaskCompletionSource<float[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                // Đọc lỗi của task ngay. Task không còn ai chờ vẫn không bị báo là lỗi chưa quan sát.
                _ = source.Task.ContinueWith(
                    static task => task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
                _inFlight[key] = source.Task;
                vectorTasks[key] = source.Task;
                owned.Add(new PendingEmbedding(key, redactedText, source));
            }
        }

        return (vectorTasks, owned);
    }

    private void ReleaseInFlight(List<PendingEmbedding> owned)
    {
        lock (_gate)
        {
            foreach (var item in owned)
            {
                _inFlight.Remove(item.Key);
            }
        }
    }

    // Lấy vector cho các text mà lời gọi này chịu trách nhiệm. Trả usage của provider để ghi log.
    private async Task<TokenUsage> FillOwnedAsync(
        IEmbeddingProvider provider,
        List<PendingEmbedding> owned,
        CallContext context,
        AiSettings settings,
        StepServices steps,
        CancellationToken cancellationToken)
    {
        var cachedByKey = await ReadCacheAsync(owned.Select(item => item.Key).ToList(), cancellationToken);
        var missing = new List<PendingEmbedding>();
        foreach (var item in owned)
        {
            if (cachedByKey.TryGetValue(item.Key, out var cached))
            {
                item.Source.TrySetResult(cached);
                continue;
            }

            missing.Add(item);
        }

        if (missing.Count == 0)
        {
            return TokenUsage.Zero;
        }

        // Chỉ kiểm tra ngân sách khi phải gọi provider. Trúng cache không phát sinh phí.
        await steps.Budget.AssertWithinBudgetAsync(context.Feature, settings.MonthlyBudgetUsd, cancellationToken);
        var result = await steps.Reliability.RunAsync(
            provider.Name,
            EmbeddingTimeout,
            token => provider.EmbedAsync(missing.Select(item => item.RedactedText).ToList(), token),
            cancellationToken);
        // Thiếu hoặc thừa vector thì không biết vector nào thuộc text nào. Ghi cache lúc này sẽ sai.
        if (result.Vectors.Count != missing.Count)
        {
            throw new AiUnavailableException("EMBEDDING_VECTOR_COUNT_ERROR");
        }

        for (var index = 0; index < missing.Count; index++)
        {
            var item = missing[index];
            var vector = result.Vectors[index];
            await WriteCacheAsync(item.Key, provider.Model, vector, cancellationToken);
            // Chỉ trả vector sau khi đã lưu. Lời gọi khác không nhận vector từ một lần ghi DB thất bại.
            item.Source.TrySetResult(vector);
        }

        return result.Usage;
    }

    private async Task<Dictionary<string, float[]>> ReadCacheAsync(List<string> keys, CancellationToken cancellationToken)
    {
        try
        {
            await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
            var rows = await database.EmbeddingCache.AsNoTracking()
                .Where(row => keys.Contains(row.Key) && row.Embedding != null)
                .Select(row => new { row.Key, row.Embedding })
                .ToListAsync(cancellationToken);
            return rows.ToDictionary(row => row.Key, row => row.Embedding!.Select(value => (float)value).ToArray(), StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Mã lỗi riêng cho lỗi DB. Exception gốc có thể chứa chuỗi kết nối nên không được giữ lại.
            throw new AiUnavailableException("EMBEDDING_CACHE_READ_ERROR");
        }
    }

    private async Task WriteCacheAsync(string key, string model, float[] vector, CancellationToken cancellationToken)
    {
        // Cột `embedding` là mảng số thực 8 byte, giống DB của bản Node.
        var embedding = vector.Select(value => (double)value).ToArray();
        var id = Guid.NewGuid().ToString();
        try
        {
            await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
            // Hai lời gọi ở hai instance có thể ghi cùng khóa. `ON CONFLICT` giữ một dòng duy nhất cho mỗi khóa.
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO ai_embedding_cache (id, key, model, embedding) VALUES ({id}, {key}, {model}, {embedding}) ON CONFLICT (key) DO UPDATE SET embedding = EXCLUDED.embedding",
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new AiUnavailableException("EMBEDDING_CACHE_WRITE_ERROR");
        }
    }
}
