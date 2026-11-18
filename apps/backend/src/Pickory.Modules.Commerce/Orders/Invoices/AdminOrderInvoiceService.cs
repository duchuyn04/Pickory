using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Orders.Admin;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Commerce.Settings;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Orders.Invoices;

// Đọc dữ liệu in hóa đơn cho danh sách mã đơn trong đúng hai câu truy vấn DB.
internal sealed class AdminOrderInvoiceService(CommerceDbContext database)
{
    public async Task<OrderInvoicesResponse> GetInvoicesAsync(
        IReadOnlyList<string> codes,
        CancellationToken cancellationToken)
    {
        // Tắt theo dõi thực thể vì route chỉ đọc dữ liệu. Nạp đơn và dòng hàng trong một truy vấn để tránh truy vấn lặp.
        var orders = await database.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .Where(order => codes.Contains(order.Code))
            .ToListAsync(cancellationToken);

        // Kiểm tra đủ mọi mã trước khi đọc cấu hình để báo thiếu đầy đủ theo đúng thứ tự yêu cầu.
        var ordersByCode = orders.ToDictionary(order => order.Code, StringComparer.Ordinal);
        var missingDetails = new List<ErrorFieldDetail>();
        foreach (var code in codes)
        {
            if (!ordersByCode.ContainsKey(code))
            {
                missingDetails.Add(new ErrorFieldDetail(
                    OrderInvoiceCodesParser.CodesParameter,
                    $"Không tìm thấy đơn {code}."));
            }
        }

        // Trả lỗi 404 cho cả yêu cầu khi có mã thiếu, không trả một phần hóa đơn.
        if (missingDetails.Count > 0)
        {
            throw new AppException("ORDER_NOT_FOUND", 404, "Không tìm thấy một số đơn hàng.", missingDetails);
        }

        // Đọc dòng cấu hình duy nhất một lần thay vì đọc theo từng hóa đơn.
        var settings = await database.Settings
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == CommerceSettingsStore.SingletonId, cancellationToken);

        // Trả bốn trường rỗng cho cửa hàng khi chưa có dòng cấu hình trong DB.
        var shop = settings is null
            ? new OrderInvoiceShopResponse(null, null, null, null)
            : new OrderInvoiceShopResponse(
                settings.ShopName,
                settings.ShopAddress,
                settings.ShopPhone,
                settings.ShopTaxCode);

        // Dựng danh sách hóa đơn theo đúng thứ tự mã người dùng chọn, không theo thứ tự DB trả về.
        var invoices = codes
            .Select(code => MapInvoice(ordersByCode[code]))
            .ToList();

        return new OrderInvoicesResponse(shop, invoices);
    }

    private static OrderInvoiceResponse MapInvoice(Order order)
    {
        // Dùng chuỗi rỗng khi bản chụp cũ thiếu khóa liên hệ để hợp đồng không nhận `null`.
        var customer = new OrderInvoiceCustomerResponse(
            ReadJsonString(order.Contact, "name") ?? string.Empty,
            ReadJsonString(order.Contact, "phone") ?? string.Empty,
            ReadJsonString(order.Contact, "email") ?? string.Empty);

        // Ghép địa chỉ một dòng từ bản chụp lúc đặt bằng hàm dùng chung với xuất Excel.
        var shippingAddressText = OrderExportSheets.JoinShippingAddress(
            ReadJsonString(order.ShippingAddress, "line"),
            ReadJsonString(order.ShippingAddress, "wardName"),
            ReadJsonString(order.ShippingAddress, "provinceName"));

        var vatInvoice = MapVatInvoice(order.InvoiceInfo);

        // Sắp xếp dòng hàng theo tên, mã nội bộ và định danh để thứ tự in luôn xác định.
        var lines = order.Items
            .OrderBy(item => item.NameSnapshot, StringComparer.Ordinal)
            .ThenBy(item => item.SkuId, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Select(item =>
            {
                // Dùng phép nhân có kiểm tra tràn số để phát hiện vượt giới hạn nguyên.
                var lineTotal = checked(item.UnitPrice * item.Quantity);
                return new OrderInvoiceLineResponse(
                    item.SkuCodeSnapshot,
                    item.NameSnapshot,
                    OrderItemMapper.FormatVariantName(item.OptionValues),
                    item.Quantity,
                    item.UnitPrice,
                    lineTotal);
            })
            .ToList();

        // Giữ nguyên số tổng lúc đặt đơn thay vì tính lại, bảo toàn bản chụp tài chính gốc.
        return new OrderInvoiceResponse(
            order.Code,
            order.CreatedAt,
            OrderLabels.ToLabel(order.Status),
            OrderLabels.ToLabel(order.PaymentMethod),
            OrderLabels.ToLabel(order.PaymentStatus),
            customer,
            shippingAddressText,
            vatInvoice,
            lines,
            order.Subtotal,
            order.ShippingFee,
            order.ShippingDiscount,
            order.Total);
    }

    private static OrderInvoiceVatResponse? MapVatInvoice(JsonDocument? invoiceInfo)
    {
        // Đơn không yêu cầu hóa đơn thuế giá trị gia tăng (VAT) trả `null` theo hợp đồng.
        if (invoiceInfo is null || invoiceInfo.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // Dùng chuỗi rỗng nếu bản chụp hóa đơn công ty thiếu trường chữ bắt buộc.
        return new OrderInvoiceVatResponse(
            ReadJsonString(invoiceInfo, "companyName") ?? string.Empty,
            ReadJsonString(invoiceInfo, "taxCode") ?? string.Empty,
            ReadJsonString(invoiceInfo, "address") ?? string.Empty,
            ReadJsonString(invoiceInfo, "email") ?? string.Empty);
    }

    private static string? ReadJsonString(JsonDocument? document, string propertyName)
    {
        // Tránh ném ngoại lệ khi bản chụp cũ thiếu khóa hoặc khóa không phải kiểu chuỗi.
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return document.RootElement.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
