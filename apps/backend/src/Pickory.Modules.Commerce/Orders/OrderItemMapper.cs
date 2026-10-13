using System.Text.Json;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Entities;
using Pickory.Shared.Json;

namespace Pickory.Modules.Commerce.Orders;

// Đổi dòng hàng của đơn sang response. Trang của khách và của nhân viên dùng chung để hai bên thấy cùng một bản chụp.
internal static class OrderItemMapper
{
    public static List<OrderDetailItemResponse> ToResponses(IEnumerable<OrderItem> items, ICatalogReader catalogReader)
    {
        // Sắp xếp các dòng hàng theo tên và mã SKU để thứ tự hiển thị luôn xác định.
        return items
            .OrderBy(item => item.NameSnapshot)
            .ThenBy(item => item.SkuId)
            .Select(item => new OrderDetailItemResponse(
                item.SkuId,
                item.NameSnapshot,
                FormatVariantName(item.OptionValues),
                item.ImagePublicId,
                ResolveImageUrl(catalogReader, item.ImagePublicId),
                item.Quantity,
                item.UnitPrice,
                checked(item.UnitPrice * item.Quantity)))
            .ToList();
    }

    // Dựng URL ảnh sản phẩm qua catalog khi có mã ảnh trong bản chụp.
    public static string? ResolveImageUrl(ICatalogReader catalogReader, string? publicId) =>
        string.IsNullOrEmpty(publicId) ? null : catalogReader.BuildProductImageUrl(publicId);

    private static string? FormatVariantName(JsonDocument? optionValues)
    {
        // Bỏ qua định dạng khi dòng đơn không có tùy chọn biến thể.
        if (optionValues is null)
        {
            return null;
        }

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(
                optionValues.RootElement.GetRawText(), PickoryJsonDefaults.Options);

            // Nối các giá trị biến thể thành chuỗi ngăn cách bởi dấu phẩy.
            return dict is not null && dict.Count > 0 ? string.Join(", ", dict.Values) : null;
        }
        catch (JsonException)
        {
            // Trả null khi tài liệu JSON không đúng cấu trúc từ điển.
            return null;
        }
    }
}
