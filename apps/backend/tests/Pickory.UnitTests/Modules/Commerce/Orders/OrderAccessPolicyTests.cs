using Pickory.Modules.Commerce.Orders;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

public sealed class OrderAccessPolicyTests
{
    private const string ExpectedHash = "5e884898da28047151d0e56f8dc6292773603d0d6aabbdd62a11ef721d1542d8";

    [Fact]
    public void CanAccess_WhenCallerIsOrderOwner_ReturnsTrue()
    {
        // Chủ tài khoản được xem đơn của mình mà không cần truyền token.
        var result = OrderAccessPolicy.CanAccess("user-1", ExpectedHash, "user-1", null);

        Assert.True(result);
    }

    [Fact]
    public void CanAccess_WhenGuestProvidesValidToken_ReturnsTrue()
    {
        // Khách vãng lai cung cấp đúng token khớp với mã băm trong đơn được xem đơn.
        var pair = OrderAccessToken.Create();
        var result = OrderAccessPolicy.CanAccess(null, pair.Hash, null, pair.Token);

        Assert.True(result);
    }

    [Fact]
    public void CanAccess_WhenCallerHasValidTokenForUserOrder_ReturnsTrue()
    {
        // Thư xác nhận gửi liên kết có token, nên người cầm token hợp lệ được xem cả đơn có tài khoản.
        var pair = OrderAccessToken.Create();
        var result = OrderAccessPolicy.CanAccess("user-1", pair.Hash, "user-2", pair.Token);

        Assert.True(result);
    }

    [Fact]
    public void CanAccess_WhenCallerIsDifferentUserWithoutToken_ReturnsFalse()
    {
        // Người dùng khác không được xem đơn của người khác nếu không có token hợp lệ.
        var result = OrderAccessPolicy.CanAccess("user-1", ExpectedHash, "user-2", null);

        Assert.False(result);
    }

    [Fact]
    public void CanAccess_WhenGuestWithoutToken_ReturnsFalse()
    {
        // Khách chưa đăng nhập không có token thì không được xem đơn.
        var result = OrderAccessPolicy.CanAccess(null, ExpectedHash, null, null);

        Assert.False(result);
    }

    [Fact]
    public void CanAccess_WhenTokenIsInvalid_ReturnsFalse()
    {
        // Token sai không khớp với mã băm của đơn bị từ chối truy cập.
        var result = OrderAccessPolicy.CanAccess(null, ExpectedHash, null, "wrong-token");

        Assert.False(result);
    }

    [Fact]
    public void CanAccess_WhenTokenIsEmptyOrWhitespace_ReturnsFalse()
    {
        // Token rỗng hoặc khoảng trắng bị từ chối truy cập.
        Assert.False(OrderAccessPolicy.CanAccess(null, ExpectedHash, null, ""));
        Assert.False(OrderAccessPolicy.CanAccess(null, ExpectedHash, null, "   "));
    }

    [Fact]
    public void CanAccess_WhenOrderHashIsEmpty_ReturnsFalse()
    {
        // Đơn thiếu mã băm bảo vệ bị từ chối để tránh khớp ngoài ý muốn.
        Assert.False(OrderAccessPolicy.CanAccess(null, "", null, "some-token"));
    }
}
