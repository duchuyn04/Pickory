using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Shared.Configuration;
using Pickory.Shared.Email;

namespace Pickory.Modules.Commerce.Orders.Lookup;

// Tra cứu và gửi lại liên kết xem đơn hàng qua email liên hệ của đơn.
public sealed class OrderLookupService(
    CommerceDbContext database,
    IEmailSender emailSender,
    AppConfig config,
    ILogger<OrderLookupService> logger)
{
    // Tra cứu đơn và gửi email khi thông tin khớp.
    public async Task LookupAsync(OrderLookupInput input, CancellationToken cancellationToken)
    {
        // Tìm đơn theo mã đơn đã chuẩn hóa. Cột Code có chỉ mục duy nhất.
        var order = await database.Orders.AsNoTracking()
            .Where(o => o.Code == input.OrderCode)
            .Select(o => new
            {
                o.Id,
                o.Code,
                o.Contact,
            })
            .SingleOrDefaultAsync(cancellationToken);

        // Đơn không tồn tại thì kết thúc mà không gửi email hay thay đổi dữ liệu.
        if (order is null)
        {
            return;
        }

        // Đọc email liên hệ từ bản chụp của đơn hàng đã lưu trong cơ sở dữ liệu.
        var contactEmail = ReadContactEmail(order.Contact);
        // Đơn thiếu email liên hệ thì không có hòm thư để gửi link. Request vẫn nhận 202 như nhánh không khớp.
        if (string.IsNullOrWhiteSpace(contactEmail))
        {
            return;
        }

        // So sánh email không phân biệt chữ hoa chữ thường sau khi cắt khoảng trắng.
        if (!string.Equals(contactEmail.Trim(), input.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Khớp thông tin thì tạo cặp token xem đơn mới.
        var tokenPair = OrderAccessToken.Create();

        // Ghi hash mới trực tiếp trên cơ sở dữ liệu để tránh tranh chấp với cập nhật đơn.
        await database.Orders
            .Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(o => o.AccessTokenHash, tokenPair.Hash),
                cancellationToken);

        // Gửi email chứa liên kết mới tới email liên hệ của đơn.
        await SendLookupEmailAsync(order.Code, contactEmail.Trim(), tokenPair.Token);
    }

    private async Task SendLookupEmailAsync(string orderCode, string contactEmail, string rawToken)
    {
        try
        {
            var message = OrderLookupEmail.Build(orderCode, contactEmail, rawToken, config.WebOrigin);
            // Dùng CancellationToken.None để việc gửi thư không bị hủy khi client ngắt kết nối.
            await emailSender.SendAsync(message, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Log chỉ chứa mã đơn và exception. Tuyệt đối không đưa token, link hay email vào log.
            logger.LogError(exception, "Gửi email tra cứu đơn {OrderCode} thất bại.", orderCode);
        }
    }

    private static string? ReadContactEmail(JsonDocument? contact)
    {
        // Trích xuất trường email từ đối tượng JSON của bản chụp liên hệ.
        if (contact is null || contact.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (contact.RootElement.TryGetProperty("email", out var emailProp) && emailProp.ValueKind == JsonValueKind.String)
        {
            return emailProp.GetString();
        }

        return null;
    }
}
