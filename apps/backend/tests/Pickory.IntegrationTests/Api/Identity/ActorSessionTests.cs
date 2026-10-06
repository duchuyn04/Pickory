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
    // Test tự đọc và gửi cookie. Client không tự lưu cookie giữa các request.
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    [Fact]
    // Phiên khách hàng còn dài trả đủ hồ sơ, role và quyền. API không ghi lại phiên khi chưa cần gia hạn.
    public async Task CustomerSession_ReturnsProfileRolesPermissionsWithoutRenewal()
    {
        // Hai role có một quyền trùng và một quyền không có trong danh mục.
        var seeded = await SeedSessionAsync(
            UserKind.Customer,
            expiresIn: TimeSpan.FromDays(20),
            rolePermissions: [[Permissions.OrdersRead, "legacy:removed"], [Permissions.ReportsRead, Permissions.OrdersRead]]);

        var response = await SendGetMeAsync(seeded);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        // Hồ sơ trả về khớp tài khoản của phiên, giống `GET /api/me` của bản Node.
        Assert.Equal(seeded.AnonymousId, data.GetProperty("anonymousId").GetString());
        Assert.Equal(seeded.UserId, data.GetProperty("userId").GetString());
        Assert.Equal(seeded.Email, data.GetProperty("email").GetString());
        Assert.Equal("Khách Thử", data.GetProperty("name").GetString());
        Assert.Equal("0901234567", data.GetProperty("phone").GetString());
        Assert.Equal("customer", data.GetProperty("kind").GetString());
        // So sánh sau khi sắp xếp. Thứ tự role trong DB không phải là hợp đồng API.
        Assert.Equal(seeded.RoleCodes.Order(StringComparer.Ordinal), ReadStrings(data.GetProperty("roles")).Order(StringComparer.Ordinal));
        // Quyền lạ bị bỏ. Quyền trùng giữa hai role chỉ xuất hiện một lần.
        Assert.Equal([Permissions.OrdersRead, Permissions.ReportsRead], ReadStrings(data.GetProperty("permissions")).Order(StringComparer.Ordinal));

        // Phiên còn trên 7 ngày không được gia hạn.
        Assert.Empty(ActorHttp.ReadSetCookies(response, "pk_session"));
        Assert.Equal(seeded.ExpiresAt, (await ReadSessionExpiryAsync(seeded.TokenHash))!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    // Phiên khách hàng còn dưới 7 ngày được gia hạn 30 ngày. Người dùng đang hoạt động không bị đăng xuất.
    public async Task CustomerSession_UnderSevenDaysIsRenewedForThirtyDays()
    {
        // Phiên còn 3 ngày nằm dưới ngưỡng gia hạn 7 ngày.
        var seeded = await SeedSessionAsync(UserKind.Customer, expiresIn: TimeSpan.FromDays(3));
        var requestedAt = DateTime.UtcNow;

        var response = await SendGetMeAsync(seeded);

        // API gửi lại cùng token với hạn 30 ngày. Không gửi lại thì trình duyệt xóa cookie theo hạn cũ.
        var sessionCookie = Assert.Single(ActorHttp.ReadSetCookies(response, "pk_session"));
        Assert.Equal(seeded.RawToken, ActorHttp.ReadCookieValue(sessionCookie));
        Assert.Contains("max-age=2592000", sessionCookie, StringComparison.OrdinalIgnoreCase);
        // Cookie gia hạn giữ thuộc tính bảo mật. Script không đọc được token và trang khác không gửi kèm token.
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", sessionCookie, StringComparison.OrdinalIgnoreCase);
        // Hạn trong DB khớp hạn của cookie. Sai lệch một phút bù thời gian chạy test.
        var renewedExpiry = await ReadSessionExpiryAsync(seeded.TokenHash);
        Assert.Equal(requestedAt.AddDays(30), renewedExpiry!.Value, TimeSpan.FromMinutes(1));
    }

    [Fact]
    // Phiên nhân viên không bao giờ tự gia hạn. Hạn cố định giới hạn rủi ro khi phiên nhân viên bị lộ.
    public async Task StaffSession_IsNeverRenewed()
    {
        // Phiên còn 1 giờ, thấp hơn nhiều so với ngưỡng gia hạn của khách hàng.
        var seeded = await SeedSessionAsync(UserKind.Staff, expiresIn: TimeSpan.FromHours(1));

        var response = await SendGetMeAsync(seeded);

        // Phiên vẫn hợp lệ, nên actor là nhân viên.
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("staff", document.RootElement.GetProperty("data").GetProperty("kind").GetString());
        // API không gửi cookie mới và không đổi hạn trong DB.
        Assert.Empty(ActorHttp.ReadSetCookies(response, "pk_session"));
        Assert.Equal(seeded.ExpiresAt, (await ReadSessionExpiryAsync(seeded.TokenHash))!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    // Role `admin` nhận cả danh mục quyền. Quyền mới thêm vào code có hiệu lực cho admin ngay.
    public async Task AdminRole_ReceivesWholePermissionCatalog()
    {
        var seeded = await SeedSessionAsync(UserKind.Staff, expiresIn: TimeSpan.FromHours(1), roleCode: "admin");

        var response = await SendGetMeAsync(seeded);

        // Danh sách quyền bằng `Permissions.All`, không phụ thuộc quyền lưu trong DB.
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal(Permissions.All, ReadStrings(document.RootElement.GetProperty("data").GetProperty("permissions")));
    }

    [Fact]
    // Phiên hết hạn không cho đăng nhập. API xóa dòng phiên và xóa cookie để trình duyệt ngừng gửi token cũ.
    public async Task ExpiredSession_IsDeletedAndCookieCleared()
    {
        // Phiên hết hạn một phút trước request.
        var seeded = await SeedSessionAsync(UserKind.Customer, expiresIn: TimeSpan.FromMinutes(-1));

        var response = await SendGetMeAsync(seeded);

        // Request chạy với quyền khách vãng lai. Cookie phiên bị xóa.
        await AssertGuestWithClearedSessionCookieAsync(response);
        // Dòng phiên hết hạn không còn trong DB.
        Assert.Null(await ReadSessionExpiryAsync(seeded.TokenHash));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    // Tài khoản bị khóa hoặc bị xóa mất phiên ngay ở request kế tiếp, dù phiên chưa hết hạn.
    public async Task LockedOrDeletedAccount_LosesSessionButKeepsSessionRow(bool locked, bool deleted)
    {
        // Phiên còn 20 ngày. Chỉ trạng thái tài khoản làm phiên mất hiệu lực.
        var seeded = await SeedSessionAsync(UserKind.Customer, expiresIn: TimeSpan.FromDays(20), locked: locked, deleted: deleted);

        var response = await SendGetMeAsync(seeded);

        // Request chạy với quyền khách vãng lai. Cookie phiên bị xóa.
        await AssertGuestWithClearedSessionCookieAsync(response);
        // Bản Node chỉ xóa phiên hết hạn. Phiên của tài khoản bị khóa vẫn nằm trong DB.
        Assert.NotNull(await ReadSessionExpiryAsync(seeded.TokenHash));
    }

    [Fact]
    // Token không có trong DB bị coi là khách vãng lai. Token giả không mở được phiên nào.
    public async Task UnknownToken_IsTreatedAsGuestAndCookieCleared()
    {
        var anonymousId = Guid.NewGuid().ToString();
        // Gửi token tự đặt, không có dòng phiên tương ứng.
        var request = ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", $"pk_anon={anonymousId}; pk_session=khong-ton-tai");

        var response = await _client.SendAsync(request);

        // API xóa cookie phiên. Trình duyệt ngừng gửi token sai ở các request sau.
        await AssertGuestWithClearedSessionCookieAsync(response);
    }

    [Fact]
    // Người đã đăng nhập nhận consent mới nhất theo tài khoản hoặc mã ẩn danh. Lựa chọn trước khi đăng nhập vẫn có hiệu lực.
    public async Task SignedInConsent_UsesLatestRecordOfUserOrAnonymousId()
    {
        var seeded = await SeedSessionAsync(UserKind.Customer, expiresIn: TimeSpan.FromDays(20));
        // Bản cũ gắn với tài khoản. Bản mới chỉ gắn với mã ẩn danh của trình duyệt hiện tại.
        await using (var identity = database.CreateIdentityContext())
        {
            identity.ConsentRecords.AddRange(
                new ConsentRecord { AnonymousId = Guid.NewGuid().ToString(), UserId = seeded.UserId, Personalization = false, Marketing = false, PolicyVersion = "v1", CreatedAt = DateTime.UtcNow.AddDays(-2) },
                new ConsentRecord { AnonymousId = seeded.AnonymousId, Personalization = true, Marketing = true, PolicyVersion = "v1", CreatedAt = DateTime.UtcNow.AddDays(-1) });
            await identity.SaveChangesAsync();
        }

        var response = await SendGetMeAsync(seeded);

        // Giá trị khớp bản mới theo mã ẩn danh, không khớp bản cũ theo tài khoản.
        using var document = await ActorHttp.ReadJsonAsync(response);
        var consent = document.RootElement.GetProperty("data").GetProperty("consent");
        Assert.True(consent.GetProperty("personalization").GetBoolean());
        Assert.True(consent.GetProperty("marketing").GetBoolean());
    }

    // Gửi cả cookie ẩn danh và cookie phiên, giống trình duyệt đã đăng nhập.
    private async Task<HttpResponseMessage> SendGetMeAsync(SeededSession seeded)
    {
        var cookieHeader = $"pk_anon={seeded.AnonymousId}; pk_session={seeded.RawToken}";
        return await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", cookieHeader));
    }

    // Phiên không dùng được không làm request lỗi. Request chạy tiếp với quyền khách vãng lai.
    private static async Task AssertGuestWithClearedSessionCookieAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("guest", data.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("userId").ValueKind);

        // Cookie rỗng với hạn năm 1970 là cách trình duyệt nhận lệnh xóa cookie.
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
        // Đặt mốc khóa hoặc xóa trong quá khứ. Middleware coi tài khoản đã khóa hoặc đã xóa ngay khi test chạy.
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

        // Mỗi danh sách quyền tạo một role mới. Test kiểm tra quyền gộp từ nhiều role.
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
        // Cắt về mili giây để so sánh chính xác với giá trị đọc lại từ DB.
        var expiresAt = TruncateToMilliseconds(now.Add(expiresIn));

        await using var identity = database.CreateIdentityContext();
        identity.Users.Add(user);
        foreach (var role in roles)
        {
            // Dùng lại role đã có cùng mã. Thêm role trùng mã sẽ vi phạm khóa duy nhất trong DB.
            var existingRole = await identity.Roles.SingleOrDefaultAsync(value => value.Code == role.Code);
            user.Roles.Add(new UserRole { User = user, Role = existingRole ?? role });
        }

        identity.Sessions.Add(new Session { TokenHash = tokenHash, User = user, ExpiresAt = expiresAt });
        await identity.SaveChangesAsync();

        // Tạo mã ẩn danh mới cho mỗi phiên. Consent của test khác không lẫn vào kết quả.
        return new SeededSession(user.Id, user.Email, Guid.NewGuid().ToString(), rawToken, tokenHash, expiresAt, roles.Select(role => role.Code).ToList());
    }

    // Đọc hạn phiên trực tiếp từ DB. Giá trị null nghĩa là API đã xóa dòng phiên.
    private async Task<DateTime?> ReadSessionExpiryAsync(string tokenHash)
    {
        await using var identity = database.CreateIdentityContext();
        return await identity.Sessions
            .Where(session => session.TokenHash == tokenHash)
            .Select(session => (DateTime?)session.ExpiresAt)
            .SingleOrDefaultAsync();
    }

    // Đổi mảng JSON thành danh sách chuỗi. Test so sánh role và quyền bằng `Assert.Equal`.
    private static List<string> ReadStrings(JsonElement array)
    {
        return array.EnumerateArray().Select(item => item.GetString()!).ToList();
    }

    // Cột thời gian giữ 3 chữ số phần giây lẻ. Giá trị mong đợi phải cùng độ chính xác với DB.
    private static DateTime TruncateToMilliseconds(DateTime value)
    {
        return new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    // Giữ cả token gốc và hash. Test gửi token gốc qua cookie và tra phiên trong DB bằng hash.
    private sealed record SeededSession(
        string UserId,
        string Email,
        string AnonymousId,
        string RawToken,
        string TokenHash,
        DateTime ExpiresAt,
        IReadOnlyList<string> RoleCodes);
}
