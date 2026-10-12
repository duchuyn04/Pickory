using System.Security.Cryptography;
using System.Text;

namespace Pickory.Modules.Commerce.Orders;

// Quy tắc phân quyền xem và thao tác trên đơn hàng.
public static class OrderAccessPolicy
{
    // Kiểm tra quyền xem đơn dựa trên tài khoản người gọi hoặc token truy cập.
    public static bool CanAccess(string? orderUserId, string orderAccessTokenHash, string? callerUserId, string? providedToken)
    {
        // Chủ tài khoản đã đăng nhập được xem đơn của chính mình mà không cần token.
        if (!string.IsNullOrEmpty(callerUserId) && callerUserId == orderUserId)
        {
            return true;
        }

        // Token rỗng hoặc đơn thiếu mã băm thì từ chối quyền xem.
        if (string.IsNullOrWhiteSpace(providedToken) || string.IsNullOrEmpty(orderAccessTokenHash))
        {
            return false;
        }

        // Băm token và so sánh thời gian cố định trên mảng byte để chống tấn công kênh kề.
        var computedHash = Encoding.UTF8.GetBytes(OrderAccessToken.Hash(providedToken));
        var expectedHash = Encoding.UTF8.GetBytes(orderAccessTokenHash);
        return CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
    }
}
