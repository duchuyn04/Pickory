using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Entities;
using Pickory.Modules.AiGateway.Persistence;
using Pickory.Modules.AiGateway.Pricing;

namespace Pickory.Modules.AiGateway.Usage;

// Ghi mỗi bước gọi AI thành một dòng `ai_usage_logs`. Ngân sách tháng tính từ các dòng này.
public sealed class UsageLogWriter(
    IDbContextFactory<AiGatewayDbContext> databaseFactory,
    TimeProvider timeProvider,
    ILogger<UsageLogWriter> logger)
{
    public async Task WriteAsync(UsageLogEntry entry, CancellationToken cancellationToken)
    {
        var usage = entry.Usage;
        var log = new AiUsageLog
        {
            Feature = AiFeatureCodes.ToCode(entry.Feature),
            Provider = entry.Provider,
            Model = entry.Model,
            // Chuỗi rỗng không phải phiên bản prompt. Lưu null để báo cáo không có nhóm phiên bản rỗng.
            PromptVersion = string.IsNullOrWhiteSpace(entry.PromptVersion) ? null : entry.PromptVersion.Trim(),
            InputTokens = usage?.InputTokens ?? 0,
            OutputTokens = usage?.OutputTokens ?? 0,
            CachedTokens = usage?.CachedTokens ?? 0,
            CostUsd = ResolveCostUsd(entry),
            // Làm tròn giống `Math.round` của bản Node. Cột `latencyMs` là số nguyên.
            LatencyMs = (int)Math.Max(0, Math.Round(entry.LatencyMs, MidpointRounding.AwayFromZero)),
            Status = ToCode(entry.Status),
            ErrorCode = entry.ErrorCode,
            ActorKeyHash = HashActorKey(entry.ActorKey),
            CreatedAt = entry.CreatedAt ?? timeProvider.GetUtcNow().UtcDateTime,
        };

        try
        {
            // Mỗi dòng dùng context riêng. Một lần ghi lỗi không làm hỏng context của thao tác khác.
            await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
            database.UsageLogs.Add(log);
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Không ghi exception gốc vì exception của Npgsql có thể chứa chuỗi kết nối DB.
            logger.LogError(
                "Không thể lưu ai_usage_log xuống DB: {Feature}, {Provider}, {Status}, {ErrorCode}.",
                log.Feature,
                log.Provider,
                log.Status,
                "DB_PERSIST_ERROR");
            // Báo lỗi cho người gọi. Bước AI thiếu log làm ngân sách tháng bị tính thiếu.
            throw new InvalidOperationException("Không thể lưu nhật ký sử dụng AI.");
        }
    }

    // Băm một chiều. Bảng log đối soát được theo người mà không lộ userId hoặc anonymousId.
    public static string? HashActorKey(string? actorKey)
    {
        if (string.IsNullOrWhiteSpace(actorKey))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(actorKey.Trim()));
        return Convert.ToHexStringLower(hash);
    }

    private static double ResolveCostUsd(UsageLogEntry entry)
    {
        if (entry.CostUsd is { } costUsd)
        {
            return costUsd;
        }

        // Bị chặn trước provider thì không có phí.
        if (entry.Status == UsageStatus.Blocked)
        {
            return 0;
        }

        // Đã nhận token thì tính phí, kể cả khi bước sau đó lỗi. Provider vẫn tính tiền cho token đã sinh.
        var usage = entry.Usage;
        if (usage is not null && (usage.InputTokens > 0 || usage.OutputTokens > 0))
        {
            return ModelPricingCatalog.CalculateCostUsd(entry.Model, usage);
        }

        // Lỗi mạng trước khi nhận token thì không có phí.
        return 0;
    }

    private static string ToCode(UsageStatus status)
    {
        return status switch
        {
            UsageStatus.Ok => "ok",
            UsageStatus.Error => "error",
            UsageStatus.Blocked => "blocked",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Trạng thái usage không có mã."),
        };
    }
}
