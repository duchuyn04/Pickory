using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Commerce.Persistence;

namespace Pickory.Modules.Commerce.Settings;

// Đọc giới hạn của commerce từ DB. Giỏ hàng và đặt đơn dùng chung lớp này để cùng một nguồn giá trị.
public sealed class CommerceSettingsProvider(CommerceDbContext database)
{
    public async Task<CommerceLimits> GetLimitsAsync(CancellationToken cancellationToken)
    {
        // Bảng chỉ có một dòng nên không cần điều kiện lọc. Chỉ đọc, nên không theo dõi thay đổi.
        var row = await database.Settings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        // Chưa có dòng cấu hình thì dùng mặc định trong mã nguồn. Shop mới vẫn có giới hạn hợp lệ.
        if (row is null)
        {
            return CommerceLimits.Defaults;
        }

        return new CommerceLimits(
            row.FreeShippingThreshold,
            row.CodMaxAmount,
            row.MaxQuantityPerSku,
            row.MaxCartLines);
    }
}
