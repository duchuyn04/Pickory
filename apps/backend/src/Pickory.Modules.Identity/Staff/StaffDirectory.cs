using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Contracts.Staff;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Persistence;

namespace Pickory.Modules.Identity.Staff;

// Tra tên nhân viên theo mã người dùng. Tài khoản đã xóa vẫn trả tên để lịch sử cũ đọc được.
public sealed class StaffDirectory(IdentityDbContext database) : IStaffDirectory
{
    public async Task<IReadOnlyDictionary<string, string>> GetNamesAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken)
    {
        // Danh sách rỗng không cần truy vấn DB.
        if (userIds.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var ids = userIds.Distinct(StringComparer.Ordinal).ToList();
        var rows = await database.Users.AsNoTracking()
            .Where(user => ids.Contains(user.Id) && user.Kind == UserKind.Staff)
            .Select(user => new { user.Id, user.Name, user.Email })
            .ToListAsync(cancellationToken);

        // Tên trống thì dùng email để lịch sử vẫn có nhãn đọc được.
        return rows.ToDictionary(
            row => row.Id,
            row => string.IsNullOrWhiteSpace(row.Name) ? row.Email : row.Name!);
    }
}
