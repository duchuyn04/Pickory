using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Contracts.Reading;
using Pickory.Modules.Identity.Contracts.Customers;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.SupportDesk.Sessions;

// Dịch vụ tổng hợp bảng thông tin khách hàng và đơn hàng cho phiên hỗ trợ CSKH.
internal sealed class SupportSessionCustomerService(
    SupportDeskDbContext database,
    ICustomerDirectory customerDirectory,
    ICommerceReader commerceReader)
{
    private const string NotFoundMessage = "Không tìm thấy phiên hỗ trợ.";
    private const int RecentOrdersLimit = 5;

    public async Task<SupportSessionCustomerPanelResponse> GetCustomerPanelAsync(
        string sessionId, CancellationToken cancellationToken)
    {
        // Đọc thông tin phiên hỗ trợ mà không theo dõi thay đổi để tối ưu hiệu năng.
        var session = await database.Sessions.AsNoTracking()
            .Where(item => item.Id == sessionId)
            .Select(item => new { item.CustomerUserId, item.AnonymousId })
            .FirstOrDefaultAsync(cancellationToken);

        // Báo lỗi 404 khi không tìm thấy phiên hỗ trợ tương ứng trong hệ thống.
        if (session is null)
        {
            throw new AppException(SupportDeskErrorCodes.SessionNotFound, 404, NotFoundMessage);
        }

        SupportSessionCustomerInfo customerInfo;
        IReadOnlyList<OrderSummary> orderSummaries;

        // Phân nhánh đọc thông tin giữa khách hàng đã đăng nhập và khách vãng lai.
        if (session.CustomerUserId is not null)
        {
            // Đọc thông tin liên hệ của khách hàng từ danh bạ của `identity`.
            var contact = await customerDirectory.GetContactAsync(session.CustomerUserId, cancellationToken);
            // Khách đăng nhập có `isGuest` bằng `false` cùng tên và email từ danh bạ.
            customerInfo = new SupportSessionCustomerInfo(
                IsGuest: false,
                Name: contact?.Name,
                Email: contact?.Email);

            // Đọc tối đa 5 đơn hàng gần nhất của khách hàng đã đăng nhập.
            orderSummaries = await commerceReader.GetOrdersForCustomerAsync(
                session.CustomerUserId, RecentOrdersLimit, cancellationToken);
        }
        else
        {
            // Khách vãng lai luôn có `isGuest` bằng `true` và không có tên hoặc email.
            customerInfo = new SupportSessionCustomerInfo(
                IsGuest: true,
                Name: null,
                Email: null);

            // Đọc tối đa 5 đơn hàng gần nhất của khách vãng lai theo mã ẩn danh.
            orderSummaries = await commerceReader.GetOrdersForAnonymousAsync(
                session.AnonymousId, RecentOrdersLimit, cancellationToken);
        }

        // Chuyển đổi danh sách đơn hàng sang định dạng phản hồi chuẩn của API.
        var orders = orderSummaries.Select(order => new SupportSessionCustomerOrder(
            Code: order.Code,
            Status: JsonNamingPolicy.SnakeCaseLower.ConvertName(order.Status.ToString()),
            Total: order.Total,
            ItemCount: order.ItemCount,
            CreatedAt: order.CreatedAt)).ToList();

        // Trả về dữ liệu bảng thông tin khách hàng gồm thông tin khách và đơn hàng.
        return new SupportSessionCustomerPanelResponse(customerInfo, orders);
    }
}
