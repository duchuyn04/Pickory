using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.Persistence;

namespace Pickory.Modules.Shipping.FallbackFees;

// Đọc và sửa dòng phí dự phòng duy nhất. SHIP-14 dùng lại `GetAsync` để tính phí khi GHN không phản hồi.
internal sealed class FallbackFeeStore(
    ShippingDbContext database,
    IAuditLogWriter auditLogWriter,
    TimeProvider timeProvider)
{
    // Bảng chỉ có một dòng, nên mã dòng là hằng số có tên thay vì chuỗi rải rác.
    public const string SingletonId = "singleton";
    public const string UpdateAuditAction = "shipping.fallback_fee.update";
    public const string AuditTargetType = "shipping_fallback_fee";

    // Trả `null` khi admin chưa lưu lần nào. Nơi gọi tự quyết định dùng giá trị mặc định hay báo lỗi.
    public async Task<ShippingFallbackFee?> GetAsync(CancellationToken cancellationToken)
    {
        // Chỉ đọc nên không cần theo dõi entity.
        return await database.FallbackFees
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == SingletonId, cancellationToken);
    }

    public async Task<ShippingFallbackFee> SaveAsync(
        FallbackFeeInput input, string actorId, CancellationToken cancellationToken)
    {
        // Cấu hình mới và audit nằm trong một transaction. Không có thay đổi nào thiếu audit, và không có audit nào thiếu thay đổi.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        var row = await database.FallbackFees.SingleOrDefaultAsync(value => value.Id == SingletonId, cancellationToken);
        // Chụp trạng thái cũ trước khi sửa. Lần lưu đầu chưa có dòng, nên dùng giá trị mặc định để audit luôn có cả hai phía.
        var before = row is null ? FallbackFeeView.Default : FallbackFeeView.From(row);
        if (row is null)
        {
            row = new ShippingFallbackFee { Id = SingletonId };
            database.FallbackFees.Add(row);
        }

        row.Enabled = input.Enabled;
        row.SameProvinceBaseFee = input.SameProvinceBaseFee;
        row.SameProvinceBaseWeightGrams = input.SameProvinceBaseWeightGrams;
        row.SameProvinceStepFee = input.SameProvinceStepFee;
        row.SameRegionBaseFee = input.SameRegionBaseFee;
        row.SameRegionBaseWeightGrams = input.SameRegionBaseWeightGrams;
        row.SameRegionStepFee = input.SameRegionStepFee;
        row.CrossRegionBaseFee = input.CrossRegionBaseFee;
        row.CrossRegionBaseWeightGrams = input.CrossRegionBaseWeightGrams;
        row.CrossRegionStepFee = input.CrossRegionStepFee;
        // Lấy giờ từ `TimeProvider` để test đặt được thời điểm cố định.
        row.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await database.SaveChangesAsync(cancellationToken);

        // Mỗi PUT hợp lệ ghi đúng một dòng audit, kể cả khi giá trị không đổi.
        var entry = new AuditLogEntry(
            actorId, UpdateAuditAction, AuditTargetType, SingletonId, before, FallbackFeeView.From(row));
        await auditLogWriter.WriteAsync(entry, transaction.GetDbTransaction(), cancellationToken);

        // Commit sau khi ghi audit. Audit lỗi thì transaction rollback khi bị hủy, và cấu hình giữ nguyên.
        await transaction.CommitAsync(cancellationToken);
        return row;
    }
}
