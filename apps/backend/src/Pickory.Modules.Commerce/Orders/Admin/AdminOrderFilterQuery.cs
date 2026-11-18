using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Commerce.Entities;
using Pickory.Shared.Calendar;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Câu truy vấn lọc đơn viết một lần. Route danh sách và route xuất file cùng gọi, nên hai route luôn thấy cùng tập đơn.
internal static class AdminOrderFilterQuery
{
    // Chỉ áp các điều kiện lọc. Hàm không đọc `filter.Page`, nên route xuất bỏ qua phân trang mà không cần nhánh riêng.
    public static IQueryable<Order> Apply(IQueryable<Order> orders, AdminOrderListQuery filter)
    {
        // Mỗi bộ lọc chỉ thêm điều kiện khi có tham số. Các điều kiện kết hợp theo AND.
        if (filter.Status is { } status)
        {
            orders = orders.Where(order => order.Status == status);
        }

        if (filter.PaymentStatus is { } paymentStatus)
        {
            orders = orders.Where(order => order.PaymentStatus == paymentStatus);
        }

        if (filter.PaymentMethod is { } paymentMethod)
        {
            orders = orders.Where(order => order.PaymentMethod == paymentMethod);
        }

        if (filter.Flag is { } flag)
        {
            orders = orders.Where(order => order.Flags.Contains(flag));
        }

        // Ngày lọc theo giờ Việt Nam. Cận dưới gồm cận, cận trên không gồm cận để ngày được tính hết 23:59:59.999.
        if (filter.From is { } from)
        {
            var fromUtc = VietnamCalendar.DayStartUtc(from);
            orders = orders.Where(order => order.CreatedAt >= fromUtc);
        }

        if (filter.To is { } to)
        {
            var toExclusiveUtc = VietnamCalendar.DayEndExclusiveUtc(to);
            orders = orders.Where(order => order.CreatedAt < toExclusiveUtc);
        }

        if (filter.Search is { } term)
        {
            // Nhân viên có thể chỉ biết mã đơn, tên, số điện thoại hoặc email khi khách gọi tới. Điều kiện kết hợp theo OR.
            var pattern = term.LikePattern;

            // Đơn thiếu khóa JSON cho giá trị NULL trong SQL, và `ILIKE` với NULL không khớp. Dấu `!` chỉ để biên dịch chấp nhận kiểu.
            orders = orders.Where(order =>
                // Mã đơn và số điện thoại không có dấu, nên không cần `unaccent`. Mẫu đã escape `%` và `_` trong từ khóa.
                EF.Functions.ILike(order.Code, pattern, SearchTerm.EscapeCharacter)
                || EF.Functions.ILike(order.Contact.RootElement.GetProperty("phone").GetString()!, pattern, SearchTerm.EscapeCharacter)
                // Tên và email không dấu và không phân biệt hoa thường. Từ khóa đi qua `unaccent` như cột, để hai phía cùng dạng.
                || EF.Functions.ILike(
                    EF.Functions.Unaccent(order.Contact.RootElement.GetProperty("name").GetString()!),
                    EF.Functions.Unaccent(pattern),
                    SearchTerm.EscapeCharacter)
                || EF.Functions.ILike(
                    EF.Functions.Unaccent(order.Contact.RootElement.GetProperty("email").GetString()!),
                    EF.Functions.Unaccent(pattern),
                    SearchTerm.EscapeCharacter));
        }

        return orders;
    }

    // Sắp xếp theo ngày tạo giảm dần rồi theo `Id`. Thiếu `Id` thì phân trang lặp hoặc sót các đơn cùng ngày tạo.
    public static IOrderedQueryable<Order> NewestFirst(IQueryable<Order> orders)
    {
        return orders
            .OrderByDescending(order => order.CreatedAt)
            .ThenBy(order => order.Id);
    }
}
