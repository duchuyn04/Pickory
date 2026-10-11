using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Database;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Contracts.Audit;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-06: route cấu hình commerce của back-office, gồm quyền, đọc, ghi, luật kiểm body và audit.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CommerceSettingsApiTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string SettingsPath = "/api/admin/commerce/settings";
    private const string AuditTargetType = "commerce_settings";
    private const string AuditAction = "commerce.settings.update";

    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public CommerceSettingsApiTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
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
        await using (var commerce = _database.CreateCommerceContext())
        {
            // Test xóa cả bảng, nên phải chắc context trỏ tới DB test chứ không phải DB dev.
            DotnetSchemaIntegrationTests.AssertTestConnection(commerce);
            await commerce.Settings.ExecuteDeleteAsync();
        }

        await using var identity = _database.CreateIdentityContext();
        await identity.AuditLogs.Where(log => log.TargetType == AuditTargetType).ExecuteDeleteAsync();
    }

    private static Dictionary<string, object?> ValidBody() => new()
    {
        ["freeShippingThreshold"] = 700_000,
        ["codMaxAmount"] = 15_000_000,
        ["maxQuantityPerSku"] = 3,
        ["maxCartLines"] = 10,
        ["sepayAccountNumber"] = "0123456789",
        ["sepayBankCode"] = "MB",
    };

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string? session, object? body = null) =>
        _client.SendAsync(ActorHttp.CreateJsonRequest(method, SettingsPath, body, session is null ? null : $"pk_session={session}"));

    private async Task<string> CreateStaffSessionAsync(string roleCode) =>
        (await IdentityTestData.CreateStaffSessionAsync(_database, roleCode, "com06")).SessionToken;

    private async Task<List<CommerceSettings>> ReadStoredRowsAsync()
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.Settings.AsNoTracking().ToListAsync();
    }

    private async Task<int> CountAuditRowsAsync()
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.AuditLogs.CountAsync(log => log.TargetType == AuditTargetType);
    }

    [Fact]
    // Chưa có dòng cấu hình thì trả giá trị mặc định, hai trường SePay và `updatedAt` là `null`.
    public async Task Get_WhenNotConfigured_ReturnsDefaults()
    {
        var adminSession = await CreateStaffSessionAsync("admin");

        var response = await SendAsync(HttpMethod.Get, adminSession);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(500_000, data.GetProperty("freeShippingThreshold").GetInt32());
        Assert.Equal(20_000_000, data.GetProperty("codMaxAmount").GetInt32());
        Assert.Equal(5, data.GetProperty("maxQuantityPerSku").GetInt32());
        Assert.Equal(20, data.GetProperty("maxCartLines").GetInt32());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("sepayAccountNumber").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("sepayBankCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("updatedAt").ValueKind);
        // GET không tạo dòng cấu hình. Chỉ PUT mới ghi.
        Assert.Empty(await ReadStoredRowsAsync());
    }

    [Theory]
    [InlineData("support")]
    [InlineData("sales")]
    // `support` và `sales` không có `commerce-settings:manage`, nên không đọc được cấu hình.
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
    // `support` và `sales` không sửa được cấu hình. Dòng cấu hình và audit không được tạo ra.
    public async Task Put_WithoutPermission_Returns403AndSavesNothing(string roleCode)
    {
        var session = await CreateStaffSessionAsync(roleCode);

        var response = await SendAsync(HttpMethod.Put, session, ValidBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await ReadStoredRowsAsync());
        Assert.Equal(0, await CountAuditRowsAsync());
    }

    [Fact]
    public async Task Put_AsGuest_Returns401()
    {
        var response = await SendAsync(HttpMethod.Put, session: null, ValidBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    // Response của PUT có đúng 7 trường đã chốt, bọc trong `data`, và dữ liệu lưu khớp response.
    public async Task Put_Valid_ReturnsExactFieldsAndStoresValues()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body["sepayBankCode"] = "  vcb ";
        body["sepayAccountNumber"] = " 0123456789 ";
        var beforeRequest = DateTime.UtcNow.AddSeconds(-1);

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var document = await ActorHttp.ReadJsonAsync(response))
        {
            var data = document.RootElement.GetProperty("data");
            var fieldNames = data.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal);
            Assert.Equal(
                ["codMaxAmount", "freeShippingThreshold", "maxCartLines", "maxQuantityPerSku", "sepayAccountNumber", "sepayBankCode", "updatedAt"],
                fieldNames);
            Assert.Equal(700_000, data.GetProperty("freeShippingThreshold").GetInt32());
            Assert.Equal(15_000_000, data.GetProperty("codMaxAmount").GetInt32());
            Assert.Equal(3, data.GetProperty("maxQuantityPerSku").GetInt32());
            Assert.Equal(10, data.GetProperty("maxCartLines").GetInt32());
            Assert.Equal("0123456789", data.GetProperty("sepayAccountNumber").GetString());
            Assert.Equal("VCB", data.GetProperty("sepayBankCode").GetString());
            Assert.Equal(JsonValueKind.String, data.GetProperty("updatedAt").ValueKind);
        }

        var stored = Assert.Single(await ReadStoredRowsAsync());
        Assert.Equal(
            (700_000, 15_000_000, 3, 10, "0123456789", "VCB"),
            (stored.FreeShippingThreshold, stored.CodMaxAmount, stored.MaxQuantityPerSku, stored.MaxCartLines,
                stored.SepayAccountNumber, stored.SepayBankCode));
        Assert.True(stored.UpdatedAt >= beforeRequest, "`UpdatedAt` phải là thời điểm lưu.");
    }

    [Fact]
    // GET sau PUT trả đúng cấu hình đã lưu.
    public async Task Get_AfterPut_ReturnsStoredSettings()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        await SendAsync(HttpMethod.Put, adminSession, ValidBody());

        var response = await SendAsync(HttpMethod.Get, adminSession);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(700_000, data.GetProperty("freeShippingThreshold").GetInt32());
        Assert.Equal("MB", data.GetProperty("sepayBankCode").GetString());
        Assert.Equal(JsonValueKind.String, data.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    // PUT thay toàn bộ cấu hình. PUT lần hai sửa đúng dòng duy nhất và có thể xóa hai trường SePay.
    public async Task Put_Twice_KeepsSingleRowAndReplacesEverything()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        await SendAsync(HttpMethod.Put, adminSession, ValidBody());
        var second = ValidBody();
        second["maxCartLines"] = 50;
        second["sepayAccountNumber"] = null;
        second["sepayBankCode"] = "";

        var response = await SendAsync(HttpMethod.Put, adminSession, second);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = Assert.Single(await ReadStoredRowsAsync());
        Assert.Equal(50, stored.MaxCartLines);
        Assert.Null(stored.SepayAccountNumber);
        Assert.Null(stored.SepayBankCode);
    }

    // Mỗi dòng: tên ca, trường bị sửa, giá trị sai. Trường lỗi phải là khóa trong `details`.
    public static TheoryData<string, string, object?> InvalidFieldCases => new()
    {
        { "freeShippingThreshold âm", "freeShippingThreshold", -1 },
        { "freeShippingThreshold quá trần", "freeShippingThreshold", 1_000_000_001 },
        { "freeShippingThreshold là chuỗi", "freeShippingThreshold", "500000" },
        { "codMaxAmount âm", "codMaxAmount", -1 },
        { "codMaxAmount quá trần", "codMaxAmount", 1_000_000_001 },
        { "maxQuantityPerSku bằng 0", "maxQuantityPerSku", 0 },
        { "maxQuantityPerSku bằng 100", "maxQuantityPerSku", 100 },
        { "maxCartLines bằng 0", "maxCartLines", 0 },
        { "maxCartLines bằng 101", "maxCartLines", 101 },
        { "maxCartLines là số lẻ", "maxCartLines", 2.5 },
        { "maxCartLines thiếu", "maxCartLines", null },
        { "sepayAccountNumber quá ngắn", "sepayAccountNumber", "12345" },
        { "sepayAccountNumber có chữ", "sepayAccountNumber", "12345a" },
        { "sepayAccountNumber quá dài", "sepayAccountNumber", "123456789012345678901" },
        { "sepayBankCode có ký tự lạ", "sepayBankCode", "MB-1" },
        { "sepayBankCode quá ngắn", "sepayBankCode", "M" },
    };

    [Theory]
    [MemberData(nameof(InvalidFieldCases))]
    // Mỗi luật kiểm trả 400 `VALIDATION_ERROR` với `details` chỉ đúng ô sai. Cấu hình và audit không đổi.
    public async Task Put_InvalidField_Returns400AndChangesNothing(string caseName, string field, object? value)
    {
        _ = caseName;
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        if (value is null)
        {
            body.Remove(field);
        }
        else
        {
            body[field] = value;
        }

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var document = await ActorHttp.ReadJsonAsync(response))
        {
            var error = document.RootElement.GetProperty("error");
            Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
            var failedKeys = error.GetProperty("details").EnumerateArray()
                .Select(detail => detail.GetProperty("key").GetString())
                .ToList();
            Assert.Equal([field], failedKeys);
        }

        Assert.Empty(await ReadStoredRowsAsync());
        Assert.Equal(0, await CountAuditRowsAsync());
    }

    [Theory]
    [InlineData("sepayAccountNumber", "sepayBankCode")]
    [InlineData("sepayBankCode", "sepayAccountNumber")]
    // Chỉ nhập một trường SePay thì lỗi nằm ở ô còn thiếu.
    public async Task Put_OnlyOneSepayField_ReportsTheMissingField(string keptField, string missingField)
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body[missingField] = null;
        Assert.NotNull(body[keptField]);

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var details = document.RootElement.GetProperty("error").GetProperty("details");
        Assert.Equal(1, details.GetArrayLength());
        Assert.Equal(missingField, details[0].GetProperty("key").GetString());
        Assert.Empty(await ReadStoredRowsAsync());
    }

    [Fact]
    // Trường lạ bị từ chối, kể cả khóa webhook mà client tự gửi.
    public async Task Put_UnknownField_Returns400()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body["sepayWebhookApiKey"] = "khoa-do-client-gui";

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var details = document.RootElement.GetProperty("error").GetProperty("details");
        Assert.Equal("sepayWebhookApiKey", details[0].GetProperty("key").GetString());
        Assert.Empty(await ReadStoredRowsAsync());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"chuỗi\"")]
    [InlineData("null")]
    // Body không phải đối tượng JSON bị từ chối, và cấu hình không đổi.
    public async Task Put_BodyIsNotAnObject_Returns400(string rawJson)
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        // Helper luôn tuần tự hóa `object`, nên thân được thay bằng JSON thô sau khi dựng request.
        using var request = ActorHttp.CreateJsonRequest(HttpMethod.Put, SettingsPath, body: null, $"pk_session={adminSession}");
        request.Content = new StringContent(rawJson, Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ReadStoredRowsAsync());
    }

    [Fact]
    // Biên hợp lệ ở hai đầu đều được nhận và lưu nguyên.
    public async Task Put_BoundaryValues_AreAccepted()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = new Dictionary<string, object?>
        {
            ["freeShippingThreshold"] = 0,
            ["codMaxAmount"] = 1_000_000_000,
            ["maxQuantityPerSku"] = 99,
            ["maxCartLines"] = 1,
            ["sepayAccountNumber"] = null,
            ["sepayBankCode"] = null,
        };

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = Assert.Single(await ReadStoredRowsAsync());
        Assert.Equal((0, 1_000_000_000, 99, 1), (stored.FreeShippingThreshold, stored.CodMaxAmount, stored.MaxQuantityPerSku, stored.MaxCartLines));
    }

    [Fact]
    // Lần PUT đầu có `before` là `null`. Audit ghi đúng người sửa, hành động và đích.
    public async Task Put_FirstTime_WritesOneAuditRowWithNullBefore()
    {
        var (admin, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin", "com06");

        var response = await SendAsync(HttpMethod.Put, adminSession, ValidBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var identity = _database.CreateIdentityContext();
        var audit = await identity.AuditLogs.AsNoTracking().SingleAsync(log => log.TargetType == AuditTargetType);
        Assert.Equal((admin.Id, AuditAction, "singleton"), (audit.ActorId, audit.Action, audit.TargetId));
        Assert.True(audit.Before is null || audit.Before.RootElement.ValueKind == JsonValueKind.Null);
        Assert.Equal(3, audit.After!.RootElement.GetProperty("maxQuantityPerSku").GetInt32());
        Assert.Equal("MB", audit.After.RootElement.GetProperty("sepayBankCode").GetString());
    }

    [Fact]
    // Mỗi PUT hợp lệ ghi đúng 1 dòng, kể cả khi giá trị không đổi. Dòng sau có `before` là cấu hình cũ.
    public async Task Put_SameValuesTwice_WritesOneAuditRowPerRequest()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        await SendAsync(HttpMethod.Put, adminSession, ValidBody());
        var changed = ValidBody();
        changed["maxQuantityPerSku"] = 7;

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
        Assert.All(audits, audit => Assert.Equal(AuditAction, audit.Action));
        Assert.Equal(3, audits[1].Before!.RootElement.GetProperty("maxQuantityPerSku").GetInt32());
        Assert.Equal(7, audits[1].After!.RootElement.GetProperty("maxQuantityPerSku").GetInt32());
        Assert.Equal(7, audits[2].Before!.RootElement.GetProperty("maxQuantityPerSku").GetInt32());
        Assert.Equal(7, audits[2].After!.RootElement.GetProperty("maxQuantityPerSku").GetInt32());
    }

    [Fact]
    // Audit lỗi thì cấu hình đã có không đổi. Thay đổi và audit cùng thành công hoặc cùng không có.
    public async Task Put_WhenAuditWriteFails_KeepsPreviousSettings()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var firstResponse = await SendAsync(HttpMethod.Put, adminSession, ValidBody());
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var failingClient = CreateFailingAuditClient();
        var changed = ValidBody();
        changed["maxCartLines"] = 99;

        var response = await failingClient.SendAsync(
            ActorHttp.CreateJsonRequest(HttpMethod.Put, SettingsPath, changed, $"pk_session={adminSession}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var stored = Assert.Single(await ReadStoredRowsAsync());
        Assert.Equal(10, stored.MaxCartLines);
        Assert.Equal(1, await CountAuditRowsAsync());
    }

    [Fact]
    // Audit lỗi ở lần cấu hình đầu thì không tạo dòng cấu hình nào.
    public async Task Put_FirstTimeWhenAuditWriteFails_CreatesNoRow()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        using var failingClient = CreateFailingAuditClient();

        var response = await failingClient.SendAsync(
            ActorHttp.CreateJsonRequest(HttpMethod.Put, SettingsPath, ValidBody(), $"pk_session={adminSession}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(await ReadStoredRowsAsync());
    }

    [Fact]
    // Nhiều PUT đồng thời khi chưa có dòng nào đều trả 200, và DB có đúng 1 dòng cấu hình.
    // Khóa dòng trong DB bảo đảm kết quả này, không phụ thuộc thứ tự chạy.
    public async Task Put_ConcurrentFirstWrites_CreateSingleRowWithoutServerError()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        const int requestCount = 8;

        var responses = await Task.WhenAll(Enumerable.Range(1, requestCount).Select(index =>
        {
            var body = ValidBody();
            body["maxCartLines"] = index;
            return SendAsync(HttpMethod.Put, adminSession, body);
        }));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Single(await ReadStoredRowsAsync());
        Assert.Equal(requestCount, await CountAuditRowsAsync());
    }

    private HttpClient CreateFailingAuditClient()
    {
        var failingFactory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuditLogWriter>();
            services.AddSingleton<IAuditLogWriter, ThrowingAuditLogWriter>();
        }));
        return failingFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    // Bản ghi audit luôn lỗi. Test dùng nó để kiểm tra transaction hủy cả thay đổi cấu hình.
    private sealed class ThrowingAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogEntry entry, DbTransaction transaction, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Audit lỗi do test cố ý gây ra.");
    }
}
