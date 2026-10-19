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

// SHIP-13: route cấu hình phí dự phòng của back-office, gồm quyền, đọc, ghi, kiểm body và audit.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class FallbackFeeApiTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string FallbackFeePath = "/api/admin/shipping/fallback-fee";
    private const string AuditTargetType = "shipping_fallback_fee";
    private const string AuditAction = "shipping.fallback_fee.update";
    private const string IncompleteCode = "FALLBACK_FEE_INCOMPLETE";

    private static readonly string[] FeeFields =
    [
        "sameProvinceBaseFee", "sameProvinceStepFee",
        "sameRegionBaseFee", "sameRegionStepFee",
        "crossRegionBaseFee", "crossRegionStepFee",
    ];

    private static readonly string[] WeightFields =
    [
        "sameProvinceBaseWeightGrams", "sameRegionBaseWeightGrams", "crossRegionBaseWeightGrams",
    ];

    private static readonly string[] AllPriceFields = [.. FeeFields, .. WeightFields];

    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public FallbackFeeApiTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
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
        await WithShippingAsync(shipping => shipping.FallbackFees.ExecuteDeleteAsync());

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

    private static Dictionary<string, object?> ValidBody() => new()
    {
        ["enabled"] = true,
        ["sameProvinceBaseFee"] = 22_000,
        ["sameProvinceBaseWeightGrams"] = 1_000,
        ["sameProvinceStepFee"] = 3_000,
        ["sameRegionBaseFee"] = 30_000,
        ["sameRegionBaseWeightGrams"] = 1_500,
        ["sameRegionStepFee"] = 5_000,
        ["crossRegionBaseFee"] = 38_000,
        ["crossRegionBaseWeightGrams"] = 2_000,
        ["crossRegionStepFee"] = 7_000,
    };

    private async Task<ShippingFallbackFee?> ReadStoredAsync()
    {
        ShippingFallbackFee? stored = null;
        await WithShippingAsync(async shipping => stored = await shipping.FallbackFees.AsNoTracking().SingleOrDefaultAsync());
        return stored;
    }

    private async Task<int> CountStoredRowsAsync()
    {
        var rowCount = 0;
        await WithShippingAsync(async shipping => rowCount = await shipping.FallbackFees.CountAsync());
        return rowCount;
    }

    private async Task<int> CountAuditRowsAsync()
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.AuditLogs.CountAsync(log => log.TargetType == AuditTargetType);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string? session, object? body = null) =>
        _client.SendAsync(ActorHttp.CreateJsonRequest(method, FallbackFeePath, body, session is null ? null : $"pk_session={session}"));

    private async Task<string> CreateStaffSessionAsync(string roleCode) =>
        (await IdentityTestData.CreateStaffSessionAsync(_database, roleCode, "ship13")).SessionToken;

    private static async Task<List<(string? Key, string? Message)>> ReadDetailsAsync(HttpResponseMessage response)
    {
        using var document = await ActorHttp.ReadJsonAsync(response);
        return document.RootElement.GetProperty("error").GetProperty("details").EnumerateArray()
            .Select(detail => (detail.GetProperty("key").GetString(), detail.GetProperty("message").GetString()))
            .OrderBy(detail => detail.Item1, StringComparer.Ordinal)
            .ToList();
    }

    private async Task AssertErrorOnFieldAsync(HttpResponseMessage response, HttpStatusCode status, string field)
    {
        Assert.Equal(status, response.StatusCode);
        var details = await ReadDetailsAsync(response);
        Assert.Equal([field], details.Select(detail => detail.Key));
        Assert.Equal(0, await CountStoredRowsAsync());
        Assert.Equal(0, await CountAuditRowsAsync());
    }

    // ----- Đọc cấu hình -----

    [Fact]
    // Chưa có dòng cấu hình thì `GET` trả `enabled: false` và 9 trường `null`.
    public async Task Get_WhenNotConfigured_ReturnsDisabledWithNullPrices()
    {
        var adminSession = await CreateStaffSessionAsync("admin");

        var response = await SendAsync(HttpMethod.Get, adminSession);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("enabled").GetBoolean());
        foreach (var field in AllPriceFields)
        {
            Assert.Equal(JsonValueKind.Null, data.GetProperty(field).ValueKind);
        }

        Assert.Equal(JsonValueKind.Null, data.GetProperty("updatedAt").ValueKind);
        // `GET` không tạo dòng nào.
        Assert.Equal(0, await CountStoredRowsAsync());
    }

    [Fact]
    public async Task Get_AfterPut_ReturnsStoredValues()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var putResponse = await SendAsync(HttpMethod.Put, adminSession, ValidBody());
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var response = await SendAsync(HttpMethod.Get, adminSession);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("enabled").GetBoolean());
        foreach (var (field, expected) in ValidBody().Where(pair => pair.Key != "enabled"))
        {
            Assert.Equal((int)expected!, data.GetProperty(field).GetInt32());
        }

        Assert.Equal(JsonValueKind.String, data.GetProperty("updatedAt").ValueKind);
    }

    // ----- Quyền -----

    [Theory]
    [InlineData("support")]
    [InlineData("sales")]
    public async Task Get_WithoutPermission_Returns403(string roleCode)
    {
        var session = await CreateStaffSessionAsync(roleCode);

        var response = await SendAsync(HttpMethod.Get, session);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("support")]
    [InlineData("sales")]
    // `support` và `sales` không sửa được. Dòng cấu hình không được tạo ra.
    public async Task Put_WithoutPermission_Returns403AndSavesNothing(string roleCode)
    {
        var session = await CreateStaffSessionAsync(roleCode);

        var response = await SendAsync(HttpMethod.Put, session, ValidBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await CountStoredRowsAsync());
        Assert.Equal(0, await CountAuditRowsAsync());
    }

    [Fact]
    public async Task Guest_Returns401OnBothRoutes()
    {
        var getResponse = await SendAsync(HttpMethod.Get, session: null);
        var putResponse = await SendAsync(HttpMethod.Put, session: null, ValidBody());

        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, putResponse.StatusCode);
    }

    // ----- Lưu cấu hình -----

    [Fact]
    public async Task Put_Valid_StoresEveryValueAndReturnsThem()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var beforeRequest = DateTime.UtcNow.AddSeconds(-1);

        var response = await SendAsync(HttpMethod.Put, adminSession, ValidBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var document = await ActorHttp.ReadJsonAsync(response))
        {
            var data = document.RootElement.GetProperty("data");
            Assert.True(data.GetProperty("enabled").GetBoolean());
            Assert.Equal(22_000, data.GetProperty("sameProvinceBaseFee").GetInt32());
            Assert.Equal(7_000, data.GetProperty("crossRegionStepFee").GetInt32());
        }

        var stored = await ReadStoredAsync();
        Assert.NotNull(stored);
        Assert.Equal("singleton", stored.Id);
        Assert.True(stored.Enabled);
        Assert.Equal(
            (22_000, 1_000, 3_000, 30_000, 1_500, 5_000, 38_000, 2_000, 7_000),
            (stored.SameProvinceBaseFee, stored.SameProvinceBaseWeightGrams, stored.SameProvinceStepFee,
                stored.SameRegionBaseFee, stored.SameRegionBaseWeightGrams, stored.SameRegionStepFee,
                stored.CrossRegionBaseFee, stored.CrossRegionBaseWeightGrams, stored.CrossRegionStepFee));
        Assert.True(stored.UpdatedAt >= beforeRequest, "`UpdatedAt` phải là thời điểm lưu.");
    }

    [Fact]
    // PUT lần hai sửa đúng dòng duy nhất, không thêm dòng mới.
    public async Task Put_Twice_KeepsSingleRowWithLatestValues()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, adminSession, ValidBody())).StatusCode);
        var second = ValidBody();
        second["sameProvinceBaseFee"] = 25_000;

        var response = await SendAsync(HttpMethod.Put, adminSession, second);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await CountStoredRowsAsync());
        Assert.Equal(25_000, (await ReadStoredAsync())!.SameProvinceBaseFee);
    }

    [Fact]
    // Tắt thì được lưu bảng trống, vì admin chưa nhập giá nào vẫn cần giữ công tắc tắt.
    public async Task Put_DisabledWithAllNullPrices_IsAccepted()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = new Dictionary<string, object?> { ["enabled"] = false };
        foreach (var field in AllPriceFields)
        {
            body[field] = null;
        }

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await ReadStoredAsync();
        Assert.False(stored!.Enabled);
        Assert.Null(stored.SameProvinceBaseFee);
        Assert.Null(stored.CrossRegionBaseWeightGrams);
    }

    [Fact]
    // Tắt và bỏ hẳn các trường giá cũng hợp lệ. Thiếu trường được hiểu là chưa nhập.
    public async Task Put_DisabledWithOnlyEnabledField_IsAccepted()
    {
        var adminSession = await CreateStaffSessionAsync("admin");

        var response = await SendAsync(HttpMethod.Put, adminSession, new Dictionary<string, object?> { ["enabled"] = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await ReadStoredAsync())!.Enabled);
    }

    [Fact]
    // Tắt vẫn giữ giá đã nhập, để admin bật lại không phải nhập lại.
    public async Task Put_DisabledWithPrices_KeepsPrices()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body["enabled"] = false;

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await ReadStoredAsync();
        Assert.False(stored!.Enabled);
        Assert.Equal(22_000, stored.SameProvinceBaseFee);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5_000_000)]
    // Giá đúng hai đầu của khoảng cho phép phải được nhận.
    public async Task Put_FeeAtLimits_IsAccepted(int fee)
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        foreach (var field in FeeFields)
        {
            body[field] = fee;
        }

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(fee, (await ReadStoredAsync())!.CrossRegionStepFee);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20_000)]
    // Khối lượng gốc đúng hai đầu của khoảng cho phép phải được nhận.
    public async Task Put_WeightAtLimits_IsAccepted(int weight)
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        foreach (var field in WeightFields)
        {
            body[field] = weight;
        }

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(weight, (await ReadStoredAsync())!.SameRegionBaseWeightGrams);
    }

    // ----- Luật kiểm body -----

    public static TheoryData<string, object?> InvalidFeeCases()
    {
        var data = new TheoryData<string, object?>();
        foreach (var field in FeeFields)
        {
            foreach (var value in new object?[] { -1, 5_000_001, "", "1000", 1500.5, true })
            {
                data.Add(field, value);
            }
        }

        return data;
    }

    public static TheoryData<string, object?> InvalidWeightCases()
    {
        var data = new TheoryData<string, object?>();
        foreach (var field in WeightFields)
        {
            foreach (var value in new object?[] { 0, -5, 20_001, "", "500", 250.5, false })
            {
                data.Add(field, value);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidFeeCases))]
    [MemberData(nameof(InvalidWeightCases))]
    // Mỗi luật kiểm kiểu và khoảng trả 400 `VALIDATION_ERROR` với đúng khóa trường sai. Cấu hình và audit không đổi.
    public async Task Put_InvalidPriceValue_Returns400OnThatField(string field, object? value)
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body[field] = value;

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        await AssertErrorOnFieldAsync(response, HttpStatusCode.BadRequest, field);
    }

    [Theory]
    [InlineData("sameProvinceBaseFee", "{}")]
    [InlineData("sameRegionStepFee", "[1]")]
    [InlineData("crossRegionBaseWeightGrams", "{\"value\":1}")]
    // Đối tượng hoặc mảng không phải số. Test gửi JSON thô vì `TheoryData` không tuần tự hóa được đối tượng ẩn danh.
    public async Task Put_ObjectOrArrayPrice_Returns400OnThatField(string field, string rawValue)
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body.Remove(field);
        var json = "{" + string.Join(",", body.Select(pair => $"\"{pair.Key}\":{JsonSerializer.Serialize(pair.Value)}")) + $",\"{field}\":{rawValue}}}";
        using var request = ActorHttp.CreateJsonRequest(HttpMethod.Put, FallbackFeePath, body: null, $"pk_session={adminSession}");
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        await AssertErrorOnFieldAsync(response, HttpStatusCode.BadRequest, field);
    }

    [Theory]
    [InlineData("sameProvinceBaseFee", -1)]
    [InlineData("crossRegionBaseWeightGrams", 0)]
    [InlineData("sameRegionStepFee", 5_000_001)]
    // Tắt thì giá được phép `null`, nhưng giá trị khác `null` vẫn phải đúng giới hạn.
    public async Task Put_DisabledWithOutOfRangeValue_Returns400(string field, int value)
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body["enabled"] = false;
        body[field] = value;

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        await AssertErrorOnFieldAsync(response, HttpStatusCode.BadRequest, field);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    [InlineData(1)]
    [InlineData("")]
    public async Task Put_EnabledNotBoolean_Returns400OnEnabled(object? value)
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body["enabled"] = value;

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        await AssertErrorOnFieldAsync(response, HttpStatusCode.BadRequest, "enabled");
    }

    [Fact]
    public async Task Put_EnabledMissing_Returns400OnEnabled()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body.Remove("enabled");

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        await AssertErrorOnFieldAsync(response, HttpStatusCode.BadRequest, "enabled");
    }

    [Fact]
    // Trường lạ bị từ chối, kể cả tên gần giống tên thật. Binder mặc định sẽ bỏ qua âm thầm.
    public async Task Put_UnknownField_Returns400OnThatField()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body["sameProvinceFee"] = 1_000;

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        await AssertErrorOnFieldAsync(response, HttpStatusCode.BadRequest, "sameProvinceFee");
    }

    [Fact]
    // Nhiều trường sai thì một phản hồi liệt kê đủ từng khóa, để UI hiện lỗi dưới từng ô.
    public async Task Put_MultipleInvalidFields_ReportsEveryFieldInOneResponse()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body["sameProvinceBaseFee"] = -1;
        body["crossRegionBaseWeightGrams"] = 0;
        body["sameRegionStepFee"] = "abc";

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var keys = (await ReadDetailsAsync(response)).Select(detail => detail.Key).ToList();
        Assert.Equal(["crossRegionBaseWeightGrams", "sameProvinceBaseFee", "sameRegionStepFee"], keys);
        Assert.Equal(0, await CountStoredRowsAsync());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"chuỗi\"")]
    [InlineData("null")]
    // Body không phải đối tượng JSON bị từ chối, và cấu hình không đổi.
    public async Task Put_BodyIsNotAnObject_Returns400(string rawJson)
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        using var request = ActorHttp.CreateJsonRequest(HttpMethod.Put, FallbackFeePath, body: null, $"pk_session={adminSession}");
        request.Content = new StringContent(rawJson, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        await AssertErrorOnFieldAsync(response, HttpStatusCode.BadRequest, "body");
    }

    [Theory]
    [MemberData(nameof(PriceFieldsAsOmittedOrNull))]
    // Bật mà thiếu hoặc `null` một trường của một tuyến thì 422 với lỗi ở đúng trường thiếu.
    public async Task Put_EnabledWithMissingField_Returns422OnThatField(string field, bool omit)
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        if (omit)
        {
            body.Remove(field);
        }
        else
        {
            body[field] = null;
        }

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using (var document = await ActorHttp.ReadJsonAsync(response))
        {
            Assert.Equal(IncompleteCode, document.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        var details = await ReadDetailsAsync(response);
        Assert.Equal([field], details.Select(detail => detail.Key));
        Assert.Equal(0, await CountStoredRowsAsync());
        Assert.Equal(0, await CountAuditRowsAsync());
    }

    public static TheoryData<string, bool> PriceFieldsAsOmittedOrNull()
    {
        var data = new TheoryData<string, bool>();
        foreach (var field in AllPriceFields)
        {
            data.Add(field, true);
            data.Add(field, false);
        }

        return data;
    }

    [Fact]
    // Bật mà không gửi giá nào thì lỗi liệt kê cả 9 trường.
    public async Task Put_EnabledWithNoPrices_Returns422ListingAllNineFields()
    {
        var adminSession = await CreateStaffSessionAsync("admin");

        var response = await SendAsync(HttpMethod.Put, adminSession, new Dictionary<string, object?> { ["enabled"] = true });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var keys = (await ReadDetailsAsync(response)).Select(detail => detail.Key).ToList();
        Assert.Equal(AllPriceFields.Order(StringComparer.Ordinal), keys);
        Assert.Equal(0, await CountStoredRowsAsync());
    }

    [Fact]
    // Lỗi sai kiểu hoặc sai khoảng được báo trước lỗi thiếu trường, để admin sửa từng lớp lỗi một.
    public async Task Put_InvalidValueAndMissingField_Returns400ForInvalidValue()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        var body = ValidBody();
        body["sameProvinceBaseFee"] = -1;
        body.Remove("crossRegionStepFee");

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        await AssertErrorOnFieldAsync(response, HttpStatusCode.BadRequest, "sameProvinceBaseFee");
    }

    // ----- Audit -----

    [Fact]
    // Lần lưu đầu có `before` là cấu hình mặc định (tắt, 9 trường `null`). Audit ghi đúng người sửa, hành động và đích.
    public async Task Put_FirstTime_WritesOneAuditRowWithDefaultBeforeAndNewAfter()
    {
        var (admin, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin", "ship13");

        var response = await SendAsync(HttpMethod.Put, adminSession, ValidBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var identity = _database.CreateIdentityContext();
        var audit = await identity.AuditLogs.AsNoTracking().SingleAsync(log => log.TargetType == AuditTargetType);
        Assert.Equal((admin.Id, AuditAction, "singleton"), (audit.ActorId, audit.Action, audit.TargetId));
        Assert.False(audit.Before!.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, audit.Before.RootElement.GetProperty("sameProvinceBaseFee").ValueKind);
        Assert.True(audit.After!.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(22_000, audit.After.RootElement.GetProperty("sameProvinceBaseFee").GetInt32());
    }

    [Fact]
    // Mỗi PUT hợp lệ ghi đúng một dòng, kể cả khi giá trị không đổi. Dòng thứ hai có `before` là cấu hình cũ.
    public async Task Put_SameValuesTwice_WritesOneAuditRowPerRequest()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        await SendAsync(HttpMethod.Put, adminSession, ValidBody());
        var changed = ValidBody();
        changed["sameProvinceBaseFee"] = 25_000;

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
        Assert.Equal(22_000, audits[1].Before!.RootElement.GetProperty("sameProvinceBaseFee").GetInt32());
        Assert.Equal(25_000, audits[1].After!.RootElement.GetProperty("sameProvinceBaseFee").GetInt32());
        Assert.Equal(25_000, audits[2].Before!.RootElement.GetProperty("sameProvinceBaseFee").GetInt32());
        Assert.Equal(25_000, audits[2].After!.RootElement.GetProperty("sameProvinceBaseFee").GetInt32());
    }

    [Fact]
    // Audit lỗi thì cấu hình đã có không đổi. Hai bản ghi cùng thành công hoặc cùng không có.
    public async Task Put_WhenAuditWriteFails_KeepsPreviousValues()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, adminSession, ValidBody())).StatusCode);
        using var failingFactory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuditLogWriter>();
            services.AddSingleton<IAuditLogWriter, ThrowingAuditLogWriter>();
        }));
        using var failingClient = failingFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var changed = ValidBody();
        changed["sameProvinceBaseFee"] = 99_000;

        var response = await failingClient.SendAsync(
            ActorHttp.CreateJsonRequest(HttpMethod.Put, FallbackFeePath, changed, $"pk_session={adminSession}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(22_000, (await ReadStoredAsync())!.SameProvinceBaseFee);
        Assert.Equal(1, await CountAuditRowsAsync());
    }

    [Fact]
    // Audit lỗi ở lần lưu đầu thì không tạo dòng cấu hình nào.
    public async Task Put_FirstTimeWhenAuditWriteFails_CreatesNoRow()
    {
        var adminSession = await CreateStaffSessionAsync("admin");
        using var failingFactory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuditLogWriter>();
            services.AddSingleton<IAuditLogWriter, ThrowingAuditLogWriter>();
        }));
        using var failingClient = failingFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await failingClient.SendAsync(
            ActorHttp.CreateJsonRequest(HttpMethod.Put, FallbackFeePath, ValidBody(), $"pk_session={adminSession}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, await CountStoredRowsAsync());
    }

    // Bản ghi audit luôn lỗi. Test dùng nó để kiểm tra transaction hủy cả thay đổi cấu hình.
    private sealed class ThrowingAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogEntry entry, DbTransaction transaction, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Audit lỗi do test cố ý gây ra.");
    }
}
