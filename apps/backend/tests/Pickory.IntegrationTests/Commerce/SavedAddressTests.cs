using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.IntegrationTests.Shipping;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-18: sổ địa chỉ của khách đã đăng nhập. Test đi qua route thật và đọc lại DB để chứng minh dữ liệu đã lưu.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class SavedAddressTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string AddressesPath = "/api/me/addresses";
    private const string ProvinceCode = "1000001";
    private const string WardCode = "1003646";
    private const string OtherProvinceCode = "1000000";
    private const string InactiveWardCode = "1003649";
    private const string OtherProvinceWardCode = "1004001";

    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;
    private readonly List<string> _userIds = [];

    public SavedAddressTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        // Danh mục địa chỉ dựng lại cho mỗi test, để mã tỉnh và phường trong test luôn có mặt và không lệch.
        await QuoteTestSetup.ResetAsync(_factory);
        await QuoteTestSetup.SeedCatalogAsync(_factory);
        await IdentityTestData.SeedRolesAsync(_database);
    }

    public async Task DisposeAsync()
    {
        await using var commerce = _database.CreateCommerceContext();
        // Bỏ qua bộ lọc soft delete để dọn cả địa chỉ đã xóa mềm trong test.
        await commerce.SavedAddresses.IgnoreQueryFilters().Where(address => _userIds.Contains(address.UserId)).ExecuteDeleteAsync();
    }

    private sealed record Customer(string Id, string Cookie);

    private async Task<Customer> NewCustomerAsync()
    {
        var user = await IdentityTestData.CreateUserAsync(
            _database, IdentityTestData.NewEmail("addr"), UserKind.Customer, DefaultRoles.CustomerCode, emailVerified: true);
        _userIds.Add(user.Id);
        var token = await IdentityTestData.CreateSessionAsync(_database, user.Id);
        return new Customer(user.Id, $"pk_session={token}");
    }

    private static object AddressBody(
        string label = "Nhà", string name = "Nguyễn Văn An", string phone = "0901234567",
        string provinceCode = ProvinceCode, string wardCode = WardCode, string line = "12 Phố Test", bool? isDefault = null)
    {
        var address = new { provinceCode, wardCode, line };
        return isDefault is null
            ? new { label, name, phone, address }
            : new { label, name, phone, address, isDefault = isDefault.Value };
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? cookie, object? body = null)
    {
        return await _client.SendAsync(body is null && method == HttpMethod.Get
            ? ActorHttp.CreateRequest(method, path, cookie)
            : ActorHttp.CreateJsonRequest(method, path, body, cookie));
    }

    // Gửi chuỗi JSON thô, để test body mà serializer của test không tạo ra được (mảng, kiểu sai).
    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, string cookie, string json)
    {
        var request = ActorHttp.CreateRequest(method, path, cookie);
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _client.SendAsync(request);
    }

    private async Task<string> CreateAsync(Customer customer, object? body = null)
    {
        var response = await SendAsync(HttpMethod.Post, AddressesPath, customer.Cookie, body ?? AddressBody());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadDataAsync(response)).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        var document = await ActorHttp.ReadJsonAsync(response);
        return document.RootElement.GetProperty("data").Clone();
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code, string? field = null)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetString());
        // Câu `message` của lỗi kiểm tra có dạng `trường: nội dung`, nên test khẳng định đúng trường bị lỗi.
        if (field is not null)
        {
            Assert.Contains(field + ":", error.GetProperty("message").GetString());
        }
    }

    private async Task<List<SavedAddress>> RowsOfAsync(string userId)
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.SavedAddresses.AsNoTracking().Where(address => address.UserId == userId).ToListAsync();
    }

    // Ghi thẳng `count` địa chỉ hợp lệ, để test giới hạn không phải gọi route 10 lần.
    private async Task SeedAddressesAsync(string userId, int count)
    {
        await using var commerce = _database.CreateCommerceContext();
        for (var index = 0; index < count; index++)
        {
            commerce.SavedAddresses.Add(new SavedAddress
            {
                UserId = userId,
                Label = $"Địa chỉ {index:00}",
                Name = "Người nhận",
                Phone = "0901234567",
                Address = JsonSerializer.SerializeToDocument(new
                {
                    provinceCode = ProvinceCode, provinceName = "Hồ Chí Minh",
                    wardCode = WardCode, wardName = "Phường Bến Nghé", line = $"{index} Phố Test",
                }),
                IsDefault = index == 0,
            });
        }

        await commerce.SaveChangesAsync();
    }

    // ---------- Quyền truy cập ----------

    [Theory]
    [InlineData("GET", "")]
    [InlineData("POST", "")]
    [InlineData("PATCH", "/some-id")]
    [InlineData("DELETE", "/some-id")]
    // Khách chưa đăng nhập không chạm tới logic của route nào trong sổ địa chỉ.
    public async Task AnyRoute_WithoutSession_Returns401(string method, string suffix)
    {
        var response = await SendAsync(new HttpMethod(method), AddressesPath + suffix, cookie: null, AddressBody());

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("POST", "")]
    [InlineData("PATCH", "/some-id")]
    [InlineData("DELETE", "/some-id")]
    // Sổ địa chỉ là của khách mua hàng. Tài khoản nhân viên bị từ chối và không tạo dữ liệu.
    public async Task AnyRoute_WithStaffSession_Returns403(string method, string suffix)
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(_database, "support");
        _userIds.Add(staff.Id);

        var response = await SendAsync(new HttpMethod(method), AddressesPath + suffix, $"pk_session={token}", AddressBody());

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Empty(await RowsOfAsync(staff.Id));
    }

    // ---------- Tạo ----------

    [Fact]
    // Địa chỉ đầu tiên tự là mặc định, và tên tỉnh, phường lấy từ danh mục chứ không từ client.
    public async Task Create_FirstAddress_BecomesDefaultWithCanonicalNames()
    {
        var customer = await NewCustomerAsync();

        var response = await SendAsync(HttpMethod.Post, AddressesPath, customer.Cookie,
            AddressBody(phone: "+84 901 234 567", isDefault: false));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await ReadDataAsync(response);
        Assert.True(data.GetProperty("isDefault").GetBoolean());
        Assert.Equal("Nhà", data.GetProperty("label").GetString());
        Assert.Equal("Nguyễn Văn An", data.GetProperty("name").GetString());
        // Số điện thoại lưu ở dạng chuẩn như đơn hàng.
        Assert.Equal("0901234567", data.GetProperty("phone").GetString());
        var address = data.GetProperty("address");
        Assert.Equal(ProvinceCode, address.GetProperty("provinceCode").GetString());
        Assert.Equal("Hồ Chí Minh", address.GetProperty("provinceName").GetString());
        Assert.Equal(WardCode, address.GetProperty("wardCode").GetString());
        Assert.Equal("Phường Bến Nghé", address.GetProperty("wardName").GetString());
        Assert.Equal("12 Phố Test", address.GetProperty("line").GetString());

        var row = Assert.Single(await RowsOfAsync(customer.Id));
        Assert.Equal(data.GetProperty("id").GetString(), row.Id);
        Assert.Equal("Phường Bến Nghé", row.Address.RootElement.GetProperty("wardName").GetString());
    }

    [Fact]
    // Địa chỉ thứ hai chọn làm mặc định thì địa chỉ cũ bỏ cờ trong cùng thao tác.
    public async Task Create_SecondAddressAsDefault_ClearsPreviousDefault()
    {
        var customer = await NewCustomerAsync();
        var firstId = await CreateAsync(customer, AddressBody(label: "Nhà"));

        var secondId = await CreateAsync(customer, AddressBody(label: "Công ty", isDefault: true));

        var rows = await RowsOfAsync(customer.Id);
        Assert.Equal(2, rows.Count);
        Assert.False(rows.Single(row => row.Id == firstId).IsDefault);
        Assert.True(rows.Single(row => row.Id == secondId).IsDefault);
    }

    [Fact]
    // Địa chỉ thứ hai không xin làm mặc định thì địa chỉ đầu giữ cờ.
    public async Task Create_SecondAddressWithoutDefault_KeepsPreviousDefault()
    {
        var customer = await NewCustomerAsync();
        var firstId = await CreateAsync(customer, AddressBody(label: "Nhà"));

        var secondId = await CreateAsync(customer, AddressBody(label: "Công ty"));

        var rows = await RowsOfAsync(customer.Id);
        Assert.True(rows.Single(row => row.Id == firstId).IsDefault);
        Assert.False(rows.Single(row => row.Id == secondId).IsDefault);
    }

    [Fact]
    // Địa chỉ thứ 11 bị từ chối bằng 422 và sổ giữ nguyên 10 địa chỉ.
    public async Task Create_EleventhAddress_Returns422AndKeepsTen()
    {
        var customer = await NewCustomerAsync();
        await SeedAddressesAsync(customer.Id, 10);

        var response = await SendAsync(HttpMethod.Post, AddressesPath, customer.Cookie, AddressBody());

        await AssertErrorAsync(response, (HttpStatusCode)422, "ADDRESS_LIMIT_REACHED");
        Assert.Equal(10, (await RowsOfAsync(customer.Id)).Count);
    }

    [Fact]
    // Địa chỉ thứ 10 vẫn được tạo. Test này bắt lỗi lệch một đơn vị ở bước đếm.
    public async Task Create_TenthAddress_Returns201()
    {
        var customer = await NewCustomerAsync();
        await SeedAddressesAsync(customer.Id, 9);

        var response = await SendAsync(HttpMethod.Post, AddressesPath, customer.Cookie, AddressBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(10, (await RowsOfAsync(customer.Id)).Count);
    }

    [Fact]
    // Giới hạn tính theo từng khách. Sổ đầy của khách khác không chặn khách này.
    public async Task Create_LimitCountsOnlyTheCallersAddresses()
    {
        var full = await NewCustomerAsync();
        var other = await NewCustomerAsync();
        await SeedAddressesAsync(full.Id, 10);

        var response = await SendAsync(HttpMethod.Post, AddressesPath, other.Cookie, AddressBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData(ProvinceCode, "9999999")]
    [InlineData("9999999", WardCode)]
    // Phường thuộc tỉnh khác.
    [InlineData(ProvinceCode, OtherProvinceWardCode)]
    // Phường đang tắt trong danh mục.
    [InlineData(ProvinceCode, InactiveWardCode)]
    // Mã không có trong danh mục shipping bị từ chối bằng 400 và không tạo địa chỉ.
    public async Task Create_CodesNotInCatalog_Returns400(string provinceCode, string wardCode)
    {
        var customer = await NewCustomerAsync();

        var response = await SendAsync(HttpMethod.Post, AddressesPath, customer.Cookie,
            AddressBody(provinceCode: provinceCode, wardCode: wardCode));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", "address");
        Assert.Empty(await RowsOfAsync(customer.Id));
    }

    [Theory]
    [InlineData("""{"name":"An","phone":"0901234567","address":{"provinceCode":"1000001","wardCode":"1003646","line":"12 Phố"}}""", "label")]
    [InlineData("""{"label":"   ","name":"An","phone":"0901234567","address":{"provinceCode":"1000001","wardCode":"1003646","line":"12 Phố"}}""", "label")]
    [InlineData("""{"label":"Nhà","phone":"0901234567","address":{"provinceCode":"1000001","wardCode":"1003646","line":"12 Phố"}}""", "name")]
    [InlineData("""{"label":"Nhà","name":"An","address":{"provinceCode":"1000001","wardCode":"1003646","line":"12 Phố"}}""", "phone")]
    [InlineData("""{"label":"Nhà","name":"An","phone":"123","address":{"provinceCode":"1000001","wardCode":"1003646","line":"12 Phố"}}""", "phone")]
    [InlineData("""{"label":"Nhà","name":"An","phone":"0901234567"}""", "address")]
    [InlineData("""{"label":"Nhà","name":"An","phone":"0901234567","address":{"provinceCode":"1000001","wardCode":"1003646"}}""", "address.line")]
    [InlineData("""{"label":"Nhà","name":"An","phone":"0901234567","address":{"provinceCode":1000001,"wardCode":"1003646","line":"12 Phố"}}""", "address.provinceCode")]
    [InlineData("""{"label":"Nhà","name":"An","phone":"0901234567","isDefault":"yes","address":{"provinceCode":"1000001","wardCode":"1003646","line":"12 Phố"}}""", "isDefault")]
    [InlineData("""{"label":"Nhà","name":"An","phone":"0901234567","color":"red","address":{"provinceCode":"1000001","wardCode":"1003646","line":"12 Phố"}}""", "color")]
    [InlineData("""{"label":5,"name":"An","phone":"0901234567","address":{"provinceCode":"1000001","wardCode":"1003646","line":"12 Phố"}}""", "label")]
    // Body sai theo từng trường bị từ chối bằng 400 đúng trường, và không tạo địa chỉ.
    public async Task Create_InvalidBody_Returns400ForTheField(string json, string field)
    {
        var customer = await NewCustomerAsync();

        var response = await SendRawAsync(HttpMethod.Post, AddressesPath, customer.Cookie, json);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", field);
        Assert.Empty(await RowsOfAsync(customer.Id));
    }

    [Fact]
    // Nhãn dài 50 ký tự được nhận, 51 ký tự bị từ chối. Khoảng trắng hai đầu được cắt trước khi đếm.
    public async Task Create_LabelLengthBoundary_Is50Characters()
    {
        var customer = await NewCustomerAsync();

        var accepted = await SendAsync(HttpMethod.Post, AddressesPath, customer.Cookie,
            AddressBody(label: "  " + new string('a', 50) + "  "));
        var rejected = await SendAsync(HttpMethod.Post, AddressesPath, customer.Cookie,
            AddressBody(label: new string('a', 51)));

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(new string('a', 50), (await ReadDataAsync(accepted)).GetProperty("label").GetString());
        await AssertErrorAsync(rejected, HttpStatusCode.BadRequest, "VALIDATION_ERROR", "label");
    }

    [Fact]
    // Body không phải đối tượng JSON bị từ chối bằng 400.
    public async Task Create_ArrayBody_Returns400()
    {
        var customer = await NewCustomerAsync();

        var response = await SendRawAsync(HttpMethod.Post, AddressesPath, customer.Cookie, "[]");

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    // ---------- Đọc ----------

    [Fact]
    // `GET` chỉ trả địa chỉ của khách đang đăng nhập: mặc định trước, rồi theo nhãn, rồi theo `Id`.
    public async Task Get_ReturnsOnlyOwnAddresses_DefaultFirstThenLabelThenId()
    {
        var customer = await NewCustomerAsync();
        var other = await NewCustomerAsync();
        await CreateAsync(other, AddressBody(label: "Của người khác"));
        await CreateAsync(customer, AddressBody(label: "Bưu điện"));
        await CreateAsync(customer, AddressBody(label: "An"));
        var defaultId = await CreateAsync(customer, AddressBody(label: "Zebra", isDefault: true));

        // Hai địa chỉ cùng nhãn: thứ tự chốt theo `Id`.
        var sameLabelFirst = await CreateAsync(customer, AddressBody(label: "Trùng"));
        var sameLabelSecond = await CreateAsync(customer, AddressBody(label: "Trùng"));

        var response = await SendAsync(HttpMethod.Get, AddressesPath, customer.Cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync(response);
        var labels = data.EnumerateArray().Select(item => item.GetProperty("label").GetString()).ToList();
        Assert.Equal(["Zebra", "An", "Bưu điện", "Trùng", "Trùng"], labels);
        Assert.Equal(defaultId, data[0].GetProperty("id").GetString());
        var sameLabelIds = data.EnumerateArray().Skip(3).Select(item => item.GetProperty("id").GetString()!).ToList();
        Assert.Equal(new[] { sameLabelFirst, sameLabelSecond }.OrderBy(id => id, StringComparer.Ordinal).ToList(), sameLabelIds);
    }

    [Fact]
    // Khách chưa có địa chỉ nhận danh sách rỗng, không nhận lỗi.
    public async Task Get_WithoutAddresses_ReturnsEmptyList()
    {
        var customer = await NewCustomerAsync();

        var response = await SendAsync(HttpMethod.Get, AddressesPath, customer.Cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await ReadDataAsync(response)).EnumerateArray());
    }

    // ---------- Sửa ----------

    [Fact]
    // `PATCH` đổi một trường, mọi trường khác giữ nguyên.
    public async Task Patch_ChangesOnlyTheGivenField()
    {
        var customer = await NewCustomerAsync();
        var id = await CreateAsync(customer);
        var before = Assert.Single(await RowsOfAsync(customer.Id));

        var response = await SendAsync(new HttpMethod("PATCH"), $"{AddressesPath}/{id}", customer.Cookie, new { label = "Công ty" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync(response);
        Assert.Equal("Công ty", data.GetProperty("label").GetString());
        var after = Assert.Single(await RowsOfAsync(customer.Id));
        Assert.Equal("Công ty", after.Label);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.Phone, after.Phone);
        Assert.Equal(before.IsDefault, after.IsDefault);
        Assert.Equal(before.Address.RootElement.GetRawText(), after.Address.RootElement.GetRawText());
    }

    [Fact]
    // Đổi địa chỉ thì mã được kiểm lại và tên chuẩn được lưu lại, như lúc tạo.
    public async Task Patch_Address_RevalidatesAndRefreshesNames()
    {
        var customer = await NewCustomerAsync();
        var id = await CreateAsync(customer);

        var response = await SendAsync(new HttpMethod("PATCH"), $"{AddressesPath}/{id}", customer.Cookie, new
        {
            address = new { provinceCode = OtherProvinceCode, wardCode = OtherProvinceWardCode, line = "1 Hoàng Diệu" },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var address = (await ReadDataAsync(response)).GetProperty("address");
        Assert.Equal("Hà Nội", address.GetProperty("provinceName").GetString());
        Assert.Equal("Phường Ba Đình", address.GetProperty("wardName").GetString());
        Assert.Equal("1 Hoàng Diệu", address.GetProperty("line").GetString());
        var row = Assert.Single(await RowsOfAsync(customer.Id));
        Assert.Equal("Phường Ba Đình", row.Address.RootElement.GetProperty("wardName").GetString());
    }

    [Fact]
    // Đổi địa chỉ sang mã không có trong danh mục bị từ chối và địa chỉ cũ giữ nguyên.
    public async Task Patch_AddressNotInCatalog_Returns400AndChangesNothing()
    {
        var customer = await NewCustomerAsync();
        var id = await CreateAsync(customer);
        var before = Assert.Single(await RowsOfAsync(customer.Id));

        var response = await SendAsync(new HttpMethod("PATCH"), $"{AddressesPath}/{id}", customer.Cookie, new
        {
            label = "Đổi nhãn",
            address = new { provinceCode = ProvinceCode, wardCode = "9999999", line = "1 Hoàng Diệu" },
        });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", "address");
        var after = Assert.Single(await RowsOfAsync(customer.Id));
        Assert.Equal(before.Label, after.Label);
        Assert.Equal(before.Address.RootElement.GetRawText(), after.Address.RootElement.GetRawText());
    }

    [Fact]
    // Chọn địa chỉ khác làm mặc định thì địa chỉ mặc định cũ bỏ cờ.
    public async Task Patch_MakeDefault_ClearsPreviousDefault()
    {
        var customer = await NewCustomerAsync();
        var firstId = await CreateAsync(customer, AddressBody(label: "Nhà"));
        var secondId = await CreateAsync(customer, AddressBody(label: "Công ty"));

        var response = await SendAsync(new HttpMethod("PATCH"), $"{AddressesPath}/{secondId}", customer.Cookie, new { isDefault = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await RowsOfAsync(customer.Id);
        Assert.False(rows.Single(row => row.Id == firstId).IsDefault);
        Assert.True(rows.Single(row => row.Id == secondId).IsDefault);
    }

    [Fact]
    // Bỏ cờ mặc định chỉ bỏ cờ, không tự chọn địa chỉ khác làm mặc định.
    public async Task Patch_ClearDefault_LeavesNoDefault()
    {
        var customer = await NewCustomerAsync();
        var firstId = await CreateAsync(customer, AddressBody(label: "Nhà"));
        await CreateAsync(customer, AddressBody(label: "Công ty"));

        var response = await SendAsync(new HttpMethod("PATCH"), $"{AddressesPath}/{firstId}", customer.Cookie, new { isDefault = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(await RowsOfAsync(customer.Id), row => row.IsDefault);
    }

    [Fact]
    // Gửi `isDefault: true` cho địa chỉ đã là mặc định không đổi gì. Bước bỏ cờ không được xóa cờ của chính địa chỉ đó.
    public async Task Patch_MakeDefaultOnCurrentDefault_KeepsItDefault()
    {
        var customer = await NewCustomerAsync();
        var firstId = await CreateAsync(customer, AddressBody(label: "Nhà"));
        var secondId = await CreateAsync(customer, AddressBody(label: "Công ty"));

        var response = await SendAsync(new HttpMethod("PATCH"), $"{AddressesPath}/{firstId}", customer.Cookie, new { isDefault = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await ReadDataAsync(response)).GetProperty("isDefault").GetBoolean());

        // Đọc lại bằng `GET` để thấy dữ liệu thật đã lưu, không chỉ response của `PATCH`.
        var list = await ReadDataAsync(await SendAsync(HttpMethod.Get, AddressesPath, customer.Cookie));
        var defaults = list.EnumerateArray().Where(item => item.GetProperty("isDefault").GetBoolean()).ToList();
        Assert.Equal(firstId, Assert.Single(defaults).GetProperty("id").GetString());

        var rows = await RowsOfAsync(customer.Id);
        Assert.True(rows.Single(row => row.Id == firstId).IsDefault);
        Assert.False(rows.Single(row => row.Id == secondId).IsDefault);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"color":"red"}""")]
    [InlineData("""{"label":"Nhà","color":"red"}""")]
    [InlineData("""{"label":5}""")]
    [InlineData("""{"label":"   "}""")]
    [InlineData("""{"label":null}""")]
    [InlineData("""{"name":""}""")]
    [InlineData("""{"phone":"123"}""")]
    [InlineData("""{"isDefault":"yes"}""")]
    [InlineData("""{"address":{"provinceCode":"1000001"}}""")]
    [InlineData("[]")]
    // Body rỗng, trường lạ hoặc sai kiểu bị từ chối bằng 400, kể cả khi đi kèm trường hợp lệ. Địa chỉ giữ nguyên.
    public async Task Patch_InvalidBody_Returns400AndChangesNothing(string json)
    {
        var customer = await NewCustomerAsync();
        var id = await CreateAsync(customer);
        var before = Assert.Single(await RowsOfAsync(customer.Id));

        var response = await SendRawAsync(new HttpMethod("PATCH"), $"{AddressesPath}/{id}", customer.Cookie, json);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var after = Assert.Single(await RowsOfAsync(customer.Id));
        Assert.Equal(before.Label, after.Label);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.Phone, after.Phone);
    }

    [Fact]
    // Sửa địa chỉ của khách khác nhận 404 cùng thông báo với `id` không tồn tại, và địa chỉ đó không đổi.
    public async Task Patch_OtherCustomersAddress_Returns404AndChangesNothing()
    {
        var owner = await NewCustomerAsync();
        var intruder = await NewCustomerAsync();
        var id = await CreateAsync(owner);

        var foreign = await SendAsync(new HttpMethod("PATCH"), $"{AddressesPath}/{id}", intruder.Cookie, new { label = "Chiếm", isDefault = false });
        var missing = await SendAsync(new HttpMethod("PATCH"), $"{AddressesPath}/khong-co", intruder.Cookie, new { label = "Chiếm" });

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(await foreign.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
        var row = Assert.Single(await RowsOfAsync(owner.Id));
        Assert.Equal("Nhà", row.Label);
        Assert.True(row.IsDefault);
    }

    // ---------- Xóa ----------

    [Fact]
    // Xóa trả 204 và chỉ ẩn địa chỉ đó khỏi sổ. Xóa lại nhận 404, dòng vẫn còn với `deletedAt`.
    public async Task Delete_SoftDeletesOnlyThatAddress()
    {
        var customer = await NewCustomerAsync();
        var firstId = await CreateAsync(customer, AddressBody(label: "Nhà"));
        var secondId = await CreateAsync(customer, AddressBody(label: "Công ty"));

        var response = await SendAsync(HttpMethod.Delete, $"{AddressesPath}/{secondId}", customer.Cookie);
        var again = await SendAsync(HttpMethod.Delete, $"{AddressesPath}/{secondId}", customer.Cookie);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(firstId, Assert.Single(await RowsOfAsync(customer.Id)).Id);

        // Dòng đã xóa vẫn còn trong DB để đối soát, nhưng không còn trong sổ và không còn là mặc định.
        await using var commerce = _database.CreateCommerceContext();
        var deleted = await commerce.SavedAddresses.IgnoreQueryFilters().SingleAsync(address => address.Id == secondId);
        Assert.NotNull(deleted.DeletedAt);
        Assert.False(deleted.IsDefault);
    }

    [Fact]
    // Xóa địa chỉ mặc định không tự chọn địa chỉ khác làm mặc định.
    public async Task Delete_DefaultAddress_LeavesNoDefault()
    {
        var customer = await NewCustomerAsync();
        var defaultId = await CreateAsync(customer, AddressBody(label: "Nhà"));
        await CreateAsync(customer, AddressBody(label: "Công ty"));

        var response = await SendAsync(HttpMethod.Delete, $"{AddressesPath}/{defaultId}", customer.Cookie);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var rows = await RowsOfAsync(customer.Id);
        Assert.Single(rows);
        Assert.DoesNotContain(rows, row => row.IsDefault);
    }

    [Fact]
    // Xóa địa chỉ của khách khác nhận 404 và địa chỉ đó còn nguyên.
    public async Task Delete_OtherCustomersAddress_Returns404AndChangesNothing()
    {
        var owner = await NewCustomerAsync();
        var intruder = await NewCustomerAsync();
        var id = await CreateAsync(owner);

        var response = await SendAsync(HttpMethod.Delete, $"{AddressesPath}/{id}", intruder.Cookie);

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "ADDRESS_NOT_FOUND");
        Assert.Single(await RowsOfAsync(owner.Id));
    }

    // ---------- Đồng thời ----------

    // Giữ khóa tư vấn của khách trong một transaction riêng. Request ghi phải chờ tới khi test nhả khóa.
    private async Task<(Task<HttpResponseMessage[]> Pending, bool FinishedWhileLocked)> RunWhileLockedAsync(
        string userId, params Func<Task<HttpResponseMessage>>[] requests)
    {
        await using var holder = _database.CreateCommerceContext();
        await using var holderTransaction = await holder.Database.BeginTransactionAsync();
        var lockName = "saved_addresses:" + userId;
        await holder.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({lockName}))");

        var pending = Task.WhenAll(requests.Select(request => request()));
        var finishedWhileLocked = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(2))) == pending;

        await holderTransaction.CommitAsync();
        await pending;
        return (pending, finishedWhileLocked);
    }

    [Fact]
    // Hai request cùng đặt hai địa chỉ khác nhau làm mặc định. Cuối cùng chỉ có một địa chỉ mặc định.
    public async Task Patch_ConcurrentMakeDefault_LeavesExactlyOneDefault()
    {
        var customer = await NewCustomerAsync();
        await CreateAsync(customer, AddressBody(label: "Nhà"));
        var secondId = await CreateAsync(customer, AddressBody(label: "Công ty"));
        var thirdId = await CreateAsync(customer, AddressBody(label: "Kho"));

        var (pending, finishedWhileLocked) = await RunWhileLockedAsync(customer.Id,
            () => SendAsync(new HttpMethod("PATCH"), $"{AddressesPath}/{secondId}", customer.Cookie, new { isDefault = true }),
            () => SendAsync(new HttpMethod("PATCH"), $"{AddressesPath}/{thirdId}", customer.Cookie, new { isDefault = true }));

        Assert.False(finishedWhileLocked, "Request ghi phải chờ khóa của khách.");
        Assert.All(await pending, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var rows = await RowsOfAsync(customer.Id);
        Assert.Equal(3, rows.Count);
        Assert.Single(rows, row => row.IsDefault);
    }

    [Fact]
    // Khách chưa có địa chỉ nào: khóa dòng không có dòng để khóa. Hai request tạo cùng lúc vẫn chỉ cho một mặc định.
    public async Task Create_ConcurrentFirstAddresses_LeavesExactlyOneDefault()
    {
        var customer = await NewCustomerAsync();

        var (pending, finishedWhileLocked) = await RunWhileLockedAsync(customer.Id,
            () => SendAsync(HttpMethod.Post, AddressesPath, customer.Cookie, AddressBody(label: "Nhà", isDefault: true)),
            () => SendAsync(HttpMethod.Post, AddressesPath, customer.Cookie, AddressBody(label: "Công ty", isDefault: true)));

        Assert.False(finishedWhileLocked, "Request ghi phải chờ khóa của khách.");
        Assert.All(await pending, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var rows = await RowsOfAsync(customer.Id);
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, row => row.IsDefault);
    }

    [Fact]
    // Khách có 9 địa chỉ, hai request tạo cùng lúc: một request nhận 201, một request nhận 422, sổ có đúng 10.
    public async Task Create_ConcurrentAtNineAddresses_AllowsExactlyOne()
    {
        var customer = await NewCustomerAsync();
        await SeedAddressesAsync(customer.Id, 9);

        var (pending, finishedWhileLocked) = await RunWhileLockedAsync(customer.Id,
            () => SendAsync(HttpMethod.Post, AddressesPath, customer.Cookie, AddressBody(label: "Mới 1")),
            () => SendAsync(HttpMethod.Post, AddressesPath, customer.Cookie, AddressBody(label: "Mới 2")));

        Assert.False(finishedWhileLocked, "Request ghi phải chờ khóa của khách.");
        var statusCodes = (await pending).Select(response => (int)response.StatusCode).OrderBy(code => code).ToList();
        Assert.Equal([201, 422], statusCodes);
        Assert.Equal(10, (await RowsOfAsync(customer.Id)).Count);
    }
}
