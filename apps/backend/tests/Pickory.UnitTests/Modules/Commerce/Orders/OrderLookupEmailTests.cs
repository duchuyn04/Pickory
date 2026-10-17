using Pickory.Modules.Commerce.Orders.Lookup;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

// Kiểm tra nội dung, liên kết và metadata của email tra cứu đơn hàng.
public sealed class OrderLookupEmailTests
{
    private const string WebOrigin = "https://shop.example.com";
    private const string OrderCode = "PK23456789";
    private const string ContactEmail = "khach@example.com";
    private const string AccessToken = "tok_test_secret_123";

    [Fact]
    // Email gửi đúng người nhận kèm tiêu đề và metadata phân loại thư.
    public void Build_AddressesContactEmailWithSubjectAndMetadata()
    {
        var message = OrderLookupEmail.Build(OrderCode, ContactEmail, AccessToken, WebOrigin);

        Assert.Equal(ContactEmail, message.To);
        Assert.Equal("Pickory: liên kết xem đơn hàng PK23456789", message.Subject);
        Assert.Equal("order-lookup", message.Metadata["purpose"]);
        Assert.Equal("PK23456789", message.Metadata["orderCode"]);
    }

    [Fact]
    // Metadata không được chứa token để tránh rò rỉ token truy cập.
    public void Build_MetadataDoesNotCarryTheToken()
    {
        var message = OrderLookupEmail.Build(OrderCode, ContactEmail, AccessToken, WebOrigin);

        Assert.DoesNotContain(message.Metadata.Values, value => value.Contains(AccessToken));
        Assert.Equal(2, message.Metadata.Count);
    }

    [Fact]
    // Thân thư chứa liên kết xem đơn gồm web origin, mã đơn và token.
    public void Build_LinkHasOriginOrderCodeAndToken()
    {
        var message = OrderLookupEmail.Build(OrderCode, ContactEmail, AccessToken, WebOrigin);

        Assert.Contains("https://shop.example.com/orders/PK23456789?t=tok_test_secret_123", message.Text);
    }

    [Fact]
    // Mã đơn và token có ký tự đặc biệt phải được mã hóa URL trong liên kết.
    public void Build_EscapesOrderCodeAndTokenInTheLink()
    {
        var message = OrderLookupEmail.Build("PK 1/2", ContactEmail, "a+b/c=&d", WebOrigin);

        Assert.Contains("/orders/PK%201%2F2?t=a%2Bb%2Fc%3D%26d", message.Text);
    }

    [Fact]
    // Dấu gạch chéo ở cuối web origin được loại bỏ để tránh tạo URL có hai dấu gạch chéo.
    public void Build_IgnoresTrailingSlashInWebOrigin()
    {
        var message = OrderLookupEmail.Build(OrderCode, ContactEmail, AccessToken, WebOrigin + "/");

        Assert.Contains("https://shop.example.com/orders/PK23456789?t=", message.Text);
        Assert.DoesNotContain("com//orders", message.Text);
    }
}
