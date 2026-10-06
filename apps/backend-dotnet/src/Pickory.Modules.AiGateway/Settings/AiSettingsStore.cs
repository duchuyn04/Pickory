using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Pickory.Modules.AiGateway.Entities;
using Pickory.Modules.AiGateway.Persistence;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Shared.Configuration;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.AiGateway.Settings;

// Đọc và sửa dòng cấu hình duy nhất của bảng `ai_settings`.
public sealed class AiSettingsStore(
    IDbContextFactory<AiGatewayDbContext> databaseFactory,
    AppConfig config,
    IAuditLogWriter auditLogWriter,
    TimeProvider timeProvider)
{
    public const string SingletonId = "singleton";
    private const string UniqueViolationSqlState = "23505";

    public async Task<AiSettings> GetAsync(CancellationToken cancellationToken)
    {
        await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
        var existing = await database.Settings.AsNoTracking().SingleOrDefaultAsync(row => row.Id == SingletonId, cancellationToken);
        if (existing is not null)
        {
            return ToSettings(existing);
        }

        // Lần chạy đầu chưa có dòng cấu hình. Biến môi trường cấp giá trị khởi tạo.
        database.Settings.Add(new AiSetting
        {
            Id = SingletonId,
            MonthlyBudgetUsd = config.AiMonthlyBudgetUsd,
            ChatDailyLimitUser = config.AiChatDailyLimitUser,
            ChatDailyLimitAnon = config.AiChatDailyLimitAnon,
            UpdatedAt = timeProvider.GetUtcNow().UtcDateTime,
        });
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: UniqueViolationSqlState })
        {
            // Request khác vừa tạo dòng này. Đọc lại dòng đó thay vì ghi đè giá trị admin vừa lưu.
        }

        // Lỗi DB khác không bị nuốt. Hạn mức sai còn nguy hiểm hơn một request lỗi.
        await using var verify = await databaseFactory.CreateDbContextAsync(cancellationToken);
        var created = await verify.Settings.AsNoTracking().SingleAsync(row => row.Id == SingletonId, cancellationToken);
        return ToSettings(created);
    }

    public async Task<AiSettings> UpdateAsync(AiSettings settings, string actorId, CancellationToken cancellationToken)
    {
        // Ngân sách âm hoặc vô hạn làm phép chia tỉ lệ ngân sách sai. Hạn mức 0 khóa toàn bộ chat.
        var isValid = double.IsFinite(settings.MonthlyBudgetUsd)
            && settings.MonthlyBudgetUsd >= 0
            && settings.ChatDailyLimitUser > 0
            && settings.ChatDailyLimitAnon > 0;
        if (!isValid)
        {
            throw new AppException(
                "INVALID_AI_SETTINGS",
                400,
                "Cài đặt AI không hợp lệ: ngân sách phải không âm và hạn mức phải là số nguyên dương.");
        }

        await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
        // Cấu hình mới và nhật ký nằm trong một transaction. Không có thay đổi nào thiếu nhật ký.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        var row = await database.Settings.SingleOrDefaultAsync(value => value.Id == SingletonId, cancellationToken);
        // Chụp trạng thái cũ trước khi sửa. Nhật ký cần cả trạng thái trước và sau.
        var before = row is null ? null : ToAuditSnapshot(row);
        if (row is null)
        {
            row = new AiSetting { Id = SingletonId };
            database.Settings.Add(row);
        }

        row.MonthlyBudgetUsd = settings.MonthlyBudgetUsd;
        row.ChatDailyLimitUser = settings.ChatDailyLimitUser;
        row.ChatDailyLimitAnon = settings.ChatDailyLimitAnon;
        // Cập nhật thời điểm sửa ở mỗi lần lưu, giống `@updatedAt` của Prisma.
        row.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await database.SaveChangesAsync(cancellationToken);

        var entry = new AuditLogEntry(actorId, "ai_settings.update", "ai_setting", SingletonId, before, ToAuditSnapshot(row));
        await auditLogWriter.WriteAsync(entry, transaction.GetDbTransaction(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToSettings(row);
    }

    private static AiSettings ToSettings(AiSetting row)
    {
        return new AiSettings(row.MonthlyBudgetUsd, row.ChatDailyLimitUser, row.ChatDailyLimitAnon);
    }

    // Nhật ký lưu cả dòng như bản Node, gồm thời điểm tạo và sửa.
    private static object ToAuditSnapshot(AiSetting row)
    {
        return new
        {
            row.Id,
            row.MonthlyBudgetUsd,
            row.ChatDailyLimitUser,
            row.ChatDailyLimitAnon,
            row.CreatedAt,
            row.UpdatedAt,
        };
    }
}
