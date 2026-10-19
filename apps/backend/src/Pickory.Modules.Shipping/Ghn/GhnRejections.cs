using Pickory.Modules.Shipping.Contracts.Errors;

namespace Pickory.Modules.Shipping.Ghn;

// Đổi thông báo tiếng Anh của GHN thành lỗi tiếng Việt. Adapter thật và bản giả dùng chung để hai bên báo cùng một câu.
internal static class GhnRejections
{
    internal const string AddressMismatchMessage =
        "Đơn vị vận chuyển không nhận địa chỉ vì tên đường không khớp với phường/xã đã chọn. "
        + "Kiểm tra địa chỉ nhận của đơn và địa chỉ lấy hàng trong cấu hình vận chuyển.";

    internal const string PickupPlaceNotInCatalogMessage =
        "Địa chỉ lấy hàng trong cấu hình vận chuyển có tỉnh/thành hoặc phường/xã không còn trong danh mục của đơn vị vận chuyển. "
        + "Cập nhật lại ở trang cấu hình vận chuyển.";

    // Tạo lỗi từ thông báo GHN đã làm sạch. Lỗi không nhận ra giữ cách báo cũ, có tiền tố chung.
    internal static ShippingRejectedException Create(string? carrierMessage)
    {
        var kind = Classify(carrierMessage);
        return kind switch
        {
            ShippingRejectionKind.ToAddressMismatch or ShippingRejectionKind.FromAddressMismatch =>
                ShippingRejectedException.WithFinalMessage(AddressMismatchMessage, kind),
            ShippingRejectionKind.PickupPlaceNotInCatalog =>
                ShippingRejectedException.WithFinalMessage(PickupPlaceNotInCatalogMessage, kind),
            _ => new ShippingRejectedException(carrierMessage),
        };
    }

    // So khớp không phân biệt hoa thường, vì GHN có thể đổi cách viết hoa của thông báo.
    private static ShippingRejectionKind Classify(string? carrierMessage)
    {
        if (string.IsNullOrWhiteSpace(carrierMessage))
        {
            return ShippingRejectionKind.Other;
        }

        // Tách `To` khỏi `From` vì GHN từng gắn nhãn `From` cho địa chỉ nhận sai. Nhãn `To` chưa từng gắn cho địa chỉ lấy hàng.
        if (Contains(carrierMessage, "to address conflict"))
        {
            return ShippingRejectionKind.ToAddressMismatch;
        }

        if (Contains(carrierMessage, "from address conflict"))
        {
            return ShippingRejectionKind.FromAddressMismatch;
        }

        // Phải có dấu hiệu "from ward" để lỗi `to ward not found` của địa chỉ nhận không bị gán nhầm cho cấu hình lấy hàng.
        var isPickupPlaceLookup = Contains(carrierMessage, "from ward");
        var isPlaceMissing = Contains(carrierMessage, "ward not found") || Contains(carrierMessage, "province not found");
        return isPickupPlaceLookup && isPlaceMissing
            ? ShippingRejectionKind.PickupPlaceNotInCatalog
            : ShippingRejectionKind.Other;
    }

    private static bool Contains(string message, string fragment) =>
        message.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}
