using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Entities;
using Pickory.Shared.Http;

namespace Pickory.Modules.Identity.Features.AdminCustomers;

// Câu truy vấn lọc khách viết một lần. Route danh sách và route xuất file cùng gọi, nên hai route luôn thấy cùng tập khách.
internal static class CustomerListFilter
{
    // Chỉ áp các điều kiện lọc. Hàm không đọc `filter.Page`, nên route xuất bỏ qua phân trang mà không cần nhánh riêng.
    public static IQueryable<User> Apply(IQueryable<User> users, CustomerListQuery filter)
    {
        // Chỉ lấy khách. Nhân viên có trang quản lý riêng và không lẫn vào danh sách này.
        var customers = users.Where(user => user.Kind == UserKind.Customer);

        if (filter.Search is { } term)
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
        if (filter.Status == CustomerStatusFilter.Locked)
        {
            customers = customers.Where(user => user.LockedAt != null);
        }
        else if (filter.Status == CustomerStatusFilter.Active)
        {
            customers = customers.Where(user => user.LockedAt == null);
        }

        if (filter.EmailVerified is { } emailVerified)
        {
            customers = customers.Where(user => user.EmailVerified == emailVerified);
        }

        return customers;
    }

    // Khách mới nhất lên đầu, vì nhân viên thường cần tìm người vừa đăng ký.
    // Thứ tự phụ theo `Id` giữ hai khách cùng `CreatedAt` không đổi chỗ giữa hai trang.
    public static IOrderedQueryable<User> NewestFirst(IQueryable<User> customers)
    {
        return customers
            .OrderByDescending(user => user.CreatedAt)
            .ThenBy(user => user.Id);
    }
}
