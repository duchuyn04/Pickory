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

    // Marker chỉ gồm chữ thường và số, không dấu. Tìm kiếm trên DB dùng chung chỉ khớp khách của test.
    private static string NewMarker() => Guid.NewGuid().ToString("N")[..12];

    private async Task<(List<string> Ids, int Total)> ListAsync(string sessionToken, string query)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/admin/customers?{query}", sessionToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        var ids = data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToList();
        return (ids, data.GetProperty("total").GetInt32());
    }

    private async Task DeleteCustomersAsync(params string[] customerIds)
    {
        await using var identity = _database.CreateIdentityContext();
        await identity.Users.Where(user => customerIds.Contains(user.Id)).ExecuteDeleteAsync();
    }

    [Theory]
    [InlineData("nguyen")]
    [InlineData("NGUYEN")]
    [InlineData("Nguyễn")]
    // Tên lưu có dấu. Từ khóa không dấu, viết hoa hoặc có dấu đều tìm được.
    public async Task ListCustomers_SearchIgnoresDiacriticsAndCase_ForNguyen(string keyword)
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");
        var marker = NewMarker();
        var customer = await CreateCustomerAsync($"diacritics-{marker}", name: $"Nguyễn Văn {marker}");

        var (ids, total) = await ListAsync(salesSession, $"search={Uri.EscapeDataString($"{keyword} van {marker}")}");

        Assert.Equal([customer.Id], ids);
        Assert.Equal(1, total);
    }

    [Fact]
    // Chữ `Đ` không tách được thành `D` cộng dấu. Bảng quy tắc của `unaccent` phải có `Đ` thì `duc` mới tìm được `Đức`.
    public async Task ListCustomers_SearchDucFindsDucWithStroke()
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");
        var marker = NewMarker();
        var customer = await CreateCustomerAsync($"duc-{marker}", name: $"Đức {marker}");

        var (ids, _) = await ListAsync(salesSession, $"search={Uri.EscapeDataString($"duc {marker}")}");

        Assert.Equal([customer.Id], ids);
    }

    [Fact]
    // Từ khóa gộp khoảng trắng thừa. Tên có một khoảng trắng vẫn khớp từ khóa có nhiều khoảng trắng liền nhau.
    public async Task ListCustomers_SearchCollapsesWhitespace()
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");
        var marker = NewMarker();
        var customer = await CreateCustomerAsync($"space-{marker}", name: $"Trần An {marker}");

        var (ids, _) = await ListAsync(salesSession, $"search={Uri.EscapeDataString($"  tran    an   {marker}  ")}");

        Assert.Equal([customer.Id], ids);
    }

    [Fact]
    // `%` trong từ khóa là ký tự thường. Khách `100x` cùng marker không được khớp từ khóa `100%`.
    public async Task ListCustomers_PercentInKeywordMatchesOnlyLiteralPercent()
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");
        var marker = NewMarker();
        var withPercent = await CreateCustomerAsync($"pct-a-{marker}", name: $"100%{marker}");
        var withoutPercent = await CreateCustomerAsync($"pct-b-{marker}", name: $"100x{marker}");
        try
        {
            var (ids, total) = await ListAsync(salesSession, $"search={Uri.EscapeDataString($"100%{marker}")}");

            Assert.Equal([withPercent.Id], ids);
            Assert.Equal(1, total);
            Assert.DoesNotContain(withoutPercent.Id, ids);
        }
        finally
        {
            // Xóa khách có `%`. Test `PercentSignIsNotWildcard` cũ đếm kết quả của từ khóa `%` trên DB dùng chung.
            await DeleteCustomersAsync(withPercent.Id, withoutPercent.Id);
        }
    }

    [Fact]
    // `_` trong từ khóa là ký tự thường. Khách `axb` không được khớp từ khóa `a_b`.
    public async Task ListCustomers_UnderscoreInKeywordMatchesOnlyLiteralUnderscore()
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");
        var marker = NewMarker();
        var withUnderscore = await CreateCustomerAsync($"und-a-{marker}", name: $"a_b{marker}");
        var withoutUnderscore = await CreateCustomerAsync($"und-b-{marker}", name: $"axb{marker}");

        var (ids, _) = await ListAsync(salesSession, $"search={Uri.EscapeDataString($"a_b{marker}")}");

        Assert.Equal([withUnderscore.Id], ids);
        Assert.DoesNotContain(withoutUnderscore.Id, ids);
    }

    [Fact]
    // Từ khóa `%` chỉ khớp khách có ký tự `%` trong tên, email hoặc số điện thoại. Mọi dòng trả về phải chứa `%`.
    public async Task ListCustomers_PercentAloneMatchesOnlyCustomersContainingPercent()
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");
        var marker = NewMarker();
        var withPercent = await CreateCustomerAsync($"pct-only-a-{marker}", name: $"Giảm 50% {marker}");
        var withoutPercent = await CreateCustomerAsync($"pct-only-b-{marker}", name: $"Không có {marker}");
        try
        {
            var response = await SendAsync(HttpMethod.Get, $"/api/admin/customers?search={Uri.EscapeDataString("%")}&limit=100", salesSession);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = await ActorHttp.ReadJsonAsync(response);
            var items = document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
            Assert.Contains(items, item => item.GetProperty("id").GetString() == withPercent.Id);
            Assert.DoesNotContain(items, item => item.GetProperty("id").GetString() == withoutPercent.Id);
            Assert.All(items, item =>
            {
                var fields = new[] { "email", "name", "phone" }
                    .Select(field => item.GetProperty(field).GetString() ?? string.Empty);
                Assert.Contains(fields, value => value.Contains('%'));
            });
        }
        finally
        {
            // Xóa khách có `%`. Test `PercentSignIsNotWildcard` cũ đếm kết quả của từ khóa `%` trên DB dùng chung.
            await DeleteCustomersAsync(withPercent.Id, withoutPercent.Id);
        }
    }

    private sealed record FilterFixture(User Active, User Locked, User Unverified, string Marker);

    // Ba khách cùng marker: đang hoạt động đã xác minh, bị khóa đã xác minh, đang hoạt động chưa xác minh.
    private async Task<FilterFixture> CreateFilterFixtureAsync()
    {
        var marker = NewMarker();
        var active = await CreateCustomerAsync($"flt-a-{marker}", name: $"Lọc {marker}", emailVerified: true);
        var locked = await CreateCustomerAsync($"flt-b-{marker}", name: $"Lọc {marker}", emailVerified: true, locked: true);
        var unverified = await CreateCustomerAsync($"flt-c-{marker}", name: $"Lọc {marker}", emailVerified: false);
        return new FilterFixture(active, locked, unverified, marker);
    }

    [Fact]
    // `status=locked` chỉ trả khách có `LockedAt`. `status=active` chỉ trả khách chưa khóa.
    public async Task ListCustomers_StatusFilterSeparatesLockedAndActive()
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");
        var fixture = await CreateFilterFixtureAsync();

        var (lockedIds, lockedTotal) = await ListAsync(salesSession, $"search={fixture.Marker}&status=locked");
        var (activeIds, activeTotal) = await ListAsync(salesSession, $"search={fixture.Marker}&status=active");

        Assert.Equal([fixture.Locked.Id], lockedIds);
        Assert.Equal(1, lockedTotal);
        Assert.Equal(new[] { fixture.Active.Id, fixture.Unverified.Id }.Order(), activeIds.Order());
        Assert.Equal(2, activeTotal);
    }

    [Fact]
    // `emailVerified=false` chỉ trả khách chưa xác minh. `emailVerified=true` trả khách đã xác minh.
    public async Task ListCustomers_EmailVerifiedFilterSeparatesVerifiedAndUnverified()
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");
        var fixture = await CreateFilterFixtureAsync();

        var (unverifiedIds, _) = await ListAsync(salesSession, $"search={fixture.Marker}&emailVerified=false");
        var (verifiedIds, _) = await ListAsync(salesSession, $"search={fixture.Marker}&emailVerified=true");

        Assert.Equal([fixture.Unverified.Id], unverifiedIds);
        Assert.Equal(new[] { fixture.Active.Id, fixture.Locked.Id }.Order(), verifiedIds.Order());
    }

    [Fact]
    // Từ khóa kết hợp với hai bộ lọc theo AND. `total` đếm theo cùng điều kiện, không đếm cả ba khách.
    public async Task ListCustomers_SearchCombinesWithBothFilters_AndTotalMatchesFilter()
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");
        var fixture = await CreateFilterFixtureAsync();

        var (ids, total) = await ListAsync(salesSession, $"search={fixture.Marker}&status=active&emailVerified=true");

        Assert.Equal([fixture.Active.Id], ids);
        Assert.Equal(1, total);

        // Chỉ bộ lọc `status=active` giữ hai khách. `limit=1` chỉ cắt dòng trả về, còn `total` vẫn là 2.
        var (pageIds, pageTotal) = await ListAsync(salesSession, $"search={fixture.Marker}&status=active&limit=1");
        Assert.Single(pageIds);
        Assert.Equal(2, pageTotal);
    }

    [Theory]
    [InlineData("status=blocked", "status")]
    [InlineData("status=ACTIVE", "status")]
    [InlineData("status=active&status=locked", "status")]
    [InlineData("emailVerified=yes", "emailVerified")]
    [InlineData("emailVerified=1", "emailVerified")]
    [InlineData("emailVerified=true&emailVerified=false", "emailVerified")]
    [InlineData("search=a&search=b", "search")]
    // Giá trị lạ hoặc tham số lặp lại nhận 400 với đúng tên trường.
    public async Task ListCustomers_InvalidFilterIsRejected(string query, string field)
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");

        var response = await SendAsync(HttpMethod.Get, $"/api/admin/customers?{query}", salesSession);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        Assert.Contains($"{field}:", error.GetProperty("message").GetString());
    }

    [Fact]
    // Hai khách cùng `CreatedAt` nằm ở hai trang liền nhau. Thứ tự phụ theo `Id` giữ cho không lặp dòng và không mất dòng.
    public async Task ListCustomers_SameCreatedAtAcrossPages_NoDuplicateAndNoMissingRow()
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");
        var marker = NewMarker();
        var customerIds = new List<string>();
        for (var index = 0; index < 6; index++)
        {
            var customer = await CreateCustomerAsync($"tie-{index}-{marker}", name: $"Cùng giờ {marker}");
            customerIds.Add(customer.Id);
        }

        // Mỗi `UPDATE` ghi bản dòng mới ở cuối bảng, nên cập nhật theo `Id` giảm dần đặt thứ tự lưu ngược với `Id`.
        // Truy vấn thiếu thứ tự phụ sẽ trả sai thứ tự `Id`.
        var sameInstant = DateTime.UtcNow.AddMinutes(-5);
        foreach (var customerId in customerIds.OrderByDescending(id => id, StringComparer.Ordinal))
        {
            await using var identity = _database.CreateIdentityContext();
            await identity.Users.Where(user => user.Id == customerId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.CreatedAt, sameInstant));
        }

        var collectedIds = new List<string>();
        foreach (var pageNumber in new[] { 1, 2, 3 })
        {
            var (pageIds, total) = await ListAsync(salesSession, $"search={marker}&limit=2&page={pageNumber}");
            Assert.Equal(6, total);
            collectedIds.AddRange(pageIds);
        }

        // Ba trang liền nhau ghép lại đủ sáu khách, không lặp, và theo `Id` tăng dần.
        Assert.Equal(customerIds.Order(StringComparer.Ordinal).ToList(), collectedIds);
    }

    [Fact]
    // Khách mới nhất lên đầu. Thứ tự chính theo `CreatedAt` giảm dần.
    public async Task ListCustomers_NewestCustomerComesFirst()
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");
        var marker = NewMarker();
        var older = await CreateCustomerAsync($"order-a-{marker}", name: $"Thứ tự {marker}");
        var newer = await CreateCustomerAsync($"order-b-{marker}", name: $"Thứ tự {marker}");
        await using (var identity = _database.CreateIdentityContext())
        {
            await identity.Users.Where(user => user.Id == older.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.CreatedAt, DateTime.UtcNow.AddDays(-2)));
            await identity.Users.Where(user => user.Id == newer.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.CreatedAt, DateTime.UtcNow.AddDays(-1)));
        }

        var (ids, _) = await ListAsync(salesSession, $"search={marker}");

        Assert.Equal([newer.Id, older.Id], ids);
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

        var response = await SendAsync(HttpMethod.Patch, $"/api/admin/customers/{customer.Id}", new { locked = true }, salesSession);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN", "Bạn không có đủ quyền thực hiện thao tác này.");
    }

    [Fact]
    // Khóa khách đăng xuất khách ngay và chặn đăng nhập. Mở khóa cho khách đăng nhập lại, và mỗi thao tác có nhật ký.
    public async Task LockAndUnlockCustomer_ControlsAccessAndWritesAudit()
    {
        var (admin, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var customer = await CreateCustomerAsync("lock-customer");
        var customerSession = await IdentityTestData.CreateSessionAsync(_database, customer.Id);

        var lockResponse = await SendAsync(HttpMethod.Patch, $"/api/admin/customers/{customer.Id}", new { locked = true }, adminSession);

        // Khóa ghi thời điểm khóa và xóa dòng phiên trong DB. Kiểm bằng DB vì `/api/me` cũng từ chối tài khoản bị khóa.
        Assert.NotNull(await ReadLockedAtAsync(customer.Id));
        await using (var afterLock = _database.CreateIdentityContext())
        {
            Assert.Equal(0, await afterLock.Sessions.CountAsync(session => session.UserId == customer.Id));
        }

        await RegisterTests.AssertErrorAsync(
            await LoginAsync(customer.Email), HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS", "Email hoặc mật khẩu không chính xác.");
        var meResponse = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", $"pk_session={customerSession}"));
        using (var meDocument = await ActorHttp.ReadJsonAsync(meResponse))
        {
            Assert.Equal("guest", meDocument.RootElement.GetProperty("data").GetProperty("kind").GetString());
        }

        var unlockResponse = await SendAsync(HttpMethod.Patch, $"/api/admin/customers/{customer.Id}", new { locked = false }, adminSession);

        Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unlockResponse.StatusCode);
        // Mở khóa xóa thời điểm khóa.
        Assert.Null(await ReadLockedAtAsync(customer.Id));
        using var unlockDocument = await ActorHttp.ReadJsonAsync(unlockResponse);
        Assert.Equal("Đã mở khóa tài khoản khách hàng.", unlockDocument.RootElement.GetProperty("data").GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Created, (await LoginAsync(customer.Email)).StatusCode);

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

        var response = await SendAsync(HttpMethod.Patch, $"/api/admin/customers/{staff.Id}", new { locked = true }, adminSession);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.NotFound, "CUSTOMER_NOT_FOUND", "Không tìm thấy khách hàng.");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"locked\":null}")]
    [InlineData("{\"locked\":\"true\"}")]
    [InlineData("{\"locked\":1}")]
    [InlineData("{\"locked\":true,\"name\":\"Tên mới\"}")]
    [InlineData("[]")]
    // Body thiếu `locked`, sai kiểu hoặc có trường lạ nhận 400. Khách không bị khóa và phiên còn nguyên.
    public async Task PatchCustomer_InvalidBodyReturns400AndChangesNothing(string rawBody)
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var customer = await CreateCustomerAsync("patch-invalid");
        // Phiên này cho phép kiểm body sai không xóa phiên của khách.
        _ = await IdentityTestData.CreateSessionAsync(_database, customer.Id);
        using var request = ActorHttp.CreateRequest(HttpMethod.Patch, $"/api/admin/customers/{customer.Id}", $"pk_session={adminSession}");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Content = new StringContent(rawBody, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("VALIDATION_ERROR", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Null(await ReadLockedAtAsync(customer.Id));
        await using var identity = _database.CreateIdentityContext();
        Assert.Equal(1, await identity.Sessions.CountAsync(session => session.UserId == customer.Id));
    }

    [Theory]
    [InlineData("lock")]
    [InlineData("unlock")]
    // Hai route cũ đặt theo hành động không còn đăng ký. Test tách đường dẫn và hành động để lệnh grep route cũ không báo nhầm.
    public async Task OldLockRoutes_AreNoLongerRegistered(string action)
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var customer = await CreateCustomerAsync("old-route");

        var response = await SendAsync(HttpMethod.Post, $"/api/admin/customers/{customer.Id}/" + action, adminSession);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Null(await ReadLockedAtAsync(customer.Id));
    }

    private Task<User> CreateCustomerAsync(
        string prefix, string? name = null, string? phone = null, bool emailVerified = false, bool locked = false)
    {
        return CreateCustomerWithProfileAsync(IdentityTestData.NewEmail(prefix), name, phone, emailVerified, locked);
    }

    // Đặt tên và số điện thoại riêng cho từng khách. Tìm kiếm trên DB dùng chung chỉ khớp đúng khách của test.
    private async Task<User> CreateCustomerWithProfileAsync(
        string email, string? name, string? phone, bool emailVerified = false, bool locked = false)
    {
        var customer = await IdentityTestData.CreateUserAsync(
            _database, email, UserKind.Customer, "customer", emailVerified: emailVerified, locked: locked);
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
        return SendAsync(method, path, body: null, sessionToken);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string sessionToken)
    {
        return _client.SendAsync(ActorHttp.CreateJsonRequest(method, path, body, $"pk_session={sessionToken}"));
    }

    // Đọc thời điểm khóa trực tiếp từ DB. Test không dựa vào body response để biết khóa đã được ghi.
    private async Task<DateTime?> ReadLockedAtAsync(string userId)
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.Users.Where(user => user.Id == userId).Select(user => user.LockedAt).SingleAsync();
    }

    private Task<HttpResponseMessage> LoginAsync(string email)
    {
        return _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Post, "/api/auth/sessions", new { email, password = IdentityTestData.DefaultPassword }));
    }
}
