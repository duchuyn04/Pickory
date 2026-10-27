using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Commerce.Persistence;

namespace Pickory.Modules.Commerce.Orders.Lookup;

// Tra cứu đơn bằng mã đơn và email, trả token xem đơn cho trình duyệt.
public sealed class OrderLookupService(CommerceDbContext database)
{
    public async Task<OrderLookupResponse?> LookupAsync(OrderLookupInput input, CancellationToken cancellationToken)
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

        // Không có đơn hoặc email không khớp đều trả cùng một kết quả rỗng. Client không phân biệt được lỗi nào.
        if (order is null)
        {
            return null;
        }

        var contactEmail = ReadContactEmail(order.Contact);
        if (string.IsNullOrWhiteSpace(contactEmail))
        {
            return null;
        }

        // So sánh email không phân biệt chữ hoa chữ thường sau khi cắt khoảng trắng.
        if (!string.Equals(contactEmail.Trim(), input.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Chỉ tạo token mới sau khi mã đơn và email khớp. Lần tra cứu khác không được đổi token của đơn.
        var tokenPair = OrderAccessToken.Create();

        // Ghi hash mới trực tiếp trên cơ sở dữ liệu. DB không lưu token thô.
        await database.Orders
            .Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(o => o.AccessTokenHash, tokenPair.Hash),
                cancellationToken);

        // Token thô chỉ trả một lần trong response. Trình duyệt dùng token để mở trang chi tiết đơn.
        return new OrderLookupResponse(order.Code, tokenPair.Token);
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
