using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Consent;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Throttling;
using Xunit;

namespace Pickory.IntegrationTests.Api.Identity;

// Port từ test ID-08 của bản Node cho `PUT /api/me/consent` và việc gộp consent khi đăng nhập.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ConsentTests : IClassFixture<PickoryWebApplicationFactory>
{
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public ConsentTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        // Bộ đếm đăng nhập dùng chung trong factory. Mỗi test bắt đầu với bộ đếm trống.
        factory.Services.GetRequiredService<IdentityRateLimiters>().ClearAll();
    }

    [Fact]
    // Mỗi lần đổi thêm một bản ghi mới. Lịch sử chứng minh người dùng đã chọn gì và theo bản chính sách nào.
    public async Task SaveConsent_AppendsRecordWithCurrentPolicyVersion()
    {
        var anonymousId = Guid.NewGuid().ToString();

        var firstResponse = await PutConsentAsync(new { personalization = true, marketing = false }, $"pk_anon={anonymousId}");
        var secondResponse = await PutConsentAsync(new { personalization = true, marketing = true }, $"pk_anon={anonymousId}");

        // Response luôn có `necessary: true`. Nhóm cần thiết không tắt được.
        await AssertConsentResponseAsync(firstResponse, personalization: true, marketing: false);
        await AssertConsentResponseAsync(secondResponse, personalization: true, marketing: true);

        // Bản ghi cũ không bị sửa. DB giữ cả hai lựa chọn theo thứ tự thời gian.
        await using var identity = _database.CreateIdentityContext();
        var records = await identity.ConsentRecords.Where(record => record.AnonymousId == anonymousId).OrderBy(record => record.CreatedAt).ToListAsync();
        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.Equal(ConsentRecorder.CurrentPolicyVersion, record.PolicyVersion));
        Assert.All(records, record => Assert.Null(record.UserId));
    }

    [Fact]
    // Nhóm cần thiết luôn bật. Client gửi `necessary: false` nhận lỗi validate.
    public async Task SaveConsent_NecessaryCannotBeDisabled()
    {
        var response = await PutConsentAsync(new { necessary = false, personalization = true }, $"pk_anon={Guid.NewGuid()}");

        await RegisterTests.AssertErrorAsync(
            response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", "Dữ liệu không hợp lệ (necessary: Nhóm cần thiết luôn bật và không tắt được.).");
    }

    [Fact]
    // `personalization` bắt buộc. Client phải chọn rõ ràng, không có giá trị mặc định ngầm.
    public async Task SaveConsent_PersonalizationIsRequired()
    {
        var response = await PutConsentAsync(new { marketing = true }, $"pk_anon={Guid.NewGuid()}");

        await RegisterTests.AssertErrorAsync(
            response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", "Dữ liệu không hợp lệ (personalization: Trường này là bắt buộc.).");
    }

    [Fact]
    // `marketing` mặc định tắt khi client không gửi, giống bản Node.
    public async Task SaveConsent_MissingMarketingDefaultsToFalse()
    {
        var response = await PutConsentAsync(new { personalization = true }, $"pk_anon={Guid.NewGuid()}");

        await AssertConsentResponseAsync(response, personalization: true, marketing: false);
    }

    [Fact]
    // Lựa chọn vừa lưu có hiệu lực ngay ở request kế tiếp của khách vãng lai.
    public async Task SaveConsent_GuestSeesNewConsentInGetMe()
    {
        var anonymousId = Guid.NewGuid().ToString();

        await PutConsentAsync(new { personalization = true, marketing = false }, $"pk_anon={anonymousId}");
        var meResponse = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", $"pk_anon={anonymousId}"));

        using var document = await ActorHttp.ReadJsonAsync(meResponse);
        Assert.True(document.RootElement.GetProperty("data").GetProperty("consent").GetProperty("personalization").GetBoolean());
    }

    [Fact]
    // Khách đăng nhập rồi đổi thiết bị vẫn giữ lựa chọn mới. Handler gộp consent ghi lựa chọn vào tài khoản.
    public async Task Login_CopiesNewerGuestConsentToAccountForOtherDevices()
    {
        var email = IdentityTestData.NewEmail("consent-merge");
        var user = await IdentityTestData.CreateUserAsync(_database, email, UserKind.Customer, "customer");
        await using (var setup = _database.CreateIdentityContext())
        {
            // Tài khoản có lựa chọn cũ hơn từ một thiết bị khác.
            setup.ConsentRecords.Add(new ConsentRecord
            {
                AnonymousId = Guid.NewGuid().ToString(),
                UserId = user.Id,
                Personalization = false,
                Marketing = false,
                PolicyVersion = "2026-01-01",
                CreatedAt = DateTime.UtcNow.AddHours(-1),
            });
            await setup.SaveChangesAsync();
        }

        var guestAnonymousId = Guid.NewGuid().ToString();
        await PutConsentAsync(new { personalization = true, marketing = true }, $"pk_anon={guestAnonymousId}");
        var loginResponse = await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, "/api/auth/sessions", new { email, password = IdentityTestData.DefaultPassword }, $"pk_anon={guestAnonymousId}"));
        Assert.Equal(HttpStatusCode.Created, loginResponse.StatusCode);

        // Thiết bị khác có mã ẩn danh khác. Chỉ bản ghi gắn với tài khoản cho thiết bị này thấy lựa chọn mới.
        var otherDeviceResponse = await _client.SendAsync(ActorHttp.CreateRequest(
            HttpMethod.Get, "/api/me", $"pk_anon={Guid.NewGuid()}; pk_session={ActorHttp.ReadSessionToken(loginResponse)}"));
        using var document = await ActorHttp.ReadJsonAsync(otherDeviceResponse);
        var consent = document.RootElement.GetProperty("data").GetProperty("consent");
        Assert.True(consent.GetProperty("personalization").GetBoolean());
        Assert.True(consent.GetProperty("marketing").GetBoolean());

        // Bản ghi gộp giữ phiên bản chính sách gốc. Người dùng chưa đồng ý theo bản chính sách nào khác.
        await using var identity = _database.CreateIdentityContext();
        var merged = await identity.ConsentRecords.Where(record => record.UserId == user.Id).OrderByDescending(record => record.CreatedAt).FirstAsync();
        Assert.Equal(guestAnonymousId, merged.AnonymousId);
        Assert.Equal(ConsentRecorder.CurrentPolicyVersion, merged.PolicyVersion);
    }

    [Fact]
    // Tài khoản đã có lựa chọn mới hơn thì handler không ghi đè bằng lựa chọn cũ của khách.
    public async Task Login_KeepsNewerAccountConsent()
    {
        var email = IdentityTestData.NewEmail("consent-keep");
        var user = await IdentityTestData.CreateUserAsync(_database, email, UserKind.Customer, "customer");
        var guestAnonymousId = Guid.NewGuid().ToString();
        await PutConsentAsync(new { personalization = true, marketing = true }, $"pk_anon={guestAnonymousId}");
        await using (var setup = _database.CreateIdentityContext())
        {
            // Lựa chọn của tài khoản được lưu sau lựa chọn của khách.
            setup.ConsentRecords.Add(new ConsentRecord
            {
                AnonymousId = Guid.NewGuid().ToString(),
                UserId = user.Id,
                Personalization = false,
                Marketing = false,
                PolicyVersion = ConsentRecorder.CurrentPolicyVersion,
                CreatedAt = DateTime.UtcNow.AddMinutes(1),
            });
            await setup.SaveChangesAsync();
        }

        await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, "/api/auth/sessions", new { email, password = IdentityTestData.DefaultPassword }, $"pk_anon={guestAnonymousId}"));

        // Handler không thêm bản ghi nào cho tài khoản.
        await using var identity = _database.CreateIdentityContext();
        Assert.Equal(1, await identity.ConsentRecords.CountAsync(record => record.UserId == user.Id));
    }

    private Task<HttpResponseMessage> PutConsentAsync(object body, string cookieHeader)
    {
        return _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Put, "/api/me/consent", body, cookieHeader));
    }

    private static async Task AssertConsentResponseAsync(HttpResponseMessage response, bool personalization, bool marketing)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var consent = document.RootElement.GetProperty("data").GetProperty("consent");
        Assert.True(consent.GetProperty("necessary").GetBoolean());
        Assert.Equal(personalization, consent.GetProperty("personalization").GetBoolean());
        Assert.Equal(marketing, consent.GetProperty("marketing").GetBoolean());
    }
}
