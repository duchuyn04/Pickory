using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Contracts.Customers;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Persistence;

namespace Pickory.Modules.Identity.Customers;

// Tra cứu thông tin liên hệ của khách hàng từ cơ sở dữ liệu của `identity`.
public sealed class CustomerDirectory(IdentityDbContext database) : ICustomerDirectory
{
    public async Task<CustomerContact?> GetContactAsync(string userId, CancellationToken cancellationToken)
    {
        // Mã người dùng trống không cần truy vấn cơ sở dữ liệu.
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        // Chỉ đọc tài khoản khách hàng để không để lộ thông tin của nhân viên.
        var user = await database.Users.AsNoTracking()
            .Where(account => account.Id == userId && account.Kind == UserKind.Customer)
            .Select(account => new { account.Name, account.Email })
            .FirstOrDefaultAsync(cancellationToken);

        // Trả về `null` khi không tìm thấy tài khoản khách hàng tương ứng.
        if (user is null)
        {
            return null;
        }

        // Tên rỗng được chuẩn hóa về `null` để lớp giao diện không hiển thị khoảng trắng thừa.
        var name = string.IsNullOrWhiteSpace(user.Name) ? null : user.Name;
        // Trả về đối tượng liên hệ với tên đã chuẩn hóa và email từ hồ sơ.
        return new CustomerContact(name, user.Email);
    }
}
