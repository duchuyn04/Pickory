using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Shipping.Persistence;

namespace Pickory.Modules.Shipping.FallbackFees;

// Lưu phí GHN đã tính thành công và đọc lại khi GHN không phản hồi. Phí chỉ dùng được dưới 24 giờ kể từ lúc lưu.
internal sealed class QuoteCacheStore(ShippingDbContext database, TimeProvider timeProvider)
{
    // Phí lưu từ 24 giờ trở lên bị coi là quá hạn, vì giá GHN có thể đã đổi.
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    // Trả `null` khi không có dòng cùng khóa hoặc dòng đã quá hạn.
    public async Task<int?> FindFreshFeeAsync(QuoteCacheKey key, CancellationToken cancellationToken)
    {
        // Dòng còn hạn có `savedAt` lớn hơn mốc này. Đúng 24 giờ tuổi thì `savedAt` bằng mốc nên bị loại.
        var oldestFreshSavedAt = timeProvider.GetUtcNow().UtcDateTime - MaxAge;

        // Chỉ đọc nên không cần theo dõi entity. Khóa chính bảo đảm tối đa một dòng khớp.
        return await database.QuoteCache
            .AsNoTracking()
            .Where(row => row.ReceiverWardCode == key.ReceiverWardCode
                          && row.PickupWardCode == key.PickupWardCode
                          && row.ServiceTypeId == key.ServiceTypeId
                          && row.ChargeableWeightGrams == key.ChargeableWeightGrams
                          && row.InsuredValue == key.InsuredValue
                          && row.CodAmount == key.CodAmount
                          && row.SavedAt > oldestFreshSavedAt)
            .Select(row => (int?)row.Fee)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SaveAsync(QuoteCacheKey key, int fee, CancellationToken cancellationToken)
    {
        // Npgsql gửi `DateTime` UTC dưới dạng `timestamptz`. Lệnh lưu đổi sang cột không múi giờ bằng `AT TIME ZONE 'UTC'`, không phụ thuộc múi giờ của phiên DB.
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Hai request cùng khóa có thể lưu cùng lúc. `ON CONFLICT` để DB giữ một dòng với phí và giờ của lần ghi sau, không ném lỗi trùng khóa.
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO shipping_quote_cache
                ("receiverWardCode", "pickupWardCode", "serviceTypeId", "chargeableWeightGrams", "insuredValue", "codAmount", "fee", "savedAt")
            VALUES
                ({key.ReceiverWardCode}, {key.PickupWardCode}, {key.ServiceTypeId}, {key.ChargeableWeightGrams}, {key.InsuredValue}, {key.CodAmount}, {fee}, {now} AT TIME ZONE 'UTC')
            ON CONFLICT ("receiverWardCode", "pickupWardCode", "serviceTypeId", "chargeableWeightGrams", "insuredValue", "codAmount")
            DO UPDATE SET "fee" = EXCLUDED."fee", "savedAt" = EXCLUDED."savedAt"
            """,
            cancellationToken);

        // Xóa dòng từ 24 giờ trở lên để bảng không lớn mãi. Chỉ mục trên `savedAt` giúp lệnh này không quét cả bảng.
        var expiredAtOrBefore = now - MaxAge;
        await database.QuoteCache
            .Where(row => row.SavedAt <= expiredAtOrBefore)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
