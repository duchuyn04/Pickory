using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.SupportDesk.Persistence.Configurations;

namespace Pickory.Modules.SupportDesk.Settings;

// Đọc và lưu dòng cấu hình CSKH duy nhất. Route đã kiểm quyền và validator trước khi gọi đến đây.
internal sealed class SupportSettingsService(
    SupportDeskDbContext database,
    TimeProvider timeProvider,
    IAuditLogWriter auditLogWriter)
{
    private const string AuditAction = "support.settings.update";
    private const string AuditTargetType = "support_settings";

    public async Task<SupportSettingsResponse> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await database.Settings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == SupportSettingsConfiguration.SingletonId, cancellationToken);
        // Migration tạo sẵn dòng mặc định. Thiếu dòng là lỗi dữ liệu, nên dừng request thay vì trả giá trị đoán.
        return SupportSettingsResponse.From(settings ?? throw MissingRow());
    }

    public async Task<SupportSettingsResponse> UpdateAsync(
        string staffId, SupportSettingsValues values, CancellationToken cancellationToken)
    {
        // Dữ liệu và audit chung một transaction. Lỗi giữa chừng không để lại cấu hình mới mà thiếu dấu vết.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var settings = await database.Settings
            .SingleOrDefaultAsync(row => row.Id == SupportSettingsConfiguration.SingletonId, cancellationToken)
            ?? throw MissingRow();

        // Chụp giá trị cũ trước khi ghi đè, vì audit cần cả `before` lẫn `after`.
        var before = ToAuditSnapshot(settings);
        settings.OpensAt = values.OpensAt;
        settings.ClosesAt = values.ClosesAt;
        settings.Holidays = values.Holidays;
        settings.WaitNudgeMinutes = values.WaitNudgeMinutes;
        settings.AutoCloseMinutes = values.AutoCloseMinutes;
        settings.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await database.SaveChangesAsync(cancellationToken);

        // Đúng một dòng audit cho mỗi lần lưu thành công, ghi trong transaction của dữ liệu.
        var entry = new AuditLogEntry(
            staffId, AuditAction, AuditTargetType, settings.Id, before, ToAuditSnapshot(settings));
        await auditLogWriter.WriteAsync(entry, transaction.GetDbTransaction(), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return SupportSettingsResponse.From(settings);
    }

    // Dùng cùng dạng chuỗi với response để người đọc nhật ký thấy giá trị giống giao diện.
    private static SupportSettingsResponse ToAuditSnapshot(SupportSettings settings) =>
        SupportSettingsResponse.From(settings);

    private static InvalidOperationException MissingRow() =>
        new("Bảng `support_settings` thiếu dòng cấu hình do migration tạo.");
}
