using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Database;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// Kiểm tra tích hợp khối `shop` trên route cấu hình bán hàng và nhật ký thao tác.
[Collection(CommerceCollection.Name)]
public sealed class CommerceSettingsShopTests : IClassFixture<CommerceWebApplicationFactory>, IAsyncLifetime
{
    private const string SettingsPath = "/api/admin/commerce/settings";
    private const string AuditTargetType = "commerce_settings";
    private const string AuditAction = "commerce.settings.update";

    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public CommerceSettingsShopTests(CommerceWebApplicationFactory factory, CommerceDatabaseFixture database)
    {
        _database = database;
        // Client không tự giữ cookie để mỗi request dùng đúng phiên được gán.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync() => await ResetAsync();

    public async Task DisposeAsync() => await ResetAsync();

    private async Task ResetAsync()
    {
        await using (var commerce = _database.CreateCommerceContext())
        {
            // Xóa dữ liệu kiểm thử sau khi xác nhận kết nối trỏ đúng DB test.
            DotnetSchemaIntegrationTests.AssertTestConnection(commerce);
            await commerce.Settings.ExecuteDeleteAsync();
        }

        await using var identity = _database.CreateIdentityContext();
        await identity.AuditLogs.Where(log => log.TargetType == AuditTargetType).ExecuteDeleteAsync();
    }

    private async Task<string> CreateAdminSessionAsync() =>
        (await IdentityTestData.CreateStaffSessionAsync(_database, "admin", "exp07")).SessionToken;

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string session, object? body = null) =>
        _client.SendAsync(ActorHttp.CreateJsonRequest(method, SettingsPath, body, $"pk_session={session}"));

    private static Dictionary<string, object?> BodyWithShop(Dictionary<string, object?>? shop) => new()
    {
        ["freeShippingThreshold"] = 500_000,
        ["codMaxAmount"] = 20_000_000,
        ["maxQuantityPerSku"] = 5,
        ["maxCartLines"] = 20,
        ["sepayAccountNumber"] = null,
        ["sepayBankCode"] = null,
        ["shop"] = shop,
    };

    private static Dictionary<string, object?> BodyWithoutShop(int maxCartLines = 25) => new()
    {
        ["freeShippingThreshold"] = 600_000,
        ["codMaxAmount"] = 18_000_000,
        ["maxQuantityPerSku"] = 4,
        ["maxCartLines"] = maxCartLines,
        ["sepayAccountNumber"] = null,
        ["sepayBankCode"] = null,
    };

    private async Task<CommerceSettings?> ReadSingleRowAsync()
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.Settings.AsNoTracking().SingleOrDefaultAsync();
    }

    private async Task<int> CountAuditRowsAsync()
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.AuditLogs.CountAsync(log => log.TargetType == AuditTargetType);
    }

    private static void AssertShopJson(
        JsonElement shop, string? expectedName, string? expectedAddress, string? expectedPhone, string? expectedTaxCode)
    {
        var keys = shop.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal);
        Assert.Equal(["address", "name", "phone", "taxCode"], keys);
        AssertJsonNullableString(shop.GetProperty("name"), expectedName);
        AssertJsonNullableString(shop.GetProperty("address"), expectedAddress);
        AssertJsonNullableString(shop.GetProperty("phone"), expectedPhone);
        AssertJsonNullableString(shop.GetProperty("taxCode"), expectedTaxCode);
    }

    private static void AssertJsonNullableString(JsonElement value, string? expected)
    {
        if (expected is null)
        {
            Assert.Equal(JsonValueKind.Null, value.ValueKind);
            return;
        }

        Assert.Equal(expected, value.GetString());
    }

    [Fact]
    // Khi DB chưa có dòng cấu hình, GET vẫn trả khối `shop` với bốn trường `null`.
    public async Task Get_WhenNotConfigured_ReturnsShopWithFourNullFields()
    {
        var adminSession = await CreateAdminSessionAsync();

        var response = await SendAsync(HttpMethod.Get, adminSession);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var shop = document.RootElement.GetProperty("data").GetProperty("shop");
        AssertShopJson(shop, null, null, null, null);
    }

    [Fact]
    // PUT gửi đủ khối `shop` phải cắt khoảng trắng, biến chuỗi trắng thành `null` và GET đọc lại khớp.
    public async Task Put_WithShopBlock_TrimsStoresAndReturnsOnGet()
    {
        var adminSession = await CreateAdminSessionAsync();
        var body = BodyWithShop(new Dictionary<string, object?>
        {
            ["name"] = "  Pickory Shop  ",
            ["address"] = "   ",
            ["phone"] = "  +84 28.1234-5678  ",
            ["taxCode"] = "  0312345678-001  ",
        });

        var putResponse = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        using (var putDocument = await ActorHttp.ReadJsonAsync(putResponse))
        {
            var putShop = putDocument.RootElement.GetProperty("data").GetProperty("shop");
            AssertShopJson(putShop, "Pickory Shop", null, "+84 28.1234-5678", "0312345678-001");
        }

        var getResponse = await SendAsync(HttpMethod.Get, adminSession);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using (var getDocument = await ActorHttp.ReadJsonAsync(getResponse))
        {
            var getShop = getDocument.RootElement.GetProperty("data").GetProperty("shop");
            AssertShopJson(getShop, "Pickory Shop", null, "+84 28.1234-5678", "0312345678-001");
        }

        var row = await ReadSingleRowAsync();
        Assert.NotNull(row);
        Assert.Equal(("Pickory Shop", (string?)null, "+84 28.1234-5678", "0312345678-001"),
            (row.ShopName, row.ShopAddress, row.ShopPhone, row.ShopTaxCode));
    }

    [Fact]
    // Khi DB chưa có dòng cấu hình, PUT không gửi khối `shop` vẫn trả khối `shop` gồm bốn trường `null`.
    public async Task Put_FirstTimeWithoutShopBlock_ReturnsShopWithFourNullFields()
    {
        var adminSession = await CreateAdminSessionAsync();

        var response = await SendAsync(HttpMethod.Put, adminSession, BodyWithoutShop(maxCartLines: 18));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        AssertShopJson(document.RootElement.GetProperty("data").GetProperty("shop"), null, null, null, null);
        var row = await ReadSingleRowAsync();
        Assert.NotNull(row);
        Assert.Null(row.ShopName);
        Assert.Null(row.ShopAddress);
        Assert.Null(row.ShopPhone);
        Assert.Null(row.ShopTaxCode);
    }

    [Fact]
    // PUT thiếu trường bên trong khối `shop` coi trường thiếu đó là `null`.
    public async Task Put_WithPartialShopFields_SetsMissingShopFieldsToNull()
    {
        var adminSession = await CreateAdminSessionAsync();
        var setupResponse = await SendAsync(HttpMethod.Put, adminSession, BodyWithShop(new Dictionary<string, object?>
        {
            ["name"] = "Shop Cũ",
            ["address"] = "12 Nguyễn Văn Bảo",
            ["phone"] = "0901234567",
            ["taxCode"] = "0312345678",
        }));
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);

        var response = await SendAsync(HttpMethod.Put, adminSession, BodyWithShop(new Dictionary<string, object?>
        {
            ["name"] = "Shop Mới",
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        AssertShopJson(document.RootElement.GetProperty("data").GetProperty("shop"), "Shop Mới", null, null, null);
    }

    [Fact]
    // PUT không gửi khối `shop` phải giữ nguyên thông tin shop đã lưu và cập nhật trường cũ.
    public async Task Put_WithoutShopBlock_PreservesSavedShopInfo()
    {
        var adminSession = await CreateAdminSessionAsync();
        var setupResponse = await SendAsync(HttpMethod.Put, adminSession, BodyWithShop(new Dictionary<string, object?>
        {
            ["name"] = "Pickory Flagship",
            ["address"] = "45 Lê Lợi",
            ["phone"] = "028 3894 0390",
            ["taxCode"] = "0312345678",
        }));
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);

        var response = await SendAsync(HttpMethod.Put, adminSession, BodyWithoutShop(maxCartLines: 30));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(30, data.GetProperty("maxCartLines").GetInt32());
        AssertShopJson(data.GetProperty("shop"), "Pickory Flagship", "45 Lê Lợi", "028 3894 0390", "0312345678");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"Pickory\"")]
    // Khi khóa `shop` có mặt nhưng không phải đối tượng JSON, API trả 400 và không đổi dữ liệu.
    public async Task Put_ShopIsNullOrNotObject_Returns400AndKeepsExistingData(string rawShopValue)
    {
        var adminSession = await CreateAdminSessionAsync();
        var setupResponse = await SendAsync(HttpMethod.Put, adminSession, BodyWithShop(new Dictionary<string, object?>
        {
            ["name"] = "Shop Giữ Nguyên",
            ["address"] = "Địa chỉ cũ",
            ["phone"] = "0901234567",
            ["taxCode"] = "0312345678",
        }));
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);

        var rawBody = $$"""
            {"freeShippingThreshold":500000,"codMaxAmount":20000000,"maxQuantityPerSku":5,"maxCartLines":20,"sepayAccountNumber":null,"sepayBankCode":null,"shop":{{rawShopValue}}}
            """;
        using var request = ActorHttp.CreateJsonRequest(HttpMethod.Put, SettingsPath, body: null, $"pk_session={adminSession}");
        request.Content = new StringContent(rawBody, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        Assert.Equal("shop", error.GetProperty("details")[0].GetProperty("key").GetString());
        Assert.Equal("Shop Giữ Nguyên", (await ReadSingleRowAsync())?.ShopName);
        Assert.Equal(1, await CountAuditRowsAsync());
    }

    [Fact]
    // Khối `shop` chứa trường lạ bị từ chối với mã 400 và không ghi cấu hình hay nhật ký.
    public async Task Put_UnknownFieldInShop_Returns400AndKeepsExistingData()
    {
        var adminSession = await CreateAdminSessionAsync();
        var body = BodyWithShop(new Dictionary<string, object?>
        {
            ["name"] = "Pickory",
            ["email"] = "shop@example.test",
        });

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        Assert.Equal("shop.email", error.GetProperty("details")[0].GetProperty("key").GetString());
        Assert.Null(await ReadSingleRowAsync());
        Assert.Equal(0, await CountAuditRowsAsync());
    }

    public static IEnumerable<object[]> BoundaryAcceptanceCases()
    {
        yield return ["name", new string('a', 120)];
        yield return ["address", new string('b', 255)];
        yield return ["phone", "+84 28.1234-5678"];
        yield return ["taxCode", "0312345678-001"];
    }

    [Theory]
    [MemberData(nameof(BoundaryAcceptanceCases))]
    // Giá trị đúng biên độ dài và định dạng của từng trường `shop` phải lưu thành công.
    public async Task Put_ShopFieldAtValidBoundary_Succeeds(string field, string value)
    {
        var adminSession = await CreateAdminSessionAsync();
        var body = BodyWithShop(new Dictionary<string, object?> { [field] = value });

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal(value, document.RootElement.GetProperty("data").GetProperty("shop").GetProperty(field).GetString());
    }

    public static IEnumerable<object[]> InvalidShopFieldCases()
    {
        yield return ["name", new string('a', 121), "shop.name"];
        yield return ["address", new string('b', 256), "shop.address"];
        yield return ["phone", new string('1', 21), "shop.phone"];
        yield return ["phone", "090123456a", "shop.phone"];
        yield return ["phone", "090#123456", "shop.phone"];
        yield return ["taxCode", "031234567", "shop.taxCode"];
    }

    [Theory]
    [MemberData(nameof(InvalidShopFieldCases))]
    // Giá trị sai quy tắc trả 400 với đúng khóa `details[].key`, đồng thời giữ cấu hình cũ và không ghi thêm nhật ký.
    public async Task Put_InvalidShopField_Returns400AndLeavesStateUnchanged(string field, string invalidValue, string expectedKey)
    {
        var adminSession = await CreateAdminSessionAsync();
        var setupResponse = await SendAsync(HttpMethod.Put, adminSession, BodyWithShop(new Dictionary<string, object?>
        {
            ["name"] = "Shop Hợp Lệ",
            ["address"] = "Địa chỉ cũ",
            ["phone"] = "0901234567",
            ["taxCode"] = "0312345678",
        }));
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);

        var invalidBody = BodyWithShop(new Dictionary<string, object?>
        {
            ["name"] = "Shop Hợp Lệ",
            ["address"] = "Địa chỉ cũ",
            ["phone"] = "0901234567",
            ["taxCode"] = "0312345678",
            [field] = invalidValue,
        });

        var response = await SendAsync(HttpMethod.Put, adminSession, invalidBody);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        var keys = error.GetProperty("details").EnumerateArray()
            .Select(detail => detail.GetProperty("key").GetString())
            .ToList();
        Assert.Equal([expectedKey], keys);

        var row = await ReadSingleRowAsync();
        Assert.NotNull(row);
        Assert.Equal(("Shop Hợp Lệ", "Địa chỉ cũ", "0901234567", "0312345678"),
            (row.ShopName, row.ShopAddress, row.ShopPhone, row.ShopTaxCode));
        Assert.Equal(1, await CountAuditRowsAsync());
    }

    [Fact]
    // Nhiều trường `shop` sai cùng lúc phải trả đủ từng khóa lỗi trong `details`.
    public async Task Put_MultipleInvalidShopFields_ReturnsAllDetailKeys()
    {
        var adminSession = await CreateAdminSessionAsync();
        var body = BodyWithShop(new Dictionary<string, object?>
        {
            ["name"] = new string('a', 121),
            ["address"] = new string('b', 256),
            ["phone"] = "090abc",
            ["taxCode"] = "123",
        });

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var keys = document.RootElement.GetProperty("error").GetProperty("details").EnumerateArray()
            .Select(detail => detail.GetProperty("key").GetString())
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(["shop.address", "shop.name", "shop.phone", "shop.taxCode"], keys);
        Assert.Null(await ReadSingleRowAsync());
        Assert.Equal(0, await CountAuditRowsAsync());
    }

    [Fact]
    // Nhật ký thao tác ghi nguyên khối `shop` cho cả `before` và `after`, với `before` là `null` ở lần lưu đầu.
    public async Task Put_ShopChanges_WritesSingleAuditRowWithBeforeAndAfterShop()
    {
        var (admin, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin", "exp07");

        var firstResponse = await SendAsync(HttpMethod.Put, adminSession, BodyWithShop(new Dictionary<string, object?>
        {
            ["name"] = "Shop Lần Một",
            ["address"] = "12 Nguyễn Văn Bảo",
            ["phone"] = "028 3894 0390",
            ["taxCode"] = "0312345678",
        }));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await SendAsync(HttpMethod.Put, adminSession, BodyWithShop(new Dictionary<string, object?>
        {
            ["name"] = "Shop Lần Hai",
            ["address"] = "45 Lê Lợi",
            ["phone"] = "+84 28.1234-5678",
            ["taxCode"] = "0312345678-001",
        }));
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        await using var identity = _database.CreateIdentityContext();
        var audits = await identity.AuditLogs.AsNoTracking()
            .Where(log => log.TargetType == AuditTargetType)
            .OrderBy(log => log.CreatedAt)
            .ToListAsync();

        Assert.Equal(2, audits.Count);
        Assert.All(audits, audit => Assert.Equal((admin.Id, AuditAction, "singleton"), (audit.ActorId, audit.Action, audit.TargetId)));
        Assert.True(audits[0].Before is null || audits[0].Before!.RootElement.ValueKind == JsonValueKind.Null);
        AssertShopJson(audits[0].After!.RootElement.GetProperty("shop"),
            "Shop Lần Một", "12 Nguyễn Văn Bảo", "028 3894 0390", "0312345678");
        AssertShopJson(audits[1].Before!.RootElement.GetProperty("shop"),
            "Shop Lần Một", "12 Nguyễn Văn Bảo", "028 3894 0390", "0312345678");
        AssertShopJson(audits[1].After!.RootElement.GetProperty("shop"),
            "Shop Lần Hai", "45 Lê Lợi", "+84 28.1234-5678", "0312345678-001");
    }

    [Theory]
    [InlineData("name")]
    [InlineData("address")]
    [InlineData("phone")]
    [InlineData("taxCode")]
    // Ký tự mã 0 trong bốn trường của `shop` phải trả 400 thay vì gây lỗi DB 500.
    public async Task Put_ShopFieldWithNulCharacter_Returns400AndLeavesStateUnchanged(string field)
    {
        var adminSession = await CreateAdminSessionAsync();
        var body = BodyWithShop(new Dictionary<string, object?> { [field] = "a\u0000b" });

        var response = await SendAsync(HttpMethod.Put, adminSession, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        Assert.Equal($"shop.{field}", error.GetProperty("details")[0].GetProperty("key").GetString());
        Assert.Null(await ReadSingleRowAsync());
        Assert.Equal(0, await CountAuditRowsAsync());
    }
}
