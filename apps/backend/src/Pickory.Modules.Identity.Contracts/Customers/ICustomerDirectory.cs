namespace Pickory.Modules.Identity.Contracts.Customers;

// Cổng tra cứu thông tin liên hệ của khách hàng cho các module khác.
public interface ICustomerDirectory
{
    // Đọc thông tin liên hệ của khách hàng theo mã người dùng.
    Task<CustomerContact?> GetContactAsync(string userId, CancellationToken cancellationToken);
}
