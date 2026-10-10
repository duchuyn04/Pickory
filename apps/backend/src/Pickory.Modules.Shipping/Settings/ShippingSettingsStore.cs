using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.Persistence;

namespace Pickory.Modules.Shipping.Settings;

// Đọc và sửa dòng cấu hình lấy hàng duy nhất. SHIP-05 và SHIP-07 dùng lại `GetAsync` để biết địa chỉ lấy hàng của shop.
internal sealed class ShippingSettingsStore(
    ShippingDbContext database,
    IAuditLogWriter auditLogWriter,
    TimeProvider timeProvider)
{
    // Bảng chỉ có một dòng, nên mã dòng là hằng số có tên thay vì chuỗi rải rác.
    public const string SingletonId = "singleton";
    public const string UpdateAuditAction = "shipping.settings.update";
    public const string AuditTargetType = "shipping_settings";

    // Trả `null` khi admin chưa cấu hình. Nơi gọi tự quyết định lỗi nào phù hợp với ngữ cảnh của nó.
    public async Task<ShippingSettings?> GetAsync(CancellationToken cancellationToken)
    {
        // Chỉ đọc nên không cần theo dõi entity.
        return await database.Settings
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == SingletonId, cancellationToken);
    }

    public async Task<ShippingSettings> SaveAsync(
        ValidatedShippingSettings settings, string actorId, CancellationToken cancellationToken)
    {
        // Cấu hình mới và audit nằm trong một transaction. Không có thay đổi nào thiếu audit, và không có audit nào thiếu thay đổi.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        var row = await database.Settings.SingleOrDefaultAsync(value => value.Id == SingletonId, cancellationToken);
        // Chụp trạng thái cũ trước khi sửa. Audit cần cả trạng thái trước và sau.
        var before = row is null ? null : ShippingSettingsView.From(row);
        if (row is null)
        {
            row = new ShippingSettings { Id = SingletonId };
            database.Settings.Add(row);
        }

        row.SenderName = settings.Input.SenderName;
        row.SenderPhone = settings.Input.SenderPhone;
        row.ProvinceCode = settings.Input.ProvinceCode;
        row.ProvinceName = settings.ProvinceName;
        row.WardCode = settings.Input.WardCode;
        row.WardName = settings.WardName;
        row.Line = settings.Input.Line;
        row.DefaultServiceTypeId = settings.Input.DefaultServiceTypeId;
        // Lấy giờ từ `TimeProvider` để test đặt được thời điểm cố định.
        row.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await database.SaveChangesAsync(cancellationToken);

        // Mỗi PUT hợp lệ ghi đúng một dòng audit, kể cả khi giá trị không đổi.
        var entry = new AuditLogEntry(
            actorId, UpdateAuditAction, AuditTargetType, SingletonId, before, ShippingSettingsView.From(row));
        await auditLogWriter.WriteAsync(entry, transaction.GetDbTransaction(), cancellationToken);

        // Commit sau khi ghi audit. Audit lỗi thì transaction rollback khi bị hủy, và cấu hình giữ nguyên.
        await transaction.CommitAsync(cancellationToken);
        return row;
    }
}
