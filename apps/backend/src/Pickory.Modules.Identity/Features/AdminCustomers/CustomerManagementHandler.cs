using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Audit;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;

namespace Pickory.Modules.Identity.Features.AdminCustomers;

// Tra cứu, khóa và mở khóa tài khoản khách cho nhân viên.
public sealed class CustomerManagementHandler(IdentityDbContext database, TimeProvider timeProvider)
{
    private const string CustomerNotFoundMessage = "Không tìm thấy khách hàng.";

    public async Task<PagedResponse<CustomerResponse>> ListAsync(CustomerListQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page;

        // Chỉ lấy khách. Nhân viên có trang quản lý riêng và không lẫn vào danh sách này.
        var customers = database.Users.AsNoTracking().Where(user => user.Kind == UserKind.Customer);

        if (query.Search is { } term)
        {
            // Nhân viên hỗ trợ có thể chỉ biết email, tên hoặc số điện thoại khi khách gọi tới.
            // Email và tên so sánh không dấu, không phân biệt hoa thường, và từ khóa đi qua `unaccent` như cột.
            var pattern = term.LikePattern;
            customers = customers.Where(user =>
                EF.Functions.ILike(EF.Functions.Unaccent(user.Email), EF.Functions.Unaccent(pattern), SearchTerm.EscapeCharacter)
                || (user.Name != null
                    && EF.Functions.ILike(EF.Functions.Unaccent(user.Name), EF.Functions.Unaccent(pattern), SearchTerm.EscapeCharacter))
                // Số điện thoại không có dấu, nên không cần `unaccent`. Mẫu đã escape `%` và `_` trong từ khóa.
                || (user.Phone != null && EF.Functions.ILike(user.Phone, pattern, SearchTerm.EscapeCharacter)));
        }

        // Trạng thái khóa suy ra từ `LockedAt`. Bảng không có cột trạng thái riêng.
        if (query.Status == CustomerStatusFilter.Locked)
        {
            customers = customers.Where(user => user.LockedAt != null);
        }
        else if (query.Status == CustomerStatusFilter.Active)
        {
            customers = customers.Where(user => user.LockedAt == null);
        }

        if (query.EmailVerified is { } emailVerified)
        {
            customers = customers.Where(user => user.EmailVerified == emailVerified);
        }

        // Đếm cùng điều kiện lọc. Giao diện biết có bao nhiêu trang.
        var total = await customers.CountAsync(cancellationToken);
        // Khách mới nhất lên đầu, vì nhân viên thường cần tìm người vừa đăng ký.
        // Thứ tự phụ theo `Id` giữ hai khách cùng `CreatedAt` không đổi chỗ giữa hai trang.
        var items = await customers
            .OrderByDescending(user => user.CreatedAt)
            .ThenBy(user => user.Id)
            .Skip(page.Skip)
            .Take(page.Limit)
            .Select(user => new CustomerResponse(
                user.Id, user.Email, user.Name, user.Phone, user.EmailVerified, UserProfileResponse.CustomerKind, user.LockedAt, user.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResponse<CustomerResponse>(items, total, page.Page, page.Limit);
    }

    public async Task<CustomerResponse> GetAsync(string customerId, CancellationToken cancellationToken)
    {
        var customer = await LoadCustomerAsync(customerId, tracked: false, cancellationToken);
        return new CustomerResponse(
            customer.Id, customer.Email, customer.Name, customer.Phone, customer.EmailVerified, UserProfileResponse.CustomerKind, customer.LockedAt, customer.CreatedAt);
    }

    public async Task LockAsync(Actor actor, string customerId, CancellationToken cancellationToken)
    {
        var customer = await LoadCustomerAsync(customerId, tracked: true, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var previousLockedAt = customer.LockedAt;

        // Khóa, xóa phiên và ghi nhật ký cùng thành công hoặc cùng bị hủy.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        customer.LockedAt = now;
        customer.UpdatedAt = now;
        AuditRecorder.Add(database, actor.UserId!, "customer.lock", "user", customer.Id, before: new { lockedAt = previousLockedAt }, after: new { lockedAt = now });
        await database.SaveChangesAsync(cancellationToken);
        // Xóa phiên để khách bị đăng xuất ngay, không phải chờ phiên hết hạn.
        await database.Sessions.Where(session => session.UserId == customer.Id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UnlockAsync(Actor actor, string customerId, CancellationToken cancellationToken)
    {
        var customer = await LoadCustomerAsync(customerId, tracked: true, cancellationToken);
        var previousLockedAt = customer.LockedAt;

        // Không cần xóa phiên. Lúc khóa, API đã xóa mọi phiên của khách này.
        customer.LockedAt = null;
        customer.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        // Mở khóa và nhật ký lưu trong một lần. Không có thao tác mở khóa nào thiếu dấu vết.
        AuditRecorder.Add(database, actor.UserId!, "customer.unlock", "user", customer.Id, before: new { lockedAt = previousLockedAt }, after: new { lockedAt = (DateTime?)null });
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task<User> LoadCustomerAsync(string customerId, bool tracked, CancellationToken cancellationToken)
    {
        var users = tracked ? database.Users : database.Users.AsNoTracking();
        var customer = await users.SingleOrDefaultAsync(user => user.Id == customerId, cancellationToken);

        // Id của nhân viên cũng nhận lỗi không tìm thấy. Route khách không xem hoặc khóa được nhân viên.
        if (customer is null || customer.Kind != UserKind.Customer)
        {
            throw new AppException("CUSTOMER_NOT_FOUND", 404, CustomerNotFoundMessage);
        }

        return customer;
    }
}
