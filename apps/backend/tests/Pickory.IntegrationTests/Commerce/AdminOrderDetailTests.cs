using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Shipping.Contracts.Parcels;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Xunit;

// Contracts cũng có kiểu `Shipment`. Test này ghi entity của module Shipping.
using ShipmentEntity = Pickory.Modules.Shipping.Entities.Shipment;

namespace Pickory.IntegrationTests.Commerce;

// COM-14: chi tiết đơn của nhân viên. Đủ liên hệ, địa chỉ, dòng hàng, hóa đơn VAT, lịch sử trạng thái và vận đơn.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AdminOrderDetailTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string OrdersPath = "/api/admin/orders";

    private static readonly DateTime CreatedAt = new(2026, 10, 5, 3, 0, 0, DateTimeKind.Utc);

    private readonly IntegrationDatabaseFixture _database;
    private readonly AdminOrderTestKit _kit;
    private readonly HttpClient _client;

    public AdminOrderDetailTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _kit = new AdminOrderTestKit(database);
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        await ResetAsync();
        await IdentityTestData.SeedRolesAsync(_database);
    }

    public Task DisposeAsync() => ResetAsync();

    private async Task ResetAsync()
    {
        await _kit.ResetAsync();
        await using var shipping = _database.CreateShippingContext();
        await shipping.ShipmentEvents.ExecuteDeleteAsync();
        await shipping.Shipments.ExecuteDeleteAsync();
    }

    private async Task<HttpResponseMessage> GetAsync(string code, string roleCode = "sales")
    {
        var session = (await IdentityTestData.CreateStaffSessionAsync(_database, roleCode, "com14d")).SessionToken;
        return await _client.SendAsync(
            ActorHttp.CreateRequest(HttpMethod.Get, $"{OrdersPath}/{code}", $"pk_session={session}"));
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await ActorHttp.ReadJsonAsync(response);
        return document.RootElement.GetProperty("data").Clone();
    }

    private async Task<ShipmentEntity> AddShipmentAsync(string reference, ShipmentStatus status, int fee, string trackingCode)
    {
        var shipment = new ShipmentEntity
        {
            Ref = reference,
            TrackingCode = trackingCode,
            Fee = fee,
            CodAmount = 0,
            Parcel = new Parcel(1000, 20, 10, 5),
            Status = status,
            RawStatus = "ready_to_pick",
            CreatedAt = CreatedAt,
            UpdatedAt = CreatedAt,
        };
        await using var shipping = _database.CreateShippingContext();
        shipping.Shipments.Add(shipment);
        await shipping.SaveChangesAsync();
        return shipment;
    }

    [Theory]
    [InlineData(ShippingFeeSource.Ghn, "ghn")]
    [InlineData(ShippingFeeSource.Cached, "cached")]
    [InlineData(ShippingFeeSource.Fallback, "fallback")]
    // COM-27: nhân viên thấy nguồn của phí vận chuyển, viết thường như các nhãn khác của đơn.
    public async Task Detail_ReturnsShippingFeeSourceLabel(ShippingFeeSource source, string expectedLabel)
    {
        await _kit.AddOrderAsync("DT-SRC", CreatedAt, shippingFeeSource: source);

        var data = await ReadDataAsync(await GetAsync("DT-SRC"));

        Assert.Equal(expectedLabel, data.GetProperty("shippingFeeSource").GetString());
    }

    [Fact]
    // Đơn tạo mà không nói rõ nguồn phí nhận nguồn GHN, giống đơn đặt trước khi có cột mới.
    public async Task Detail_OrderWithoutExplicitSource_IsGhn()
    {
        await _kit.AddOrderAsync("DT-DEFAULT-SRC", CreatedAt);

        var data = await ReadDataAsync(await GetAsync("DT-DEFAULT-SRC"));

        Assert.Equal("ghn", data.GetProperty("shippingFeeSource").GetString());
    }

    [Fact]
    // Chi tiết có đủ mọi phần nhân viên cần để làm việc với đơn.
    public async Task Detail_ReturnsEveryPart()
    {
        var order = await _kit.AddOrderAsync(
            "DT-FULL",
            CreatedAt,
            OrderStatus.Shipping,
            PaymentStatus.Paid,
            PaymentMethod.Sepay,
            flags: ["overpaid"],
            lineCount: 2,
            withInvoice: true);
        await _kit.AddStatusHistoryAsync(order.Id, OrderStatus.PendingPayment, OrderStatus.PendingConfirmation, CreatedAt.AddMinutes(10), reason: "Đã nhận tiền");
        await _kit.AddStatusHistoryAsync(order.Id, OrderStatus.PendingConfirmation, OrderStatus.Confirmed, CreatedAt.AddMinutes(20), actorId: "staff-1", reason: "Xác nhận đơn");
        await AddShipmentAsync("DT-FULL", ShipmentStatus.InTransit, 31_000, "GHNTRACK1");

        var data = await ReadDataAsync(await GetAsync("DT-FULL"));

        Assert.Equal("DT-FULL", data.GetProperty("code").GetString());
        Assert.Equal("shipping", data.GetProperty("status").GetString());
        Assert.Equal("paid", data.GetProperty("paymentStatus").GetString());
        Assert.Equal("sepay", data.GetProperty("paymentMethod").GetString());
        Assert.Equal(["overpaid"], data.GetProperty("flags").EnumerateArray().Select(flag => flag.GetString()!).ToArray());

        // Liên hệ và địa chỉ giao là bản chụp lúc đặt, gồm tên tỉnh và tên phường.
        var contact = data.GetProperty("contact");
        Assert.Equal("Nguyễn Văn An", contact.GetProperty("name").GetString());
        Assert.Equal("0901234567", contact.GetProperty("phone").GetString());
        Assert.Equal("an@example.com", contact.GetProperty("email").GetString());
        var address = data.GetProperty("shippingAddress");
        Assert.Equal("Hà Nội", address.GetProperty("provinceName").GetString());
        Assert.Equal("Phường Ba Đình", address.GetProperty("wardName").GetString());
        Assert.Equal("12 Phố Test", address.GetProperty("line").GetString());

        // Dòng hàng theo bản chụp, thứ tự ổn định.
        var items = data.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("Sản phẩm 0", items[0].GetProperty("name").GetString());
        Assert.Equal("Đỏ", items[0].GetProperty("variantName").GetString());
        Assert.Equal(2, items[0].GetProperty("quantity").GetInt32());
        Assert.Equal(100_000, items[0].GetProperty("unitPrice").GetInt32());
        Assert.Equal(200_000, items[0].GetProperty("lineTotal").GetInt32());

        // Hóa đơn VAT.
        var invoice = data.GetProperty("invoice");
        Assert.Equal("Công ty Thử", invoice.GetProperty("companyName").GetString());
        Assert.Equal("0312345678", invoice.GetProperty("taxCode").GetString());

        // Số tiền.
        Assert.Equal(400_000, data.GetProperty("subtotal").GetInt32());
        Assert.Equal(30_000, data.GetProperty("shippingFee").GetInt32());
        Assert.Equal(10_000, data.GetProperty("shippingDiscount").GetInt32());
        Assert.Equal(420_000, data.GetProperty("total").GetInt32());

        // Lịch sử theo thời gian tăng dần, kèm người thực hiện và lý do.
        var history = data.GetProperty("statusHistory").EnumerateArray().ToList();
        Assert.Equal(2, history.Count);
        Assert.Equal("pending_payment", history[0].GetProperty("fromStatus").GetString());
        Assert.Equal("pending_confirmation", history[0].GetProperty("toStatus").GetString());
        Assert.Equal("Đã nhận tiền", history[0].GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, history[0].GetProperty("actorId").ValueKind);
        Assert.Equal("confirmed", history[1].GetProperty("toStatus").GetString());
        Assert.Equal("staff-1", history[1].GetProperty("actorId").GetString());

        // Vận đơn tra theo mã đơn.
        var shipment = data.GetProperty("shipment");
        Assert.Equal("GHNTRACK1", shipment.GetProperty("trackingCode").GetString());
        Assert.Equal("in_transit", shipment.GetProperty("status").GetString());
        Assert.Equal(31_000, shipment.GetProperty("fee").GetInt32());
        Assert.False(string.IsNullOrEmpty(shipment.GetProperty("id").GetString()));
    }

    [Fact]
    // Đơn không có hóa đơn VAT và chưa có vận đơn thì hai trường đó là `null`.
    public async Task Detail_WithoutInvoiceAndShipment_ReturnsNulls()
    {
        await _kit.AddOrderAsync("DT-BARE", CreatedAt);

        var data = await ReadDataAsync(await GetAsync("DT-BARE"));

        Assert.Equal(JsonValueKind.Null, data.GetProperty("invoice").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("shipment").ValueKind);
        Assert.Equal(0, data.GetProperty("statusHistory").GetArrayLength());
    }

    [Fact]
    // Vận đơn của đơn khác không lẫn vào chi tiết.
    public async Task Detail_ShipmentOfAnotherOrder_IsNotShown()
    {
        await _kit.AddOrderAsync("DT-MINE", CreatedAt);
        await _kit.AddOrderAsync("DT-OTHER", CreatedAt.AddMinutes(1));
        await AddShipmentAsync("DT-OTHER", ShipmentStatus.PendingPickup, 20_000, "GHNOTHER");

        var data = await ReadDataAsync(await GetAsync("DT-MINE"));

        Assert.Equal(JsonValueKind.Null, data.GetProperty("shipment").ValueKind);
    }

    [Fact]
    // Mã đơn không tồn tại trả 404 `ORDER_NOT_FOUND`, giống route của khách.
    public async Task Detail_UnknownCode_Returns404()
    {
        var response = await GetAsync("KHONG-CO");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("ORDER_NOT_FOUND", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("sales")]
    [InlineData("support")]
    // Nhân viên đọc được đơn của khách bất kỳ. Response không được lưu bộ nhớ đệm.
    public async Task Detail_WithOrdersRead_Returns200WithNoStore(string roleCode)
    {
        await _kit.AddOrderAsync("DT-ROLE", CreatedAt);

        var response = await GetAsync("DT-ROLE", roleCode);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    // Thiếu `orders:read` nhận 403, khách đã đăng nhập nhận 403 và khách vãng lai nhận 401. Đơn có thật nên 403 không phải do 404.
    public async Task Detail_WithoutPermission_IsRejected()
    {
        await _kit.AddOrderAsync("DT-DENY", CreatedAt);

        var role = await IdentityTestData.CreateRoleAsync(_database, [Permissions.ProductsRead]);
        var staff = await IdentityTestData.CreateUserAsync(
            _database, IdentityTestData.NewEmail("com14d-noperm"), UserKind.Staff, role.Code, emailVerified: true);
        var staffSession = await IdentityTestData.CreateSessionAsync(_database, staff.Id);
        var noPermission = await _client.SendAsync(
            ActorHttp.CreateRequest(HttpMethod.Get, $"{OrdersPath}/DT-DENY", $"pk_session={staffSession}"));
        Assert.Equal(HttpStatusCode.Forbidden, noPermission.StatusCode);

        var customer = await IdentityTestData.CreateUserAsync(
            _database, IdentityTestData.NewEmail("com14d-cust"), UserKind.Customer, "customer", emailVerified: true);
        var customerSession = await IdentityTestData.CreateSessionAsync(_database, customer.Id);
        var asCustomer = await _client.SendAsync(
            ActorHttp.CreateRequest(HttpMethod.Get, $"{OrdersPath}/DT-DENY", $"pk_session={customerSession}"));
        Assert.Equal(HttpStatusCode.Forbidden, asCustomer.StatusCode);

        var guest = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, $"{OrdersPath}/DT-DENY"));
        Assert.Equal(HttpStatusCode.Unauthorized, guest.StatusCode);
    }
}
