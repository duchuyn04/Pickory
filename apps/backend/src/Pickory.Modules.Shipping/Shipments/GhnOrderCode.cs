namespace Pickory.Modules.Shipping.Shipments;

// Dựng mã `client_order_code` gửi GHN. GHN chống tạo trùng theo mã này.
public static class GhnOrderCode
{
    // Giới hạn độ dài của GHN. Mã dài hơn bị GHN từ chối.
    public const int MaxLength = 50;

    // Lần đầu dùng `ref`, sau n lần hủy mã có hậu tố `-(n+1)`.
    // Hậu tố giữ mỗi đơn một mã riêng, vì GHN tạo đơn mới khi nhận lại mã của đơn đã hủy (ADR 0003).
    public static string For(string reference, int cancelledCount)
    {
        // Mã rỗng không định danh được vận đơn nào. GHN sẽ coi mọi đơn là cùng một mã.
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("`ref` của vận đơn không được rỗng.", nameof(reference));
        }

        // Số lần hủy âm là lỗi đếm của bên gọi. Hậu tố sẽ trùng mã của lần trước.
        if (cancelledCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cancelledCount), "Số lần hủy không được âm.");
        }

        // Lần tạo đầu giữ nguyên `ref`. Gửi lại cùng mã sau lỗi mạng giúp GHN trả vận đơn đã tạo.
        var code = cancelledCount == 0 ? reference : $"{reference}-{(long)cancelledCount + 1}";

        // Kiểm độ dài sau khi thêm hậu tố, vì hậu tố có thể đẩy mã từ 50 lên 51 ký tự.
        if (code.Length > MaxLength)
        {
            throw new ArgumentException($"Mã gửi GHN dài hơn {MaxLength} ký tự.", nameof(reference));
        }

        return code;
    }
}
