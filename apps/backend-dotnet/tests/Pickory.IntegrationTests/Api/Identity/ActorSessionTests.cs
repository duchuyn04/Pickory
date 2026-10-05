using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Entities;
using Xunit;

namespace Pickory.IntegrationTests.Api.Identity;

// Hợp đồng phiên `pk_session` theo actor middleware của bản Node.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ActorSessionTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    [Fact]
    public async Task CustomerSession_ReturnsProfileRolesPermissionsWithoutRenewal()
    {
        var seeded = await SeedSessionAsync(
            UserKind.Customer,
            expiresIn: TimeSpan.FromDays(20),
            rolePermissions: [[Permissions.OrdersRead, "legacy:removed"], [Permissions.ReportsRead, Permissions.OrdersRead]]);

        var response = await SendGetMeAsync(seeded);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(seeded.AnonymousId, data.GetProperty("anonymousId").GetString());
        Assert.Equal(seeded.UserId, data.GetProperty("userId").GetString());
        Assert.Equal(seeded.Email, data.GetProperty("email").GetString());
        Assert.Equal("Khách Thử", data.GetProperty("name").GetString());
        Assert.Equal("0901234567", data.GetProperty("phone").GetString());
        Assert.Equal("customer", data.GetProperty("kind").GetString());
        Assert.Equal(seeded.RoleCodes.Order(StringComparer.Ordinal), ReadStrings(data.GetProperty("roles")).Order(StringComparer.Ordinal));
        // Quyền lạ bị bỏ. Quyền trùng giữa hai role chỉ xuất hiện một lần.
        Assert.Equal([Permissions.OrdersRead, Permissions.ReportsRead], ReadStrings(data.GetProperty("permissions")).Order(StringComparer.Ordinal));

        // Phiên còn trên 7 ngày không được gia hạn.
        Assert.Empty(ActorHttp.ReadSetCookies(response, "pk_session"));
        Assert.Equal(seeded.ExpiresAt, (await ReadSessionExpiryAsync(seeded.TokenHash))!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task CustomerSession_UnderSevenDaysIsRenewedForThirtyDays()
    {
        var seeded = await SeedSessionAsync(UserKind.Customer, expiresIn: TimeSpan.FromDays(3));
        var requestedAt = DateTime.UtcNow;

        var response = await SendGetMeAsync(seeded);

        var sessionCookie = Assert.Single(ActorHttp.ReadSetCookies(response, "pk_session"));
        Assert.Equal(seeded.RawToken, ActorHttp.ReadCookieValue(sessionCookie));
        Assert.Contains("max-age=2592000", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", sessionCookie, StringComparison.OrdinalIgnoreCase);
        var renewedExpiry = await ReadSessionExpiryAsync(seeded.TokenHash);
        Assert.Equal(requestedAt.AddDays(30), renewedExpiry!.Value, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task StaffSession_IsNeverRenewed()
    {
        var seeded = await SeedSessionAsync(UserKind.Staff, expiresIn: TimeSpan.FromHours(1));

        var response = await SendGetMeAsync(seeded);

        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("staff", document.RootElement.GetProperty("data").GetProperty("kind").GetString());
        Assert.Empty(ActorHttp.ReadSetCookies(response, "pk_session"));
        Assert.Equal(seeded.ExpiresAt, (await ReadSessionExpiryAsync(seeded.TokenHash))!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task AdminRole_ReceivesWholePermissionCatalog()
    {
        var seeded = await SeedSessionAsync(UserKind.Staff, expiresIn: TimeSpan.FromHours(1), roleCode: "admin");

        var response = await SendGetMeAsync(seeded);

        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal(Permissions.All, ReadStrings(document.RootElement.GetProperty("data").GetProperty("permissions")));
    }

    [Fact]
    public async Task ExpiredSession_IsDeletedAndCookieCleared()
    {
        var seeded = await SeedSessionAsync(UserKind.Customer, expiresIn: TimeSpan.FromMinutes(-1));

        var response = await SendGetMeAsync(seeded);

        await AssertGuestWithClearedSessionCookieAsync(response);
        Assert.Null(await ReadSessionExpiryAsync(seeded.TokenHash));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task LockedOrDeletedAccount_LosesSessionButKeepsSessionRow(bool locked, bool deleted)
    {
        var seeded = await SeedSessionAsync(UserKind.Customer, expiresIn: TimeSpan.FromDays(20), locked: locked, deleted: deleted);

        var response = await SendGetMeAsync(seeded);

        await AssertGuestWithClearedSessionCookieAsync(response);
        // Bản Node chỉ xóa phiên hết hạn. Phiên của tài khoản bị khóa vẫn nằm trong DB.
        Assert.NotNull(await ReadSessionExpiryAsync(seeded.TokenHash));
    }

    [Fact]
    public async Task UnknownToken_IsTreatedAsGuestAndCookieCleared()
    {
        var anonymousId = Guid.NewGuid().ToString();
        var request = ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", $"pk_anon={anonymousId}; pk_session=khong-ton-tai");

        var response = await _client.SendAsync(request);

        await AssertGuestWithClearedSessionCookieAsync(response);
    }

    [Fact]
    public async Task SignedInConsent_UsesLatestRecordOfUserOrAnonymousId()
    {
        var seeded = await SeedSessionAsync(UserKind.Customer, expiresIn: TimeSpan.FromDays(20));
        await using (var identity = database.CreateIdentityContext())
        {
            identity.ConsentRecords.AddRange(
                new ConsentRecord { AnonymousId = Guid.NewGuid().ToString(), UserId = seeded.UserId, Personalization = false, Marketing = false, PolicyVersion = "v1", CreatedAt = DateTime.UtcNow.AddDays(-2) },
                new ConsentRecord { AnonymousId = seeded.AnonymousId, Personalization = true, Marketing = true, PolicyVersion = "v1", CreatedAt = DateTime.UtcNow.AddDays(-1) });
            await identity.SaveChangesAsync();
        }

        var response = await SendGetMeAsync(seeded);

        using var document = await ActorHttp.ReadJsonAsync(response);
        var consent = document.RootElement.GetProperty("data").GetProperty("consent");
        Assert.True(consent.GetProperty("personalization").GetBoolean());
        Assert.True(consent.GetProperty("marketing").GetBoolean());
    }

    private async Task<HttpResponseMessage> SendGetMeAsync(SeededSession seeded)
    {
        var cookieHeader = $"pk_anon={seeded.AnonymousId}; pk_session={seeded.RawToken}";
        return await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", cookieHeader));
    }

    private static async Task AssertGuestWithClearedSessionCookieAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("guest", data.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("userId").ValueKind);

        var clearedCookie = Assert.Single(ActorHttp.ReadSetCookies(response, "pk_session"));
        Assert.Equal(string.Empty, ActorHttp.ReadCookieValue(clearedCookie));
        Assert.Contains("expires=Thu, 01 Jan 1970 00:00:00 GMT", clearedCookie, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<SeededSession> SeedSessionAsync(
        UserKind kind,
        TimeSpan expiresIn,
        string[][]? rolePermissions = null,
        string? roleCode = null,
        bool locked = false,
        bool deleted = false)
    {
        var now = DateTime.UtcNow;
        var user = new User
        {
            Email = $"actor-{Guid.NewGuid():N}@example.test",
            Name = "Khách Thử",
            Phone = "0901234567",
            Kind = kind,
            LockedAt = locked ? now.AddHours(-1) : null,
            DeletedAt = deleted ? now.AddHours(-1) : null,
            UpdatedAt = now,
        };

        // Mỗi test tạo role riêng. Role `admin` dùng chung vì mã role là khóa duy nhất trong DB.
        var roles = new List<Role>();
        if (roleCode is not null)
        {
            roles.Add(new Role { Code = roleCode, Name = roleCode });
        }

        foreach (var permissions in rolePermissions ?? [])
        {
            var role = new Role { Code = $"test-role-{Guid.NewGuid():N}", Name = "Role thử" };
            foreach (var permission in permissions)
            {
                role.Permissions.Add(new RolePermission { Permission = permission });
            }

            roles.Add(role);
        }

        // Token gốc được băm bằng SHA-256 hex như `hashToken` của bản Node. Test không gọi code đang được kiểm tra.
        var rawToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        var expiresAt = TruncateToMilliseconds(now.Add(expiresIn));

        await using var identity = database.CreateIdentityContext();
        identity.Users.Add(user);
        foreach (var role in roles)
        {
            var existingRole = await identity.Roles.SingleOrDefaultAsync(value => value.Code == role.Code);
            user.Roles.Add(new UserRole { User = user, Role = existingRole ?? role });
        }

        identity.Sessions.Add(new Session { TokenHash = tokenHash, User = user, ExpiresAt = expiresAt });
        await identity.SaveChangesAsync();

        return new SeededSession(user.Id, user.Email, Guid.NewGuid().ToString(), rawToken, tokenHash, expiresAt, roles.Select(role => role.Code).ToList());
    }

    private async Task<DateTime?> ReadSessionExpiryAsync(string tokenHash)
    {
        await using var identity = database.CreateIdentityContext();
        return await identity.Sessions
            .Where(session => session.TokenHash == tokenHash)
            .Select(session => (DateTime?)session.ExpiresAt)
            .SingleOrDefaultAsync();
    }

    private static List<string> ReadStrings(JsonElement array)
    {
        return array.EnumerateArray().Select(item => item.GetString()!).ToList();
    }

    // Cột thời gian giữ 3 chữ số phần giây lẻ. Giá trị mong đợi phải cùng độ chính xác với DB.
    private static DateTime TruncateToMilliseconds(DateTime value)
    {
        return new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    private sealed record SeededSession(
        string UserId,
        string Email,
        string AnonymousId,
        string RawToken,
        string TokenHash,
        DateTime ExpiresAt,
        IReadOnlyList<string> RoleCodes);
}
