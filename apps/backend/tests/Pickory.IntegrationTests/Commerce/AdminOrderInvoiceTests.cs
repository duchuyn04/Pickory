using System.Data.Common;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// Kiểm tra route dữ liệu in hóa đơn bán hàng cho nhân viên.
// Hỗ trợ in nhiều đơn một lần, giữ thứ tự theo tham số và giới hạn số câu SQL.
[Collection(CommerceCollection.Name)]
public sealed class AdminOrderInvoiceTests : IClassFixture<CommerceWebApplicationFactory>, IAsyncLifetime
{
    private const string InvoicesFixture = "exports-printing/order-invoices.json";
    private const string InvoicesPath = "/api/admin/order-invoices";
    private static readonly DateTime BaseTime = new(2026, 11, 9, 3, 15, 0, DateTimeKind.Utc);

    // Đếm lệnh đọc SQL của ngữ cảnh commerce để kiểm chứng số truy vấn mỗi request.
    private sealed class CommerceCommandCounter : DbCommandInterceptor
    {
        private int _commands;

        public int Commands => Volatile.Read(ref _commands);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _commands);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private readonly CommerceWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly AdminOrderTestKit _kit;
    private readonly HttpClient _client;

    public AdminOrderInvoiceTests(CommerceWebApplicationFactory factory, CommerceDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _kit = new AdminOrderTestKit(database);
        // Tắt tự giữ cookie để mỗi request dùng đúng phiên mà test chỉ định.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        await ResetAsync();
        await IdentityTestData.SeedRolesAsync(_database);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await ResetAsync();
    }

    private async Task ResetAsync()
    {
        await _kit.ResetAsync();
        await using var commerce = _database.CreateCommerceContext();
        await commerce.Settings.ExecuteDeleteAsync();
    }

    private async Task<string> StaffCookieWithPermissionsAsync(params string[] permissions)
    {
        var role = await IdentityTestData.CreateRoleAsync(_database, permissions);
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(_database, role.Code, "exp08");
        return $"pk_session={token}";
    }

    private async Task<string> RoleCookieAsync(string roleCode)
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(_database, roleCode, $"exp08-{roleCode}");
        return $"pk_session={token}";
    }

    private Task<HttpResponseMessage> GetInvoicesAsync(string? query, string? cookie, HttpClient? client = null)
    {
        var path = query is null ? InvoicesPath : $"{InvoicesPath}?{query}";
        return (client ?? _client).SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, path, cookie));
    }

    // Sinh mã đơn hợp lệ xác định theo chỉ số từ bảng chữ cái của `OrderCodeGenerator`.
    private static string MakeOrderCode(int index)
    {
        var alphabet = OrderCodeGenerator.Alphabet;
        var chars = new char[OrderCodeGenerator.RandomPartLength];
        var remaining = index;
        for (var position = chars.Length - 1; position >= 0; position--)
        {
            chars[position] = alphabet[remaining % alphabet.Length];
            remaining /= alphabet.Length;
        }

        return OrderCodeGenerator.Prefix + new string(chars);
    }

    private static List<string> GenerateOrderCodes(int count) =>
        Enumerable.Range(0, count).Select(MakeOrderCode).ToList();

    private async Task SaveShopSettingsAsync(string? name, string? address, string? phone, string? taxCode)
    {
        await using var commerce = _database.CreateCommerceContext();
        commerce.Settings.Add(new CommerceSettings
        {
            Id = "singleton",
            FreeShippingThreshold = 500_000,
            CodMaxAmount = 20_000_000,
            MaxQuantityPerSku = 5,
            MaxCartLines = 30,
            ShopName = name,
            ShopAddress = address,
            ShopPhone = phone,
            ShopTaxCode = taxCode,
            UpdatedAt = BaseTime,
        });
        await commerce.SaveChangesAsync();
    }

    private async Task SeedTwoContractOrdersAsync()
    {
        await using var commerce = _database.CreateCommerceContext();

        var firstOrderId = Guid.NewGuid().ToString();
        commerce.Orders.Add(new Order
        {
            Id = firstOrderId,
            Code = "PK7KQ2M9XA",
            AnonymousId = "anon-PK7KQ2M9XA",
            Contact = JsonSerializer.SerializeToDocument(new
            {
                name = "Trần Thị Minh Thư",
                phone = "0901234567",
                email = "minhthu@example.com",
            }),
            ShippingAddress = JsonSerializer.SerializeToDocument(new
            {
                provinceCode = "79",
                provinceName = "TP. Hồ Chí Minh",
                wardCode = "26740",
                wardName = "Phường Bến Nghé",
                line = "45 Lê Lợi",
            }),
            InvoiceInfo = JsonSerializer.SerializeToDocument(new
            {
                companyName = "Công ty TNHH Ánh Dương",
                taxCode = "0109876543",
                address = "8 Trần Hưng Đạo, Hoàn Kiếm, Hà Nội",
                email = "ketoan@anhduong.example.com",
            }),
            PaymentMethod = PaymentMethod.Sepay,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Paid,
            Subtotal = 25_370_000,
            ShippingFee = 35_000,
            ShippingDiscount = 20_000,
            Total = 25_385_000,
            AccessTokenHash = "hash-1",
            IdempotencyKey = "idem-1",
            CreatedAt = BaseTime,
            Items =
            [
                new OrderItem
                {
                    Id = Guid.NewGuid().ToString(),
                    OrderId = firstOrderId,
                    SkuId = "sku-ip16",
                    ProductId = "prod-ip16",
                    NameSnapshot = "iPhone 16 256GB",
                    SkuCodeSnapshot = "IP16-256-BLK",
                    OptionValues = JsonSerializer.SerializeToDocument(new Dictionary<string, string>
                    {
                        ["Màu"] = "Đen",
                        ["Dung lượng"] = "256 GB",
                    }),
                    UnitPrice = 24_990_000,
                    Quantity = 1,
                },
                new OrderItem
                {
                    Id = Guid.NewGuid().ToString(),
                    OrderId = firstOrderId,
                    SkuId = "sku-cbl",
                    ProductId = "prod-cbl",
                    NameSnapshot = "Cáp USB-C 1 m",
                    SkuCodeSnapshot = "CBL-USBC-1M",
                    OptionValues = JsonSerializer.SerializeToDocument(new Dictionary<string, string>()),
                    UnitPrice = 190_000,
                    Quantity = 2,
                },
            ],
        });

        var secondOrderId = Guid.NewGuid().ToString();
        commerce.Orders.Add(new Order
        {
            Id = secondOrderId,
            Code = "PK3HN8RT2B",
            AnonymousId = "anon-PK3HN8RT2B",
            Contact = JsonSerializer.SerializeToDocument(new
            {
                name = "Lê Văn Hải",
                phone = "0987654321",
                email = "levanhai@example.com",
            }),
            ShippingAddress = JsonSerializer.SerializeToDocument(new
            {
                provinceCode = "01",
                provinceName = "Hà Nội",
                wardCode = "00349",
                wardName = "Phường Thượng Đình",
                line = "102 Nguyễn Trãi",
            }),
            InvoiceInfo = null,
            PaymentMethod = PaymentMethod.Cod,
            Status = OrderStatus.Cancelled,
            PaymentStatus = PaymentStatus.Unpaid,
            Subtotal = 5_990_000,
            ShippingFee = 30_000,
            ShippingDiscount = 5_000,
            Total = 6_015_000,
            AccessTokenHash = "hash-2",
            IdempotencyKey = "idem-2",
            CreatedAt = BaseTime.AddHours(-16).AddMinutes(-33),
            Items =
            [
                new OrderItem
                {
                    Id = Guid.NewGuid().ToString(),
                    OrderId = secondOrderId,
                    SkuId = "sku-airp",
                    ProductId = "prod-airp",
                    NameSnapshot = "AirPods Pro 2",
                    SkuCodeSnapshot = "AIRP-PRO2",
                    OptionValues = JsonSerializer.SerializeToDocument(new Dictionary<string, string>()),
                    UnitPrice = 5_990_000,
                    Quantity = 1,
                },
            ],
        });

        await commerce.SaveChangesAsync();
    }

    [Fact]
    // Khớp hợp đồng JSON cho hai đơn gồm đơn có thuế giá trị gia tăng (VAT) và đơn đã hủy.
    public async Task GetInvoices_TwoOrdersWithVatAndCancelled_MatchesContract()
    {
        await SaveShopSettingsAsync("Pickory", "12 Nguyễn Văn Bảo, Phường 4, Gò Vấp, TP. Hồ Chí Minh", "028 3894 0390", "0312345678");
        await SeedTwoContractOrdersAsync();
        var cookie = await StaffCookieWithPermissionsAsync(Permissions.OrdersRead);

        using var response = await GetInvoicesAsync("codes=PK7KQ2M9XA,PK3HN8RT2B", cookie);

        await ContractFixture.AssertMatches(InvoicesFixture, "order invoices two orders with vat and cancelled", response);
    }

    [Fact]
    // Khớp hợp đồng JSON khi bảng cấu hình chưa có dữ liệu thông tin cửa hàng.
    public async Task GetInvoices_ShopProfileNotSet_MatchesContract()
    {
        await _kit.AddOrderAsync("PK3HN8RT2B", BaseTime, OrderStatus.Delivered, PaymentStatus.Paid, PaymentMethod.Cod, lineCount: 1);
        var cookie = await StaffCookieWithPermissionsAsync(Permissions.OrdersRead);

        using var response = await GetInvoicesAsync("codes=PK3HN8RT2B", cookie);

        await ContractFixture.AssertMatches(InvoicesFixture, "order invoices shop profile not set", response);
    }

    [Fact]
    // Khớp hợp đồng JSON khi tham số `codes` rỗng.
    public async Task GetInvoices_EmptyCodes_MatchesContract()
    {
        var cookie = await StaffCookieWithPermissionsAsync(Permissions.OrdersRead);

        using var response = await GetInvoicesAsync("codes=", cookie);

        await ContractFixture.AssertMatches(InvoicesFixture, "order invoices empty codes", response);
    }

    [Fact]
    // Khớp hợp đồng JSON khi danh sách mã đơn vượt quá giới hạn 50 đơn.
    public async Task GetInvoices_TooManyCodes_MatchesContract()
    {
        var codes = GenerateOrderCodes(51);
        var cookie = await StaffCookieWithPermissionsAsync(Permissions.OrdersRead);

        using var response = await GetInvoicesAsync($"codes={string.Join(',', codes)}", cookie);

        await ContractFixture.AssertMatches(InvoicesFixture, "order invoices too many codes", response);
    }

    [Fact]
    // Khớp hợp đồng JSON khi có mã đơn không tồn tại kèm mã đơn thật.
    public async Task GetInvoices_UnknownCode_MatchesContract()
    {
        await _kit.AddOrderAsync("PK7KQ2M9XA", BaseTime);
        var cookie = await StaffCookieWithPermissionsAsync(Permissions.OrdersRead);

        using var response = await GetInvoicesAsync("codes=PK7KQ2M9XA,PKZZZZZZZZ", cookie);

        await ContractFixture.AssertMatches(InvoicesFixture, "order invoices unknown code", response);
    }

    [Fact]
    // Khớp hợp đồng JSON khi nhân viên không có quyền đọc đơn hàng.
    public async Task GetInvoices_MissingPermission_MatchesContract()
    {
        var cookie = await StaffCookieWithPermissionsAsync(Permissions.ProductsRead);

        using var response = await GetInvoicesAsync("codes=PK7KQ2M9XA", cookie);

        await ContractFixture.AssertMatches(InvoicesFixture, "order invoices missing permission", response);
    }

    [Fact]
    // Chưa đăng nhập nhận 401 và tài khoản khách nhận 403 vì không có quyền nhân viên.
    public async Task GetInvoices_UnauthenticatedAndCustomer_AreRejected()
    {
        using var unauthResponse = await GetInvoicesAsync("codes=PK7KQ2M9XA", cookie: null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResponse.StatusCode);

        var customer = await IdentityTestData.CreateUserAsync(
            _database, IdentityTestData.NewEmail("exp08-cust"), UserKind.Customer, DefaultRoles.CustomerCode);
        var customerToken = await IdentityTestData.CreateSessionAsync(_database, customer.Id);
        using var customerResponse = await GetInvoicesAsync("codes=PK7KQ2M9XA", $"pk_session={customerToken}");
        Assert.Equal(HttpStatusCode.Forbidden, customerResponse.StatusCode);
    }

    [Fact]
    // Vai trò bán hàng có quyền đọc đơn gọi được route và nhận dữ liệu hợp lệ.
    public async Task GetInvoices_SalesRoleWithOrdersRead_Returns200()
    {
        await _kit.AddOrderAsync("PK7KQ2M9XA", BaseTime);
        var salesCookie = await RoleCookieAsync(DefaultRoles.SalesCode);

        using var response = await GetInvoicesAsync("codes=PK7KQ2M9XA", salesCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    // Vai trò không có quyền đọc đơn hàng bị chặn với mã lỗi 403 dù có quyền xuất báo cáo.
    public async Task GetInvoices_RoleWithoutOrdersRead_Returns403()
    {
        var cookie = await StaffCookieWithPermissionsAsync(Permissions.ProductsRead, Permissions.ReportsExport);

        using var response = await GetInvoicesAsync("codes=PK7KQ2M9XA", cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Kiểm tra chi tiết trường cửa hàng, hóa đơn VAT, phân loại, tổng dòng hàng và địa chỉ.
    public async Task GetInvoices_ReturnsAccurateShopVatOptionLinesAndAddress()
    {
        await SaveShopSettingsAsync("Pickory Store", "12 Nguyễn Văn Bảo, Phường 4, TP. Hồ Chí Minh", "02838940390", "0312345678");
        await SeedTwoContractOrdersAsync();
        var cookie = await StaffCookieWithPermissionsAsync(Permissions.OrdersRead);

        using var response = await GetInvoicesAsync("codes=PK7KQ2M9XA,PK3HN8RT2B", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");

        var shop = data.GetProperty("shop");
        Assert.Equal("Pickory Store", shop.GetProperty("name").GetString());
        Assert.Equal("12 Nguyễn Văn Bảo, Phường 4, TP. Hồ Chí Minh", shop.GetProperty("address").GetString());
        Assert.Equal("02838940390", shop.GetProperty("phone").GetString());
        Assert.Equal("0312345678", shop.GetProperty("taxCode").GetString());

        var invoices = data.GetProperty("invoices");
        Assert.Equal(2, invoices.GetArrayLength());

        var first = invoices[0];
        Assert.Equal("PK7KQ2M9XA", first.GetProperty("code").GetString());
        Assert.Equal("2026-11-09T03:15:00.000Z", first.GetProperty("createdAt").GetString());
        Assert.Equal("confirmed", first.GetProperty("status").GetString());
        Assert.Equal("sepay", first.GetProperty("paymentMethod").GetString());
        Assert.Equal("paid", first.GetProperty("paymentStatus").GetString());
        Assert.Equal("45 Lê Lợi, Phường Bến Nghé, TP. Hồ Chí Minh", first.GetProperty("shippingAddressText").GetString());

        var firstCustomer = first.GetProperty("customer");
        Assert.Equal("Trần Thị Minh Thư", firstCustomer.GetProperty("name").GetString());
        Assert.Equal("0901234567", firstCustomer.GetProperty("phone").GetString());
        Assert.Equal("minhthu@example.com", firstCustomer.GetProperty("email").GetString());

        var vat = first.GetProperty("vatInvoice");
        Assert.Equal("Công ty TNHH Ánh Dương", vat.GetProperty("companyName").GetString());
        Assert.Equal("0109876543", vat.GetProperty("taxCode").GetString());
        Assert.Equal("8 Trần Hưng Đạo, Hoàn Kiếm, Hà Nội", vat.GetProperty("address").GetString());
        Assert.Equal("ketoan@anhduong.example.com", vat.GetProperty("email").GetString());

        var firstLines = first.GetProperty("lines");
        Assert.Equal(2, firstLines.GetArrayLength());
        Assert.Equal("CBL-USBC-1M", firstLines[0].GetProperty("skuCode").GetString());
        Assert.Equal("Cáp USB-C 1 m", firstLines[0].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, firstLines[0].GetProperty("optionText").ValueKind);
        Assert.Equal(2, firstLines[0].GetProperty("quantity").GetInt32());
        Assert.Equal(190_000, firstLines[0].GetProperty("unitPrice").GetInt32());
        Assert.Equal(380_000, firstLines[0].GetProperty("lineTotal").GetInt32());
        Assert.Equal("IP16-256-BLK", firstLines[1].GetProperty("skuCode").GetString());
        Assert.Equal("iPhone 16 256GB", firstLines[1].GetProperty("name").GetString());
        Assert.Equal("Đen, 256 GB", firstLines[1].GetProperty("optionText").GetString());
        Assert.Equal(1, firstLines[1].GetProperty("quantity").GetInt32());
        Assert.Equal(24_990_000, firstLines[1].GetProperty("unitPrice").GetInt32());
        Assert.Equal(24_990_000, firstLines[1].GetProperty("lineTotal").GetInt32());

        var lineTotalSum = firstLines[0].GetProperty("lineTotal").GetInt32() + firstLines[1].GetProperty("lineTotal").GetInt32();
        Assert.Equal(lineTotalSum, first.GetProperty("subtotal").GetInt32());
        Assert.Equal(35_000, first.GetProperty("shippingFee").GetInt32());
        Assert.Equal(20_000, first.GetProperty("shippingDiscount").GetInt32());
        Assert.Equal(25_385_000, first.GetProperty("total").GetInt32());

        var second = invoices[1];
        Assert.Equal("PK3HN8RT2B", second.GetProperty("code").GetString());
        Assert.Equal("2026-11-08T10:42:00.000Z", second.GetProperty("createdAt").GetString());
        Assert.Equal("cancelled", second.GetProperty("status").GetString());
        Assert.Equal("cod", second.GetProperty("paymentMethod").GetString());
        Assert.Equal("unpaid", second.GetProperty("paymentStatus").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("vatInvoice").ValueKind);
        Assert.Equal("102 Nguyễn Trãi, Phường Thượng Đình, Hà Nội", second.GetProperty("shippingAddressText").GetString());

        var secondCustomer = second.GetProperty("customer");
        Assert.Equal("Lê Văn Hải", secondCustomer.GetProperty("name").GetString());
        Assert.Equal("0987654321", secondCustomer.GetProperty("phone").GetString());
        Assert.Equal("levanhai@example.com", secondCustomer.GetProperty("email").GetString());

        var secondLines = second.GetProperty("lines");
        var secondLine = Assert.Single(secondLines.EnumerateArray());
        Assert.Equal("AIRP-PRO2", secondLine.GetProperty("skuCode").GetString());
        Assert.Equal("AirPods Pro 2", secondLine.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, secondLine.GetProperty("optionText").ValueKind);
        Assert.Equal(1, secondLine.GetProperty("quantity").GetInt32());
        Assert.Equal(5_990_000, secondLine.GetProperty("unitPrice").GetInt32());
        Assert.Equal(5_990_000, secondLine.GetProperty("lineTotal").GetInt32());
        Assert.Equal(5_990_000, second.GetProperty("subtotal").GetInt32());
        Assert.Equal(30_000, second.GetProperty("shippingFee").GetInt32());
        Assert.Equal(5_000, second.GetProperty("shippingDiscount").GetInt32());
        Assert.Equal(6_015_000, second.GetProperty("total").GetInt32());
    }

    [Fact]
    // Đơn có bản chụp liên hệ thiếu email vẫn trả chuỗi rỗng để khớp schema không cho `null`.
    public async Task GetInvoices_ContactMissingEmail_ReturnsEmptyString()
    {
        await _kit.AddOrderAsync("PK9EM2XY3Z", BaseTime, omitRecipientEmail: true);
        var cookie = await StaffCookieWithPermissionsAsync(Permissions.OrdersRead);

        using var response = await GetInvoicesAsync("codes=PK9EM2XY3Z", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await ActorHttp.ReadJsonAsync(response);
        var customer = document.RootElement.GetProperty("data").GetProperty("invoices")[0].GetProperty("customer");
        Assert.Equal(string.Empty, customer.GetProperty("email").GetString());
    }

    [Fact]
    // Bản chụp hóa đơn công ty thiếu khóa hoặc sai kiểu JSON được quy về chuỗi rỗng hoặc `null`.
    public async Task GetInvoices_MalformedOrIncompleteInvoiceInfo_FallsBackSafely()
    {
        await using (var commerce = _database.CreateCommerceContext())
        {
            var incompleteId = Guid.NewGuid().ToString();
            commerce.Orders.Add(new Order
            {
                Id = incompleteId,
                Code = "PK4VA2MN5P",
                AnonymousId = "anon-incomplete",
                Contact = JsonSerializer.SerializeToDocument(new { name = "Khách A", phone = "0900000001", email = "a@example.com" }),
                ShippingAddress = JsonSerializer.SerializeToDocument(new { line = "1 Đường A", wardName = "Phường 1", provinceName = "Hà Nội" }),
                InvoiceInfo = JsonSerializer.SerializeToDocument(new { companyName = "Công ty A", email = 123 }),
                PaymentMethod = PaymentMethod.Cod,
                Status = OrderStatus.Confirmed,
                PaymentStatus = PaymentStatus.Unpaid,
                Subtotal = 100_000,
                ShippingFee = 20_000,
                ShippingDiscount = 0,
                Total = 120_000,
                AccessTokenHash = "hash-inc",
                IdempotencyKey = "idem-inc",
                CreatedAt = BaseTime,
            });

            var nonObjectId = Guid.NewGuid().ToString();
            commerce.Orders.Add(new Order
            {
                Id = nonObjectId,
                Code = "PK5VB3PQ6R",
                AnonymousId = "anon-nonobj",
                Contact = JsonSerializer.SerializeToDocument(new { name = "Khách B", phone = "0900000002", email = "b@example.com" }),
                ShippingAddress = JsonSerializer.SerializeToDocument(new { line = "2 Đường B", wardName = "Phường 2", provinceName = "Hà Nội" }),
                InvoiceInfo = JsonDocument.Parse("[1, 2]"),
                PaymentMethod = PaymentMethod.Cod,
                Status = OrderStatus.Confirmed,
                PaymentStatus = PaymentStatus.Unpaid,
                Subtotal = 100_000,
                ShippingFee = 20_000,
                ShippingDiscount = 0,
                Total = 120_000,
                AccessTokenHash = "hash-nonobj",
                IdempotencyKey = "idem-nonobj",
                CreatedAt = BaseTime,
            });

            await commerce.SaveChangesAsync();
        }

        var cookie = await StaffCookieWithPermissionsAsync(Permissions.OrdersRead);
        using var response = await GetInvoicesAsync("codes=PK4VA2MN5P,PK5VB3PQ6R", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await ActorHttp.ReadJsonAsync(response);
        var invoices = document.RootElement.GetProperty("data").GetProperty("invoices");
        var incompleteVat = invoices[0].GetProperty("vatInvoice");
        Assert.Equal("Công ty A", incompleteVat.GetProperty("companyName").GetString());
        Assert.Equal(string.Empty, incompleteVat.GetProperty("taxCode").GetString());
        Assert.Equal(string.Empty, incompleteVat.GetProperty("address").GetString());
        Assert.Equal(string.Empty, incompleteVat.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, invoices[1].GetProperty("vatInvoice").ValueKind);
    }

    [Fact]
    // Hai dòng hàng trùng tên sản phẩm phải sắp xếp xác định theo mã nội bộ `SkuId`.
    public async Task GetInvoices_MultipleLinesWithSameName_SortsDeterministicallyBySkuId()
    {
        var orderId = Guid.NewGuid().ToString();
        await using (var commerce = _database.CreateCommerceContext())
        {
            commerce.Orders.Add(new Order
            {
                Id = orderId,
                Code = "PK6SR7TU8V",
                AnonymousId = "anon-sort",
                Contact = JsonSerializer.SerializeToDocument(new { name = "Khách C", phone = "0900000003", email = "c@example.com" }),
                ShippingAddress = JsonSerializer.SerializeToDocument(new { line = "3 Đường C", wardName = "Phường 3", provinceName = "Hà Nội" }),
                PaymentMethod = PaymentMethod.Cod,
                Status = OrderStatus.Confirmed,
                PaymentStatus = PaymentStatus.Unpaid,
                Subtotal = 200_000,
                ShippingFee = 20_000,
                ShippingDiscount = 0,
                Total = 220_000,
                AccessTokenHash = "hash-sort",
                IdempotencyKey = "idem-sort",
                CreatedAt = BaseTime,
                Items =
                [
                    new OrderItem
                    {
                        Id = "item-1",
                        OrderId = orderId,
                        SkuId = "sku-b",
                        ProductId = "prod-1",
                        NameSnapshot = "iPhone 16",
                        SkuCodeSnapshot = "IP16-WHT",
                        OptionValues = JsonSerializer.SerializeToDocument(new Dictionary<string, string> { ["Màu"] = "Trắng" }),
                        UnitPrice = 100_000,
                        Quantity = 1,
                    },
                    new OrderItem
                    {
                        Id = "item-2",
                        OrderId = orderId,
                        SkuId = "sku-a",
                        ProductId = "prod-1",
                        NameSnapshot = "iPhone 16",
                        SkuCodeSnapshot = "IP16-BLK",
                        OptionValues = JsonSerializer.SerializeToDocument(new Dictionary<string, string> { ["Màu"] = "Đen" }),
                        UnitPrice = 100_000,
                        Quantity = 1,
                    },
                ],
            });
            await commerce.SaveChangesAsync();
        }

        var cookie = await StaffCookieWithPermissionsAsync(Permissions.OrdersRead);
        using var response = await GetInvoicesAsync("codes=PK6SR7TU8V", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await ActorHttp.ReadJsonAsync(response);
        var lines = document.RootElement.GetProperty("data").GetProperty("invoices")[0].GetProperty("lines");
        Assert.Equal("IP16-BLK", lines[0].GetProperty("skuCode").GetString());
        Assert.Equal("IP16-WHT", lines[1].GetProperty("skuCode").GetString());
    }

    [Fact]
    // Route lấy giá trị tạm tính từ cột của đơn thay vì tự cộng lại các dòng hàng.
    public async Task GetInvoices_ReturnsStoredSubtotalFromOrderColumn()
    {
        var order = await _kit.AddOrderAsync("PK8ST4MN2Q", BaseTime, lineCount: 1);
        await using (var commerce = _database.CreateCommerceContext())
        {
            await commerce.Orders
                .Where(candidate => candidate.Id == order.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Subtotal, 999_000));
        }

        var cookie = await StaffCookieWithPermissionsAsync(Permissions.OrdersRead);
        using var response = await GetInvoicesAsync("codes=PK8ST4MN2Q", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await ActorHttp.ReadJsonAsync(response);
        var invoice = document.RootElement.GetProperty("data").GetProperty("invoices")[0];
        Assert.Equal(999_000, invoice.GetProperty("subtotal").GetInt32());
    }

    [Fact]
    // Khi bảng cấu hình tồn tại nhưng bốn cột cửa hàng rỗng thì API trả bốn giá trị rỗng.
    public async Task GetInvoices_SettingsRowWithNullShopColumns_ReturnsNullShopFields()
    {
        await SaveShopSettingsAsync(null, null, null, null);
        await _kit.AddOrderAsync("PK7KQ2M9XA", BaseTime);
        var cookie = await StaffCookieWithPermissionsAsync(Permissions.OrdersRead);

        using var response = await GetInvoicesAsync("codes=PK7KQ2M9XA", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await ActorHttp.ReadJsonAsync(response);
        var shop = document.RootElement.GetProperty("data").GetProperty("shop");
        Assert.Equal(JsonValueKind.Null, shop.GetProperty("name").ValueKind);
        Assert.Equal(JsonValueKind.Null, shop.GetProperty("address").ValueKind);
        Assert.Equal(JsonValueKind.Null, shop.GetProperty("phone").ValueKind);
        Assert.Equal(JsonValueKind.Null, shop.GetProperty("taxCode").ValueKind);
    }

    [Fact]
    // Hóa đơn trả về giữ đúng thứ tự mã trên URL và chỉ giữ lần đầu cho mã trùng.
    public async Task GetInvoices_PreservesRequestedOrderAndDeduplicatesCodes()
    {
        await _kit.AddOrderAsync("PKAAAAAAAA", BaseTime);
        await _kit.AddOrderAsync("PKBBBBBBBB", BaseTime.AddMinutes(10));
        await _kit.AddOrderAsync("PKCCCCCCCC", BaseTime.AddMinutes(20));
        var cookie = await StaffCookieWithPermissionsAsync(Permissions.OrdersRead);

        using var response = await GetInvoicesAsync("codes=PKCCCCCCCC,PKAAAAAAAA,PKCCCCCCCC,PKBBBBBBBB,PKAAAAAAAA", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await ActorHttp.ReadJsonAsync(response);
        var returnedCodes = document.RootElement
            .GetProperty("data")
            .GetProperty("invoices")
            .EnumerateArray()
            .Select(invoice => invoice.GetProperty("code").GetString()!)
            .ToList();

        Assert.Equal(["PKCCCCCCCC", "PKAAAAAAAA", "PKBBBBBBBB"], returnedCodes);
    }

    [Fact]
    // Đơn thiếu một hoặc nhiều mã đều trả 404 kèm danh sách mã thiếu theo thứ tự yêu cầu.
    public async Task GetInvoices_MultipleMissingCodes_Returns404WithOrderedMissingDetails()
    {
        await _kit.AddOrderAsync("PK7KQ2M9XA", BaseTime);
        var cookie = await StaffCookieWithPermissionsAsync(Permissions.OrdersRead);

        using var response = await GetInvoicesAsync("codes=PKZZZZZZZ2,PK7KQ2M9XA,PKZZZZZZZ3", cookie);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("ORDER_NOT_FOUND", error.GetProperty("code").GetString());
        var details = error.GetProperty("details").EnumerateArray().ToList();
        Assert.Equal(2, details.Count);
        Assert.All(details, item => Assert.Equal("codes", item.GetProperty("key").GetString()));
        Assert.Contains("PKZZZZZZZ2", details[0].GetProperty("message").GetString());
        Assert.Contains("PKZZZZZZZ3", details[1].GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("codes=")]
    [InlineData("codes=pk7kq2m9xa")]
    [InlineData("codes=PK123")]
    [InlineData("codes=PK7KQ2M9XO")]
    [InlineData("codes=PK7KQ2M9XA&codes=PK3HN8RT2B")]
    // Tham số thiếu, rỗng, lặp lại hoặc sai định dạng đều trả 400 với khóa lỗi là `codes`.
    public async Task GetInvoices_InvalidQuery_Returns400WithCodesKey(string? query)
    {
        var cookie = await StaffCookieWithPermissionsAsync(Permissions.OrdersRead);

        using var response = await GetInvoicesAsync(query, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        var detail = Assert.Single(error.GetProperty("details").EnumerateArray());
        Assert.Equal("codes", detail.GetProperty("key").GetString());
    }

    [Fact]
    // Phản hồi thành công, lỗi 400 và lỗi 404 đều gắn `Cache-Control: no-store`.
    public async Task GetInvoices_SetsNoStoreHeader_On200And400And404()
    {
        await _kit.AddOrderAsync("PK7KQ2M9XA", BaseTime);
        var cookie = await StaffCookieWithPermissionsAsync(Permissions.OrdersRead);

        using var okResponse = await GetInvoicesAsync("codes=PK7KQ2M9XA", cookie);
        using var badRequestResponse = await GetInvoicesAsync("codes=", cookie);
        using var notFoundResponse = await GetInvoicesAsync("codes=PKZZZZZZZZ", cookie);

        Assert.Equal("no-store", okResponse.Headers.CacheControl?.ToString());
        Assert.Equal("no-store", badRequestResponse.Headers.CacheControl?.ToString());
        Assert.Equal("no-store", notFoundResponse.Headers.CacheControl?.ToString());
    }

    [Fact]
    // Khi nhận đủ 50 mã có nhiều dòng hàng, route chỉ chạy đúng hai câu truy vấn DB commerce.
    public async Task GetInvoices_50Orders_RunsExactlyTwoCommerceQueriesAndZeroOn400()
    {
        // Tạo sẵn 50 đơn hàng có nhiều dòng hàng cùng cấu hình cửa hàng trong DB.
        var codes = GenerateOrderCodes(50);
        for (var index = 0; index < codes.Count; index++)
        {
            await _kit.AddOrderAsync(codes[index], BaseTime.AddMinutes(index), lineCount: 3);
        }

        await SaveShopSettingsAsync("Pickory", "12 Nguyễn Văn Bảo", "02838940390", "0312345678");
        var cookie = await StaffCookieWithPermissionsAsync(Permissions.OrdersRead);

        // Gắn bộ đếm lệnh riêng vào `CommerceDbContext` sau bước tạo dữ liệu mẫu.
        var counter = new CommerceCommandCounter();
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.ConfigureDbContext<CommerceDbContext>(options => options.AddInterceptors(counter))));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Tham số sai bị chặn ở tầng kiểm tra đầu vào nên không chạy lệnh đọc DB nào.
        var beforeInvalid = counter.Commands;
        using var badResponse = await GetInvoicesAsync("codes=INVALID", cookie, client);
        Assert.Equal(HttpStatusCode.BadRequest, badResponse.StatusCode);
        Assert.Equal(0, counter.Commands - beforeInvalid);

        // Yêu cầu hợp lệ 50 đơn chỉ chạy đúng hai lệnh: một đọc đơn kèm dòng hàng và một đọc cấu hình.
        var beforeValid = counter.Commands;
        using var okResponse = await GetInvoicesAsync($"codes={string.Join(',', codes)}", cookie, client);
        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
        Assert.Equal(2, counter.Commands - beforeValid);

        // Xác nhận kết quả vẫn đủ 50 hóa đơn và giữ đúng thứ tự yêu cầu.
        using var document = await ActorHttp.ReadJsonAsync(okResponse);
        var invoices = document.RootElement.GetProperty("data").GetProperty("invoices");
        Assert.Equal(50, invoices.GetArrayLength());
        Assert.Equal(codes[0], invoices[0].GetProperty("code").GetString());
        Assert.Equal(codes[^1], invoices[49].GetProperty("code").GetString());
    }
}
