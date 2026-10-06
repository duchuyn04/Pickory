using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Throttling;
using Xunit;

namespace Pickory.IntegrationTests.Api.Identity;

// Port từ test ID-14 của bản Node cho tra cứu, khóa và mở khóa khách hàng.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AdminCustomersTests : IClassFixture<PickoryWebApplicationFactory>
{
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public AdminCustomersTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        // Test đăng nhập khách sau khi mở khóa. Bộ đếm đăng nhập sai bắt đầu trống.
        factory.Services.GetRequiredService<IdentityRateLimiters>().ClearAll();
    }

    [Theory]
    [InlineData("email")]
    [InlineData("name")]
    [InlineData("phone")]
    // Nhân viên hỗ trợ có thể chỉ biết email, tên hoặc số điện thoại khi khách gọi tới.
    public async Task ListCustomers_SearchesByEmailNameOrPhoneWithoutPasswordHash(string searchField)
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");
        // Mỗi khách có chuỗi đánh dấu và số điện thoại ngẫu nhiên. Tìm kiếm trên DB dùng chung chỉ khớp khách của test này.
        var marker = Guid.NewGuid().ToString("N")[..10];
        var randomPhone = $"09{Random.Shared.Next(10_000_000, 100_000_000)}";
        var customer = await CreateCustomerAsync($"search-{marker}", name: $"Khách {marker.ToUpperInvariant()}", phone: randomPhone);
        var search = searchField switch
        {
            "email" => marker.ToUpperInvariant(),
            "name" => marker,
            _ => customer.Phone![2..],
        };

        var response = await SendAsync(HttpMethod.Get, $"/api/admin/customers?search={Uri.EscapeDataString(search)}", salesSession);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(customer.Id, item.GetProperty("id").GetString());
        Assert.Equal("customer", item.GetProperty("kind").GetString());
        // Response liệt kê từng trường, nên hash mật khẩu không lọt ra ngoài.
        Assert.False(item.TryGetProperty("passwordHash", out _));
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(20, data.GetProperty("limit").GetInt32());
    }

    [Fact]
    // Ký tự `%` trong từ khóa là ký tự thường. Từ khóa này không khớp mọi khách trong DB.
    public async Task ListCustomers_PercentSignIsNotWildcard()
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");
        await CreateCustomerAsync("percent-check");

        var response = await SendAsync(HttpMethod.Get, $"/api/admin/customers?search={Uri.EscapeDataString("%")}", salesSession);

        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal(0, document.RootElement.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    // Danh sách chỉ có khách. Nhân viên có trang quản lý riêng và không lẫn vào danh sách này.
    public async Task ListCustomers_ExcludesStaff()
    {
        var (staff, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin", "customer-list-admin");

        var response = await SendAsync(HttpMethod.Get, $"/api/admin/customers?search={Uri.EscapeDataString(staff.Email)}", adminSession);

        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal(0, document.RootElement.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("limit=101", "Dữ liệu không hợp lệ (limit: Số dòng mỗi trang phải là số nguyên từ 1 đến 100.).")]
    [InlineData("page=0", "Dữ liệu không hợp lệ (page: Số trang phải là số nguyên lớn hơn hoặc bằng 1.).")]
    [InlineData("page=abc", "Dữ liệu không hợp lệ (page: Số trang phải là số nguyên lớn hơn hoặc bằng 1.).")]
    // Trần 100 dòng mỗi trang. Một request không kéo về cả bảng người dùng.
    public async Task ListCustomers_InvalidPagingIsRejected(string query, string expectedMessage)
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");

        var response = await SendAsync(HttpMethod.Get, $"/api/admin/customers?{query}", salesSession);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", expectedMessage);
    }

    [Fact]
    // Xem một khách bằng id. Id của nhân viên nhận 404, nên route này không lộ hồ sơ nhân viên.
    public async Task GetCustomer_ReturnsCustomerAndHidesStaff()
    {
        var (staff, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");
        var customer = await CreateCustomerAsync("get-customer");

        var customerResponse = await SendAsync(HttpMethod.Get, $"/api/admin/customers/{customer.Id}", salesSession);
        var staffResponse = await SendAsync(HttpMethod.Get, $"/api/admin/customers/{staff.Id}", salesSession);

        using var document = await ActorHttp.ReadJsonAsync(customerResponse);
        Assert.Equal(customer.Email, document.RootElement.GetProperty("data").GetProperty("email").GetString());
        await RegisterTests.AssertErrorAsync(staffResponse, HttpStatusCode.NotFound, "CUSTOMER_NOT_FOUND", "Không tìm thấy khách hàng.");
    }

    [Fact]
    // Nhân viên bán hàng chỉ có quyền xem khách. Khóa khách cần `customers:manage`.
    public async Task LockCustomer_RequiresCustomersManage()
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");
        var customer = await CreateCustomerAsync("lock-forbidden");

        var response = await SendAsync(HttpMethod.Post, $"/api/admin/customers/{customer.Id}/lock", salesSession);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN", "Bạn không có đủ quyền thực hiện thao tác này.");
    }

    [Fact]
    // Khóa khách đăng xuất khách ngay và chặn đăng nhập. Mở khóa cho khách đăng nhập lại, và mỗi thao tác có nhật ký.
    public async Task LockAndUnlockCustomer_ControlsAccessAndWritesAudit()
    {
        var (admin, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var customer = await CreateCustomerAsync("lock-customer");
        var customerSession = await IdentityTestData.CreateSessionAsync(_database, customer.Id);

        var lockResponse = await SendAsync(HttpMethod.Post, $"/api/admin/customers/{customer.Id}/lock", adminSession);

        await RegisterTests.AssertErrorAsync(
            await LoginAsync(customer.Email), HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS", "Email hoặc mật khẩu không chính xác.");
        var meResponse = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", $"pk_session={customerSession}"));
        using (var meDocument = await ActorHttp.ReadJsonAsync(meResponse))
        {
            Assert.Equal("guest", meDocument.RootElement.GetProperty("data").GetProperty("kind").GetString());
        }

        var unlockResponse = await SendAsync(HttpMethod.Post, $"/api/admin/customers/{customer.Id}/unlock", adminSession);

        Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unlockResponse.StatusCode);
        using var unlockDocument = await ActorHttp.ReadJsonAsync(unlockResponse);
        Assert.Equal("Đã mở khóa tài khoản khách hàng.", unlockDocument.RootElement.GetProperty("data").GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(customer.Email)).StatusCode);

        // Hai thao tác cùng có nhật ký với người thực hiện là admin.
        await using var identity = _database.CreateIdentityContext();
        var actions = await identity.AuditLogs.Where(log => log.TargetId == customer.Id).OrderBy(log => log.CreatedAt).Select(log => new { log.Action, log.ActorId }).ToListAsync();
        Assert.Equal(["customer.lock", "customer.unlock"], actions.Select(value => value.Action));
        Assert.All(actions, value => Assert.Equal(admin.Id, value.ActorId));
    }

    [Fact]
    // Route khách không khóa được nhân viên. Khóa nhân viên phải qua quyền `staff:manage`.
    public async Task LockCustomer_StaffIdReturns404()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var (staff, _) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales", "not-a-customer");

        var response = await SendAsync(HttpMethod.Post, $"/api/admin/customers/{staff.Id}/lock", adminSession);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.NotFound, "CUSTOMER_NOT_FOUND", "Không tìm thấy khách hàng.");
    }

    private Task<User> CreateCustomerAsync(string prefix, string? name = null, string? phone = null)
    {
        return CreateCustomerWithProfileAsync(IdentityTestData.NewEmail(prefix), name, phone);
    }

    // Đặt tên và số điện thoại riêng cho từng khách. Tìm kiếm trên DB dùng chung chỉ khớp đúng khách của test.
    private async Task<User> CreateCustomerWithProfileAsync(string email, string? name, string? phone)
    {
        var customer = await IdentityTestData.CreateUserAsync(_database, email, UserKind.Customer, "customer");
        await using var identity = _database.CreateIdentityContext();
        await identity.Users.Where(user => user.Id == customer.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(user => user.Name, name ?? customer.Name)
            .SetProperty(user => user.Phone, phone ?? customer.Phone));
        customer.Name = name ?? customer.Name;
        customer.Phone = phone ?? customer.Phone;
        return customer;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string sessionToken)
    {
        return _client.SendAsync(ActorHttp.CreateJsonRequest(method, path, body: null, $"pk_session={sessionToken}"));
    }

    private Task<HttpResponseMessage> LoginAsync(string email)
    {
        return _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/auth/login", new { email, password = IdentityTestData.DefaultPassword }));
    }
}
