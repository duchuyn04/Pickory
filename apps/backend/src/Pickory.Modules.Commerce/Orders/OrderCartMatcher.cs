using Pickory.Modules.Commerce.Carts;

namespace Pickory.Modules.Commerce.Orders;

// So giỏ đã khóa với các dòng đã tính tiền. Hàm thuần, nên unit test không cần DB.
public static class OrderCartMatcher
{
    // Hai danh sách khớp khi có cùng tập SKU và cùng số lượng từng SKU, không phụ thuộc thứ tự.
    public static bool Matches(IReadOnlyCollection<CartLineInput> cartLines, IReadOnlyCollection<CartLineInput> pricedLines)
    {
        // Đếm trước. Số dòng khác nhau thì chắc chắn không khớp, và nhánh sau không cần duyệt.
        if (cartLines.Count != pricedLines.Count)
        {
            return false;
        }

        var cartQuantities = cartLines.ToDictionary(line => line.SkuId, line => line.Quantity, StringComparer.Ordinal);

        // Mỗi dòng đã tính tiền phải có đúng SKU và số lượng trong giỏ. Cùng số dòng và mọi dòng khớp thì hai tập bằng nhau.
        foreach (var priced in pricedLines)
        {
            if (!cartQuantities.TryGetValue(priced.SkuId, out var quantity) || quantity != priced.Quantity)
            {
                return false;
            }
        }

        return true;
    }
}
