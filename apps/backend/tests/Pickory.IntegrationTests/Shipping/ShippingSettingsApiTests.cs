using System.Data.Common;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Database;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-06: route cấu hình shipping của back-office, gồm quyền, đọc, ghi, kiểm danh mục và audit.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ShippingSettingsApiTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string SettingsPath = "/api/admin/shipping/settings";
    private const string AuditTargetType = "shipping_settings";
    private static readonly DateTime SyncedAt = new(2026, 10, 10, 6, 30, 15, DateTimeKind.Utc);

    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public ShippingSettingsApiTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        // Client không tự giữ cookie, nên mỗi request mang đúng phiên mà test chỉ định.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync() => await ResetAsync();

    public async Task DisposeAsync() => await ResetAsync();

    private async Task ResetAsync()
    {
        await WithShippingAsync(async shipping =>
        {
            await shipping.Settings.ExecuteDeleteAsync();
            await shipping.AddressWards.ExecuteDeleteAsync();
            await shipping.AddressProvinces.ExecuteDeleteAsync();
        });

        await using var identity = _database.CreateIdentityContext();
        await identity.AuditLogs.Where(log => log.TargetType == AuditTargetType).ExecuteDeleteAsync();
    }

    private async Task WithShippingAsync(Func<ShippingDbContext, Task> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        // Test xóa cả bảng, nên phải chắc context trỏ tới DB test chứ không phải DB dev.
        DotnetSchemaIntegrationTests.AssertTestConnection(shipping);
        await action(shipping);
    }

    // Dựng danh mục có hai tỉnh/thành đang dùng, một tỉnh/thành đã tắt, và các phường/xã để thử từng luật.
    private Task SeedCatalogAsync() =>
        WithShippingAsync(async shipping =>
        {
            shipping.AddressProvinces.AddRange(
                AddressTestDatabase.Province("01", "Hà Nội", isActive: true, SyncedAt),
                AddressTestDatabase.Province("79", "Hồ Chí Minh", isActive: true, SyncedAt),
                AddressTestDatabase.Province("99", "Tỉnh đã tắt", isActive: false, SyncedAt));
            await shipping.SaveChangesAsync();
            shipping.AddressWards.AddRange(
                AddressTestDatabase.Ward("0101", "01", "Phường Ba Đình", isActive: true, SyncedAt),
                AddressTestDatabase.Ward("0102", "01", "Phường đã tắt", isActive: false, SyncedAt),
                AddressTestDatabase.Ward("7901", "79", "Phường Bến Nghé", isActive: true, SyncedAt),
                AddressTestDatabase.Ward("9901", "99", "Phường của tỉnh đã tắt", isActive: true, SyncedAt));
            await shipping.SaveChangesAsync();
        });

    private static Dictionary<string, object?> ValidBody() => new()
    {
        ["senderName"] = "Kho Pickory",
        ["senderPhone"] = "0912345678",
        ["provinceCode"] = "01",
        ["wardCode"] = "0101",
        ["line"] = "12 Phố Test",
        ["defaultServiceTypeId"] = 2,
    };

    private async Task<ShippingSettings?> ReadStoredSettingsAsync()
    {
        ShippingSettings? stored = null;
        await WithShippingAsync(async shipping => stored = await shipping.Settings.AsNoTracking().SingleOrDefaultAsync());
        return stored;
    }

    private async Task<int> CountStoredRowsAsync()
    {
        var rowCount = 0;
        await WithShippingAsync(async shipping => rowCount = await shipping.Settings.CountAsync());
        return rowCount;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string? session, object? body = null) =>
        _client.SendAsync(ActorHttp.CreateJsonRequest(method, SettingsPath, body, session is null ? null : $"pk_session={session}"));

    private async Task<string> CreateStaffSessionAsync(string roleCode) =>
        (await IdentityTestData.CreateStaffSessionAsync(_database, roleCode, "ship06")).SessionToken;

    [Fact]
    // Chưa cấu hình thì `settings` là `null`. Thiếu `GHN_SHOP_ID` thì `ghnShopId` cũng là `null`.
    public async Task Get_WhenNotConfigured_ReturnsNullSettingsAndNullShopId()
    {
        var adminSession = await CreateStaffSessionAsync("admin");

        var response = await SendAsync(HttpMethod.Get, adminSession);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("settings").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("ghnShopId").ValueKind);
    }

    [Theory]
    [InlineData("support")]
    [InlineData("sales")]
    // `support` và `sales` không có `shipping-settings:manage`, nên không đọc được cấu hình.
    public async Task Get_WithoutPermission_Returns403(string roleCode)
    {
        var session = await CreateStaffSessionAsync(roleCode);

        var response = await SendAsync(HttpMethod.Get, session);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Khách vãng lai chưa đăng nhập nhận 401, giống mọi route admin khác.
    public async Task Get_AsGuest_Returns401()
    {
        var response = await SendAsync(HttpMethod.Get, session: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("support")]
    [InlineData("sales")]
    // `support` và `sales` không sửa được cấu hình. Dòng cấu hình không được tạo ra.
    public async Task Put_WithoutPermission_Returns403AndSavesNothing(string roleCode)
    {
        await SeedCatalogAsync();
        var session = await CreateStaffSessionAsync(roleCode);

        var response = await SendAsync(HttpMethod.Put, session, ValidBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(await ReadStoredSettingsAsync());
    }

    [Fact]
    public async Task Put_AsGuest_Returns401()
    {
        var response = await SendAsync(HttpMethod.Put, session: null, ValidBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    // Số điện thoại dạng `+84` lưu về `0xxxxxxxxx`. Tên tỉnh và phường lấy từ danh mục, không từ body.
    public async Task Put_Valid_StoresNormalizedPhoneAndCatalogNames()
    {
        await SeedCatalogAsync();
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body["senderName"] = "  Kho Pickory  ";
        body["senderPhone"] = "+84 912 345 678";
        body["line"] = "  12 Phố Test  ";
        var beforeRequest = DateTime.UtcNow.AddSeconds(-1);

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var document = await ActorHttp.ReadJsonAsync(response))
        {
            var settings = document.RootElement.GetProperty("data").GetProperty("settings");
            Assert.Equal("Kho Pickory", settings.GetProperty("senderName").GetString());
            Assert.Equal("0912345678", settings.GetProperty("senderPhone").GetString());
            Assert.Equal("Hà Nội", settings.GetProperty("provinceName").GetString());
            Assert.Equal("Phường Ba Đình", settings.GetProperty("wardName").GetString());
            Assert.Equal("12 Phố Test", settings.GetProperty("line").GetString());
            Assert.Equal(2, settings.GetProperty("defaultServiceTypeId").GetInt32());
        }

        var stored = await ReadStoredSettingsAsync();
        Assert.NotNull(stored);
        Assert.Equal(
            ("Kho Pickory", "0912345678", "01", "Hà Nội", "0101", "Phường Ba Đình", "12 Phố Test", 2),
            (stored.SenderName, stored.SenderPhone, stored.ProvinceCode, stored.ProvinceName, stored.WardCode, stored.WardName, stored.Line, stored.DefaultServiceTypeId));
        Assert.True(stored.UpdatedAt >= beforeRequest, "`UpdatedAt` phải là thời điểm lưu.");
    }

    [Fact]
    // GET sau PUT trả đúng dữ liệu đã lưu. Gói dịch vụ trống được lưu là `null`.
    public async Task Get_AfterPut_ReturnsStoredSettings()
    {
        await SeedCatalogAsync();
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body["defaultServiceTypeId"] = null;
        var putResponse = await SendAsync(HttpMethod.Put, adminSession, body);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var response = await SendAsync(HttpMethod.Get, adminSession);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var settings = document.RootElement.GetProperty("data").GetProperty("settings");
        Assert.Equal("Kho Pickory", settings.GetProperty("senderName").GetString());
        Assert.Equal("0912345678", settings.GetProperty("senderPhone").GetString());
        Assert.Equal("01", settings.GetProperty("provinceCode").GetString());
        Assert.Equal("0101", settings.GetProperty("wardCode").GetString());
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("defaultServiceTypeId").ValueKind);
        Assert.Equal(JsonValueKind.String, settings.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    // PUT lần hai sửa đúng dòng duy nhất, không thêm dòng mới.
    public async Task Put_Twice_KeepsSingleRowWithLatestValues()
    {
        await SeedCatalogAsync();
        var adminSession = await CreateStaffSessionAsync("admin");
        var first = await SendAsync(HttpMethod.Put, adminSession, ValidBody());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = ValidBody();
        second["provinceCode"] = "79";
        second["wardCode"] = "7901";
        second["defaultServiceTypeId"] = 5;

        var response = await SendAsync(HttpMethod.Put, adminSession, second);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await CountStoredRowsAsync());
        var stored = await ReadStoredSettingsAsync();
        Assert.Equal(
            ("79", "Hồ Chí Minh", "7901", "Phường Bến Nghé", 5),
            (stored!.ProvinceCode, stored.ProvinceName, stored.WardCode, stored.WardName, stored.DefaultServiceTypeId));
    }

    // Mỗi dòng: tên test, trường bị sửa, giá trị sai, và khóa trường phải có trong thông báo lỗi.
    public static TheoryData<string, string, object?, string> InvalidFieldCases => new()
    {
        { "senderName trống", "senderName", "   ", "senderName" },
        { "senderName thiếu", "senderName", null, "senderName" },
        { "senderName quá 100 ký tự", "senderName", new string('a', 101), "senderName" },
        { "senderName có ký tự điều khiển", "senderName", "Kho\nPickory", "senderName" },
        { "senderName sai kiểu", "senderName", 123, "senderName" },
        { "senderPhone trống", "senderPhone", "", "senderPhone" },
        { "senderPhone sai định dạng", "senderPhone", "0212345678", "senderPhone" },
        { "senderPhone không phải số", "senderPhone", "abc", "senderPhone" },
        { "provinceCode trống", "provinceCode", "", "provinceCode" },
        { "provinceCode không có trong danh mục", "provinceCode", "00", "provinceCode" },
        { "provinceCode đã tắt", "provinceCode", "99", "provinceCode" },
        { "wardCode trống", "wardCode", "", "wardCode" },
        { "wardCode không có trong danh mục", "wardCode", "000000", "wardCode" },
        { "wardCode đã tắt", "wardCode", "0102", "wardCode" },
        { "wardCode thuộc tỉnh khác", "wardCode", "7901", "wardCode" },
        { "line trống", "line", "  ", "line" },
        { "line quá 200 ký tự", "line", new string('a', 201), "line" },
        { "line có ký tự điều khiển", "line", "12 Phố\tTest", "line" },
        { "defaultServiceTypeId là 3", "defaultServiceTypeId", 3, "defaultServiceTypeId" },
        { "defaultServiceTypeId là chuỗi", "defaultServiceTypeId", "2", "defaultServiceTypeId" },
        { "defaultServiceTypeId là số lẻ", "defaultServiceTypeId", 2.5, "defaultServiceTypeId" },
    };

    [Theory]
    [MemberData(nameof(InvalidFieldCases))]
    // Mỗi luật của bảng body trả 400 `VALIDATION_ERROR` với khóa trường sai. Cấu hình và audit không đổi.
    public async Task Put_InvalidField_Returns400AndChangesNothing(string caseName, string field, object? value, string expectedFieldKey)
    {
        _ = caseName;
        await SeedCatalogAsync();
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body[field] = value;

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var document = await ActorHttp.ReadJsonAsync(response))
        {
            var error = document.RootElement.GetProperty("error");
            Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
            Assert.Contains(expectedFieldKey + ":", error.GetProperty("message").GetString());
        }

        Assert.Equal(0, await CountStoredRowsAsync());
        Assert.Equal(0, await CountAuditRowsAsync());
    }

    [Fact]
    // Phường của tỉnh đã tắt bị từ chối khi gửi kèm một tỉnh đang dùng khác, vì phường không thuộc tỉnh đó.
    public async Task Put_WardOfInactiveProvince_Returns400()
    {
        await SeedCatalogAsync();
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body["wardCode"] = "9901";

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountStoredRowsAsync());
    }

    [Fact]
    // Trường lạ bị từ chối, kể cả `provinceName` mà client tự gửi. Tên phải lấy từ danh mục.
    public async Task Put_UnknownFieldIncludingProvinceName_Returns400()
    {
        await SeedCatalogAsync();
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body["provinceName"] = "Tên do client tự đặt";

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Contains("provinceName:", document.RootElement.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(0, await CountStoredRowsAsync());
    }

    [Fact]
    // Nhiều trường sai thì một phản hồi 400 liệt kê đủ từng khóa, để UI hiện lỗi dưới đúng từng ô.
    public async Task Put_MultipleInvalidFields_ReportsEveryFieldInOneResponse()
    {
        await SeedCatalogAsync();
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body["senderName"] = "";
        body["senderPhone"] = "123";
        body["wardCode"] = "7901";

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var message = document.RootElement.GetProperty("error").GetProperty("message").GetString();
        Assert.Contains("senderName:", message);
        Assert.Contains("senderPhone:", message);
        Assert.Contains("wardCode:", message);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"chuỗi\"")]
    [InlineData("null")]
    // Body không phải đối tượng JSON bị từ chối, và cấu hình không đổi.
    public async Task Put_BodyIsNotAnObject_Returns400(string rawJson)
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        // Helper đã có `Origin`. Thân được thay bằng JSON thô, vì helper luôn tuần tự hóa `object`.
        using var request = ActorHttp.CreateJsonRequest(HttpMethod.Put, SettingsPath, body: null, $"pk_session={adminSession}");
        request.Content = new StringContent(rawJson, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountStoredRowsAsync());
    }

    [Fact]
    // `defaultServiceTypeId` được phép là 5 (hàng nặng).
    public async Task Put_ServiceTypeFive_IsAccepted()
    {
        await SeedCatalogAsync();
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body["defaultServiceTypeId"] = 5;

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(5, (await ReadStoredSettingsAsync())!.DefaultServiceTypeId);
    }

    [Fact]
    // Lần PUT đầu có `before` là `null`. Audit ghi đúng người sửa, hành động và đích.
    public async Task Put_FirstTime_WritesOneAuditRowWithNullBefore()
    {
        await SeedCatalogAsync();
        var (admin, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin", "ship06");

        var response = await SendAsync(HttpMethod.Put, adminSession, ValidBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var identity = _database.CreateIdentityContext();
        var audit = await identity.AuditLogs.AsNoTracking().SingleAsync(log => log.TargetType == AuditTargetType);
        Assert.Equal((admin.Id, "shipping.settings.update", "singleton"), (audit.ActorId, audit.Action, audit.TargetId));
        // Lần đầu chưa có cấu hình cũ. Audit lưu `before` là JSON `null` hoặc không có giá trị.
        Assert.True(audit.Before is null || audit.Before.RootElement.ValueKind == JsonValueKind.Null);
        Assert.Equal("0912345678", audit.After!.RootElement.GetProperty("senderPhone").GetString());
        Assert.Equal("Hà Nội", audit.After.RootElement.GetProperty("provinceName").GetString());
    }

    [Fact]
    // Mỗi PUT hợp lệ ghi đúng 1 dòng, kể cả khi giá trị không đổi. Dòng thứ hai có `before` là cấu hình cũ.
    public async Task Put_SameValuesTwice_WritesOneAuditRowPerRequest()
    {
        await SeedCatalogAsync();
        var adminSession = await CreateStaffSessionAsync("admin");
        await SendAsync(HttpMethod.Put, adminSession, ValidBody());
        var changed = ValidBody();
        changed["senderName"] = "Kho mới";

        var second = await SendAsync(HttpMethod.Put, adminSession, changed);
        var third = await SendAsync(HttpMethod.Put, adminSession, changed);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        await using var identity = _database.CreateIdentityContext();
        var audits = await identity.AuditLogs.AsNoTracking()
            .Where(log => log.TargetType == AuditTargetType)
            .OrderBy(log => log.CreatedAt)
            .ToListAsync();
        Assert.Equal(3, audits.Count);
        Assert.Equal("Kho Pickory", audits[1].Before!.RootElement.GetProperty("senderName").GetString());
        Assert.Equal("Kho mới", audits[1].After!.RootElement.GetProperty("senderName").GetString());
        Assert.Equal("Kho mới", audits[2].Before!.RootElement.GetProperty("senderName").GetString());
        Assert.Equal("Kho mới", audits[2].After!.RootElement.GetProperty("senderName").GetString());
    }

    [Fact]
    // Audit lỗi thì cấu hình đã có không đổi. Hai bản ghi cùng thành công hoặc cùng không có.
    public async Task Put_WhenAuditWriteFails_KeepsPreviousSettings()
    {
        await SeedCatalogAsync();
        var adminSession = await CreateStaffSessionAsync("admin");
        var firstResponse = await SendAsync(HttpMethod.Put, adminSession, ValidBody());
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var failingFactory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuditLogWriter>();
            services.AddSingleton<IAuditLogWriter, ThrowingAuditLogWriter>();
        }));
        using var failingClient = failingFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var changed = ValidBody();
        changed["senderName"] = "Tên không được lưu";
        changed["provinceCode"] = "79";
        changed["wardCode"] = "7901";

        var response = await failingClient.SendAsync(
            ActorHttp.CreateJsonRequest(HttpMethod.Put, SettingsPath, changed, $"pk_session={adminSession}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var stored = await ReadStoredSettingsAsync();
        Assert.Equal(("Kho Pickory", "01", "0101"), (stored!.SenderName, stored.ProvinceCode, stored.WardCode));
        Assert.Equal(1, await CountAuditRowsAsync());
    }

    [Fact]
    // Audit lỗi ở lần cấu hình đầu thì không tạo dòng cấu hình nào.
    public async Task Put_FirstTimeWhenAuditWriteFails_CreatesNoRow()
    {
        await SeedCatalogAsync();
        var adminSession = await CreateStaffSessionAsync("admin");
        using var failingFactory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuditLogWriter>();
            services.AddSingleton<IAuditLogWriter, ThrowingAuditLogWriter>();
        }));
        using var failingClient = failingFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await failingClient.SendAsync(
            ActorHttp.CreateJsonRequest(HttpMethod.Put, SettingsPath, ValidBody(), $"pk_session={adminSession}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, await CountStoredRowsAsync());
    }

    [Fact]
    // Lỗi 400 có `details` có cấu trúc: mỗi lỗi một phần tử `{ key, message }`, để UI gán lỗi vào đúng ô mà không đọc câu `message`.
    public async Task Put_TwoInvalidFields_ReturnsStructuredDetailsPerField()
    {
        await SeedCatalogAsync();
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body["senderPhone"] = "0212345678";
        body["wardCode"] = "7901";

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        // Câu `message` giữ nguyên dạng cũ để các client đang đọc câu này không bị vỡ.
        Assert.StartsWith("Dữ liệu không hợp lệ (", error.GetProperty("message").GetString());
        var details = error.GetProperty("details").EnumerateArray()
            .Select(detail => (detail.GetProperty("key").GetString(), detail.GetProperty("message").GetString()))
            .OrderBy(detail => detail.Item1, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(
            [
                ("senderPhone", "Số điện thoại không đúng định dạng."),
                ("wardCode", "Phường/xã không thuộc tỉnh/thành đã chọn."),
            ],
            details);
    }

    [Fact]
    // Lỗi cả body không thuộc trường nào, nên `key` là `body`.
    public async Task Put_BodyIsNotAnObject_ReturnsDetailsWithBodyKey()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        using var request = ActorHttp.CreateJsonRequest(HttpMethod.Put, SettingsPath, body: null, $"pk_session={adminSession}");
        request.Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var details = document.RootElement.GetProperty("error").GetProperty("details");
        Assert.Equal(1, details.GetArrayLength());
        Assert.Equal("body", details[0].GetProperty("key").GetString());
    }

    [Fact]
    // Lỗi không phải kiểm tra dữ liệu giữ nguyên hình dạng, không thêm `details`.
    public async Task NotFound_KeepsErrorShapeWithoutDetails()
    {
        var adminSession = await CreateStaffSessionAsync("admin");

        var response = await _client.SendAsync(
            ActorHttp.CreateRequest(HttpMethod.Get, "/api/admin/shipping/khong-co", $"pk_session={adminSession}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.False(document.RootElement.GetProperty("error").TryGetProperty("details", out _));
    }

    private async Task<int> CountAuditRowsAsync()
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.AuditLogs.CountAsync(log => log.TargetType == AuditTargetType);
    }

    // Bản ghi audit luôn lỗi. Test dùng nó để kiểm tra transaction hủy cả thay đổi cấu hình.
    private sealed class ThrowingAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogEntry entry, DbTransaction transaction, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Audit lỗi do test cố ý gây ra.");
    }
}
