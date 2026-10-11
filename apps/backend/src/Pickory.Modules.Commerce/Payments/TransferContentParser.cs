using System.Text.RegularExpressions;

namespace Pickory.Modules.Commerce.Payments;

// Tìm mã đơn trong nội dung chuyển khoản. Ngân hàng có thể chèn chữ hoặc số trước và sau nội dung khách nhập.
public static partial class TransferContentParser
{
    // Mẫu theo spec: `PK` cộng 8 ký tự `A-Z` hoặc `2-9`, không phân biệt hoa thường.
    // Mẫu viết theo từng chữ cái ASCII để quy tắc chuyển hoa của Unicode không làm mẫu khớp ký tự lạ.
    [GeneratedRegex("[Pp][Kk][A-Za-z2-9]{8}")]
    private static partial Regex OrderCodePattern();

    // Trả mã đơn viết hoa, hoặc `null` khi không thấy, vì nội dung lạ là chuyện thường và hàm không ném lỗi.
    // Nội dung có nhiều mã thì hàm trả mã xuất hiện đầu tiên.
    public static string? FindOrderCode(string? transferContent)
    {
        if (string.IsNullOrEmpty(transferContent))
        {
            return null;
        }

        var match = OrderCodePattern().Match(transferContent);
        if (!match.Success)
        {
            return null;
        }

        // Mã trong DB viết hoa, nên mã tìm được viết hoa trước khi bên gọi tra đơn.
        return match.Value.ToUpperInvariant();
    }
}
