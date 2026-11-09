using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence.Configurations;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-10: `GET` và `PUT /api/admin/support/settings`. Cả hai route cần `support-settings:manage`.
[Collection(SupportDeskCollection.Name)]
public sealed class AdminSupportSettingsEndpointTests(SupportDeskWebApplicationFactory factory, SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private const string SettingsPath = "/api/admin/support/settings";
    private const string AuditAction = "support.settings.update";
    private const string AuditTargetType = "support_settings";

    // Mốc 10:00 ngày 2026-11-03 theo giờ Việt Nam. `updatedAt` khóa theo đồng hồ giả.
    private static readonly DateTimeOffset TestMoment = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(TestMoment);
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock);
        _client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        // Bảng chỉ có một dòng và test khác đọc giá trị mặc định, nên mỗi test trả dòng về mặc định của migration.
        await using var context = database.CreateSupportDeskContext();
        await context.Settings
            .Where(row => row.Id == SupportSettingsConfiguration.SingletonId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.OpensAt, new TimeOnly(8, 0))
                .SetProperty(row => row.ClosesAt, new TimeOnly(21, 0))
                .SetProperty(row => row.Holidays, Array.Empty<DateOnly>())
                .SetProperty(row => row.WaitNudgeMinutes, 10)
                .SetProperty(row => row.AutoCloseMinutes, 30)
                .SetProperty(row => row.UpdatedAt, new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    // Role support không có `support-settings:manage`, nên không đọc được cấu hình.
    public async Task Get_Support_Returns403()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);

        var response = await SendAsync(HttpMethod.Get, cookie, body: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Role support không có `support-settings:manage`, nên `PUT` trả 403 và không đổi dòng cấu hình.
    public async Task Put_Support_Returns403AndKeepsSettings()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);
        var auditCountBefore = await CountAuditAsync();

        var response = await SendAsync(HttpMethod.Put, cookie, ValidBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNothingChangedAsync(auditCountBefore);
    }

    [Fact]
    // Người chỉ có `support-settings:manage` (không có `conversations:reply`) vẫn đọc và lưu được cấu hình.
    public async Task GetAndPut_StaffWithOnlySettingsManage_Returns200()
    {
        var role = await IdentityTestData.CreateRoleAsync(database, [Permissions.SupportSettingsManage]);
        var (_, cookie) = await CreateSessionAsync(role.Code);

        var getResponse = await SendAsync(HttpMethod.Get, cookie, body: null);
        var putResponse = await SendAsync(HttpMethod.Put, cookie, ValidBody());

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
    }

    [Fact]
    // `GET` trả dòng mặc định của migration với giờ dạng `HH:mm` và ngày nghỉ rỗng.
    public async Task Get_Admin_ReturnsDefaultSettings()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.AdminCode);

        var response = await SendAsync(HttpMethod.Get, cookie, body: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        Assert.Equal(
            ["opensAt", "closesAt", "holidays", "waitNudgeMinutes", "autoCloseMinutes", "updatedAt"],
            data.EnumerateObject().Select(property => property.Name));
        Assert.Equal("08:00", data.GetProperty("opensAt").GetString());
        Assert.Equal("21:00", data.GetProperty("closesAt").GetString());
        Assert.Empty(data.GetProperty("holidays").EnumerateArray());
        Assert.Equal(10, data.GetProperty("waitNudgeMinutes").GetInt32());
        Assert.Equal(30, data.GetProperty("autoCloseMinutes").GetInt32());
    }

    [Fact]
    // `PUT` lưu cấu hình, trả giá trị đã lưu, ghi `updatedAt` theo đồng hồ và sắp ngày nghỉ tăng dần.
    public async Task Put_Admin_SavesSettingsAndReturnsThem()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.AdminCode);

        var response = await SendAsync(HttpMethod.Put, cookie, new
        {
            opensAt = "08:30",
            closesAt = "20:15",
            holidays = new[] { "2027-02-07", "2027-02-06" },
            waitNudgeMinutes = 5,
            autoCloseMinutes = 45,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        Assert.Equal("08:30", data.GetProperty("opensAt").GetString());
        Assert.Equal("20:15", data.GetProperty("closesAt").GetString());
        Assert.Equal(
            ["2027-02-06", "2027-02-07"],
            data.GetProperty("holidays").EnumerateArray().Select(element => element.GetString()));
        Assert.Equal(5, data.GetProperty("waitNudgeMinutes").GetInt32());
        Assert.Equal(45, data.GetProperty("autoCloseMinutes").GetInt32());
        Assert.Equal(_clock.GetUtcNow(), data.GetProperty("updatedAt").GetDateTimeOffset());

        var stored = await ReadStoredSettingsAsync();
        Assert.Equal(new TimeOnly(8, 30), stored.OpensAt);
        Assert.Equal(new TimeOnly(20, 15), stored.ClosesAt);
        Assert.Equal([new DateOnly(2027, 2, 6), new DateOnly(2027, 2, 7)], stored.Holidays);
        Assert.Equal(5, stored.WaitNudgeMinutes);
        Assert.Equal(45, stored.AutoCloseMinutes);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, stored.UpdatedAt);

        // `GET` sau `PUT` phải đọc lại đúng giá trị vừa lưu.
        var reread = await SupportConversationTestKit.ReadDataAsync(await SendAsync(HttpMethod.Get, cookie, body: null));
        Assert.Equal("08:30", reread.GetProperty("opensAt").GetString());
    }

    [Fact]
    // Mỗi `PUT` thành công ghi đúng 1 dòng audit với `before` và `after` của cấu hình.
    public async Task Put_Admin_WritesExactlyOneAuditRowWithBeforeAndAfter()
    {
        var (admin, cookie) = await CreateSessionAsync(DefaultRoles.AdminCode);
        var auditCountBefore = await CountAuditAsync();

        var response = await SendAsync(HttpMethod.Put, cookie, ValidBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(auditCountBefore + 1, await CountAuditAsync());
        await using var identity = database.CreateIdentityContext();
        // Lọc theo mã người gọi, vì mỗi test tạo một quản trị viên mới nên chỉ có đúng dòng của lần `PUT` này.
        var audit = await identity.AuditLogs.AsNoTracking()
            .SingleAsync(log => log.Action == AuditAction && log.ActorId == admin.Id);
        Assert.Equal(AuditTargetType, audit.TargetType);
        Assert.Equal(SupportSettingsConfiguration.SingletonId, audit.TargetId);
        Assert.Equal("08:00", audit.Before!.RootElement.GetProperty("opensAt").GetString());
        Assert.Equal("08:30", audit.After!.RootElement.GetProperty("opensAt").GetString());
        Assert.Equal(10, audit.Before.RootElement.GetProperty("waitNudgeMinutes").GetInt32());
        Assert.Equal(15, audit.After.RootElement.GetProperty("waitNudgeMinutes").GetInt32());
    }

    [Theory]
    [InlineData("opensAt", "21:00", "closesAt", "21:00", "opensAt")]
    [InlineData("opensAt", "22:00", "closesAt", "21:00", "opensAt")]
    [InlineData("waitNudgeMinutes", 30, "autoCloseMinutes", 30, "waitNudgeMinutes")]
    [InlineData("waitNudgeMinutes", 31, "autoCloseMinutes", 30, "waitNudgeMinutes")]
    [InlineData("waitNudgeMinutes", 0, "autoCloseMinutes", 30, "waitNudgeMinutes")]
    [InlineData("autoCloseMinutes", 1441, "waitNudgeMinutes", 10, "autoCloseMinutes")]
    [InlineData("autoCloseMinutes", 0, "waitNudgeMinutes", 10, "autoCloseMinutes")]
    [InlineData("waitNudgeMinutes", 1.5, "autoCloseMinutes", 30, "waitNudgeMinutes")]
    [InlineData("waitNudgeMinutes", "10", "autoCloseMinutes", 30, "waitNudgeMinutes")]
    [InlineData("opensAt", "8:00", "closesAt", "21:00", "opensAt")]
    [InlineData("closesAt", "24:00", "opensAt", "08:00", "closesAt")]
    [InlineData("opensAt", 800, "closesAt", "21:00", "opensAt")]
    // Giá trị sai luật trả 400 `VALIDATION_ERROR` với khóa của trường sai. Dòng cấu hình và audit không đổi.
    public async Task Put_InvalidScalarField_Returns400AndChangesNothing(
        string firstField, object firstValue, string secondField, object secondValue, string expectedKey)
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.AdminCode);
        var auditCountBefore = await CountAuditAsync();
        var body = ValidBodyAsDictionary();
        body[firstField] = firstValue;
        body[secondField] = secondValue;

        var response = await SendAsync(HttpMethod.Put, cookie, body);

        await SupportDeskHttpAssertions.AssertValidationErrorAsync(response, expectedKey);
        await AssertNothingChangedAsync(auditCountBefore);
    }

    [Theory]
    [InlineData(1, 1440)]
    [InlineData(1, 2)]
    [InlineData(1439, 1440)]
    // Biên hợp lệ: mốc nhỏ nhất 1, mốc lớn nhất 1440, và mốc nhắc lại nhỏ hơn mốc tự đóng đúng 1 phút.
    public async Task Put_MinuteBoundaries_Returns200(int waitNudgeMinutes, int autoCloseMinutes)
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.AdminCode);
        var body = ValidBodyAsDictionary();
        body["waitNudgeMinutes"] = waitNudgeMinutes;
        body["autoCloseMinutes"] = autoCloseMinutes;

        var response = await SendAsync(HttpMethod.Put, cookie, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    // Hai mốc phút cùng sai thì cả hai khóa đều có trong lỗi, để giao diện tô đúng hai ô.
    public async Task Put_BothMinuteFieldsOutOfRange_ReportsBothKeys()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.AdminCode);
        var body = ValidBodyAsDictionary();
        body["waitNudgeMinutes"] = 0;
        body["autoCloseMinutes"] = 5000;

        var response = await SendAsync(HttpMethod.Put, cookie, body);

        await SupportDeskHttpAssertions.AssertValidationErrorAsync(response, "waitNudgeMinutes", "autoCloseMinutes");
    }

    [Theory]
    [InlineData("2027-2-6")]
    [InlineData("06/02/2027")]
    [InlineData("2027-02-30")]
    [InlineData("")]
    // Ngày nghỉ sai dạng `yyyy-MM-dd` hoặc không có trên lịch trả 400 với khóa `holidays`.
    public async Task Put_HolidayWithBadFormat_Returns400(string holiday)
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.AdminCode);
        var auditCountBefore = await CountAuditAsync();
        var body = ValidBodyAsDictionary();
        body["holidays"] = new[] { "2027-02-06", holiday };

        var response = await SendAsync(HttpMethod.Put, cookie, body);

        await SupportDeskHttpAssertions.AssertValidationErrorAsync(response, "holidays");
        await AssertNothingChangedAsync(auditCountBefore);
    }

    [Fact]
    // Hai ngày nghỉ trùng nhau trả 400 với khóa `holidays`.
    public async Task Put_DuplicateHolidays_Returns400()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.AdminCode);
        var auditCountBefore = await CountAuditAsync();
        var body = ValidBodyAsDictionary();
        body["holidays"] = new[] { "2027-02-06", "2027-02-07", "2027-02-06" };

        var response = await SendAsync(HttpMethod.Put, cookie, body);

        await SupportDeskHttpAssertions.AssertValidationErrorAsync(response, "holidays");
        await AssertNothingChangedAsync(auditCountBefore);
    }

    [Fact]
    // Biên ngày nghỉ: 366 ngày khác nhau hợp lệ, 367 ngày trả 400.
    public async Task Put_HolidayCountBoundary_Allows366AndRejects367()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.AdminCode);
        var firstDay = new DateOnly(2028, 1, 1);
        var days = Enumerable.Range(0, 367).Select(offset => firstDay.AddDays(offset).ToString("yyyy-MM-dd")).ToArray();

        var okBody = ValidBodyAsDictionary();
        okBody["holidays"] = days.Take(366).ToArray();
        var okResponse = await SendAsync(HttpMethod.Put, cookie, okBody);

        var tooManyBody = ValidBodyAsDictionary();
        tooManyBody["holidays"] = days;
        var tooManyResponse = await SendAsync(HttpMethod.Put, cookie, tooManyBody);

        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
        await SupportDeskHttpAssertions.AssertValidationErrorAsync(tooManyResponse, "holidays");
    }

    [Fact]
    // Thiếu `holidays` hoặc kiểu sai (không phải mảng) trả 400 thay vì lỗi 500.
    public async Task Put_HolidaysMissingOrNotAnArray_Returns400()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.AdminCode);
        var missingBody = ValidBodyAsDictionary();
        missingBody.Remove("holidays");
        var wrongKindBody = ValidBodyAsDictionary();
        wrongKindBody["holidays"] = "2027-02-06";

        var missingResponse = await SendAsync(HttpMethod.Put, cookie, missingBody);
        var wrongKindResponse = await SendAsync(HttpMethod.Put, cookie, wrongKindBody);

        await SupportDeskHttpAssertions.AssertValidationErrorAsync(missingResponse, "holidays");
        await SupportDeskHttpAssertions.AssertValidationErrorAsync(wrongKindResponse, "holidays");
    }

    [Fact]
    // Hai route trả JSON khớp mọi ví dụ trong `admin-settings.json`.
    public async Task Responses_MatchTheContractExamples()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.AdminCode);

        var getResponse = await SendAsync(HttpMethod.Get, cookie, body: null);
        AssertContract(getResponse, "đọc cấu hình");

        var saveResponse = await SendAsync(HttpMethod.Put, cookie, new
        {
            opensAt = "08:30",
            closesAt = "21:00",
            holidays = new[] { "2027-02-06" },
            waitNudgeMinutes = 10,
            autoCloseMinutes = 30,
        });
        AssertContract(saveResponse, "lưu cấu hình");

        var opensAfterClosesResponse = await SendAsync(HttpMethod.Put, cookie, new
        {
            opensAt = "22:00",
            closesAt = "21:00",
            holidays = Array.Empty<string>(),
            waitNudgeMinutes = 10,
            autoCloseMinutes = 30,
        });
        AssertContract(opensAfterClosesResponse, "giờ mở cửa sau giờ đóng cửa");

        var nudgeNotBeforeCloseResponse = await SendAsync(HttpMethod.Put, cookie, new
        {
            opensAt = "08:00",
            closesAt = "21:00",
            holidays = Array.Empty<string>(),
            waitNudgeMinutes = 30,
            autoCloseMinutes = 30,
        });
        AssertContract(nudgeNotBeforeCloseResponse, "mốc nhắc lại không trước mốc tự đóng");
    }

    private static void AssertContract(HttpResponseMessage response, string exampleName)
    {
        var body = JsonNode.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        SupportDeskContract.AssertMatches(body, response.StatusCode, "admin-settings.json", exampleName);
    }

    private static object ValidBody() => new
    {
        opensAt = "08:30",
        closesAt = "21:00",
        holidays = new[] { "2027-02-06" },
        waitNudgeMinutes = 15,
        autoCloseMinutes = 30,
    };

    private static Dictionary<string, object?> ValidBodyAsDictionary() => new()
    {
        ["opensAt"] = "08:30",
        ["closesAt"] = "21:00",
        ["holidays"] = new[] { "2027-02-06" },
        ["waitNudgeMinutes"] = 15,
        ["autoCloseMinutes"] = 30,
    };

    private async Task<(Pickory.Modules.Identity.Entities.User User, string Cookie)> CreateSessionAsync(string roleCode)
    {
        var (user, token) = await IdentityTestData.CreateStaffSessionAsync(database, roleCode, "settings");
        return (user, $"pk_session={token}");
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string cookie, object? body)
    {
        return _client.SendAsync(ActorHttp.CreateJsonRequest(method, SettingsPath, body, cookie));
    }

    private async Task<SupportSettings> ReadStoredSettingsAsync()
    {
        await using var context = database.CreateSupportDeskContext();
        return await context.Settings.AsNoTracking()
            .SingleAsync(row => row.Id == SupportSettingsConfiguration.SingletonId);
    }

    private async Task<int> CountAuditAsync()
    {
        await using var identity = database.CreateIdentityContext();
        return await identity.AuditLogs.CountAsync(log => log.Action == AuditAction);
    }

    // Dòng cấu hình và số dòng audit phải giữ nguyên sau một `PUT` bị từ chối.
    private async Task AssertNothingChangedAsync(int auditCountBefore)
    {
        var stored = await ReadStoredSettingsAsync();
        Assert.Equal(new TimeOnly(8, 0), stored.OpensAt);
        Assert.Equal(10, stored.WaitNudgeMinutes);
        Assert.Empty(stored.Holidays);
        Assert.Equal(auditCountBefore, await CountAuditAsync());
    }
}
