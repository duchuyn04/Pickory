using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Contracts.Audit;

namespace Pickory.Modules.Commerce.Settings;

// Đọc và sửa dòng cấu hình commerce duy nhất. `CommerceSettingsProvider` đọc giới hạn cho nghiệp vụ, lớp này phục vụ trang admin.
internal sealed class CommerceSettingsStore(
    CommerceDbContext database,
    IAuditLogWriter auditLogWriter,
    TimeProvider timeProvider)
{
    // Bảng chỉ có một dòng, nên mã dòng là hằng số có tên thay vì chuỗi rải rác.
    public const string SingletonId = "singleton";
    public const string UpdateAuditAction = "commerce.settings.update";
    public const string AuditTargetType = "commerce_settings";

    // Trả `null` khi admin chưa lưu cấu hình. Nơi gọi hiện giá trị mặc định.
    public async Task<CommerceSettings?> GetAsync(CancellationToken cancellationToken)
    {
        // Chỉ đọc nên không cần theo dõi entity.
        return await database.Settings
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == SingletonId, cancellationToken);
    }

    public async Task<CommerceSettings> SaveAsync(
        CommerceSettingsInput input, string actorId, CancellationToken cancellationToken)
    {
        // Cấu hình mới và audit nằm trong một transaction. Không có thay đổi nào thiếu audit, và không có audit nào thiếu thay đổi.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Tạo dòng bằng `ON CONFLICT DO NOTHING` thay vì đọc rồi thêm. Hai PUT đầu tiên đồng thời không gây lỗi trùng khóa.
        var rowWasCreated = await InsertDefaultRowIfMissingAsync(cancellationToken);

        // Khóa dòng đến hết transaction. PUT đồng thời xếp hàng, và mỗi audit ghi đúng trạng thái trước của chính lần sửa đó.
        var row = await database.Settings
            .FromSqlInterpolated($"""SELECT * FROM commerce_settings WHERE "id" = {SingletonId} FOR UPDATE""")
            .SingleAsync(cancellationToken);

        // Dòng vừa tạo chỉ chứa giá trị mặc định tạm. Với admin, lần này chưa có cấu hình trước đó.
        var before = rowWasCreated ? null : CommerceSettingsView.From(row);

        row.FreeShippingThreshold = input.FreeShippingThreshold;
        row.CodMaxAmount = input.CodMaxAmount;
        row.MaxQuantityPerSku = input.MaxQuantityPerSku;
        row.MaxCartLines = input.MaxCartLines;
        row.SepayAccountNumber = input.SepayAccountNumber;
        row.SepayBankCode = input.SepayBankCode;
        // Lấy giờ từ `TimeProvider` để test đặt được thời điểm cố định.
        row.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await database.SaveChangesAsync(cancellationToken);

        // Mỗi PUT hợp lệ ghi đúng một dòng audit, kể cả khi giá trị không đổi.
        var entry = new AuditLogEntry(
            actorId, UpdateAuditAction, AuditTargetType, SingletonId, before, CommerceSettingsView.From(row));
        await auditLogWriter.WriteAsync(entry, transaction.GetDbTransaction(), cancellationToken);

        // Commit sau khi ghi audit. Audit lỗi thì transaction rollback, và cấu hình giữ nguyên.
        await transaction.CommitAsync(cancellationToken);
        return row;
    }

    // Trả `true` khi lần gọi này tạo dòng mới. Giá trị mặc định lấy từ `CommerceLimits.Defaults`, không lặp ở chỗ khác.
    private async Task<bool> InsertDefaultRowIfMissingAsync(CancellationToken cancellationToken)
    {
        var defaults = CommerceLimits.Defaults;
        // Cột `updatedAt` là `timestamp` không múi giờ. Khai báo kiểu tường minh để Npgsql không gửi `timestamptz`.
        var updatedAt = new NpgsqlParameter("updatedAt", NpgsqlDbType.Timestamp)
        {
            Value = DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified),
        };

        var insertedRows = await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO commerce_settings
                ("id", "freeShippingThreshold", "codMaxAmount", "maxQuantityPerSku", "maxCartLines", "updatedAt")
            VALUES (@id, @freeShippingThreshold, @codMaxAmount, @maxQuantityPerSku, @maxCartLines, @updatedAt)
            ON CONFLICT ("id") DO NOTHING
            """,
            [
                new NpgsqlParameter("id", SingletonId),
                new NpgsqlParameter("freeShippingThreshold", defaults.FreeShippingThreshold),
                new NpgsqlParameter("codMaxAmount", defaults.CodMaxAmount),
                new NpgsqlParameter("maxQuantityPerSku", defaults.MaxQuantityPerSku),
                new NpgsqlParameter("maxCartLines", defaults.MaxCartLines),
                updatedAt,
            ],
            cancellationToken);
        return insertedRows == 1;
    }
}
