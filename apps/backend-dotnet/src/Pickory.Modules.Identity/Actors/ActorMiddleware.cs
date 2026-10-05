using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Identity.Sessions;
using Pickory.Modules.Identity.Tokens;
using Pickory.Shared.Configuration;

namespace Pickory.Modules.Identity.Actors;

// Chạy trước mọi route. Middleware chống CSRF, cấp cookie ẩn danh, rồi xác định actor của request.
public sealed class ActorMiddleware(
    RequestDelegate next,
    AppConfig config,
    IHostEnvironment environment,
    ILogger<ActorMiddleware> logger)
{
    // Phiên khách hàng chỉ được gia hạn khi còn ít hơn ngưỡng này. DB vì vậy không bị ghi ở mọi request.
    private static readonly TimeSpan CustomerSessionRenewalThreshold = TimeSpan.FromDays(7);

    public async Task InvokeAsync(HttpContext context, IdentityDbContext database)
    {
        // Kiểm tra nguồn trước mọi bước khác. Request giả mạo không nhận cookie và không chạm DB.
        RequestOriginGuard.EnsureTrustedOrigin(context.Request, config.WebOrigin);

        var anonymousId = ReadOrIssueAnonymousId(context);
        var actor = await ResolveSessionActorAsync(context, database, anonymousId)
            ?? await CreateGuestActorAsync(database, anonymousId, context.RequestAborted);
        context.SetActor(actor);

        await next(context);
    }

    private string ReadOrIssueAnonymousId(HttpContext context)
    {
        var anonymousId = context.Request.Cookies[SessionCookies.AnonymousCookieName];
        if (!string.IsNullOrWhiteSpace(anonymousId))
        {
            return anonymousId;
        }

        // Lần ghé thăm đầu tiên chưa có cookie. Mã ẩn danh theo dõi giỏ hàng và consent của khách chưa đăng nhập.
        var issuedAnonymousId = Guid.NewGuid().ToString();
        SessionCookies.AppendAnonymousCookie(context.Response, issuedAnonymousId, environment.IsProduction());
        return issuedAnonymousId;
    }

    private async Task<Actor?> ResolveSessionActorAsync(HttpContext context, IdentityDbContext database, string anonymousId)
    {
        var rawToken = context.Request.Cookies[SessionCookies.SessionCookieName];
        if (string.IsNullOrEmpty(rawToken))
        {
            return null;
        }

        // DB chỉ lưu hash của token. Middleware băm token trong cookie trước khi tra cứu.
        var tokenHash = SecureTokens.HashToken(rawToken);
        // Một truy vấn lấy phiên, tài khoản, role và quyền. Không có cache, nên quyền mới có hiệu lực ở request kế tiếp.
        var session = await database.Sessions
            .AsNoTracking()
            .Include(value => value.User!)
                .ThenInclude(user => user.Roles)
                .ThenInclude(userRole => userRole.Role!)
                .ThenInclude(role => role.Permissions)
            .SingleOrDefaultAsync(value => value.TokenHash == tokenHash, context.RequestAborted);

        var now = DateTime.UtcNow;
        // Tài khoản bị khóa hoặc bị xóa mất phiên ngay ở request kế tiếp.
        if (session?.User is { } user && session.ExpiresAt > now && user.LockedAt is null && user.DeletedAt is null)
        {
            // Phiên nhân viên giữ cố định 12 giờ để giới hạn rủi ro. Chỉ phiên khách hàng tự gia hạn.
            if (user.Kind == UserKind.Customer)
            {
                await RenewCustomerSessionIfNeededAsync(context, database, session, rawToken, now);
            }

            var roleCodes = user.Roles.Select(userRole => userRole.Role!.Code).ToList();
            var storedPermissions = user.Roles.SelectMany(userRole => userRole.Role!.Permissions.Select(rolePermission => rolePermission.Permission));
            // Tìm theo cả tài khoản và mã ẩn danh. Lựa chọn consent trước khi đăng nhập vẫn có hiệu lực.
            var consent = await FindLatestConsentAsync(
                database.ConsentRecords.Where(record => record.UserId == user.Id || record.AnonymousId == anonymousId),
                context.RequestAborted);

            return new Actor
            {
                AnonymousId = anonymousId,
                UserId = user.Id,
                Kind = user.Kind == UserKind.Staff ? ActorKind.Staff : ActorKind.Customer,
                Roles = roleCodes,
                Permissions = ActorPermissions.Merge(roleCodes, storedPermissions),
                Consent = consent,
                SessionCreatedAt = session.CreatedAt,
            };
        }

        if (session is not null && session.ExpiresAt <= now)
        {
            await DeleteExpiredSessionAsync(database, session.Id, context.RequestAborted);
        }

        // Cookie không còn dùng được. Trình duyệt cần xóa cookie để ngừng gửi token cũ.
        SessionCookies.DeleteSessionCookie(context.Response);
        return null;
    }

    private async Task RenewCustomerSessionIfNeededAsync(
        HttpContext context,
        IdentityDbContext database,
        Session session,
        string rawToken,
        DateTime now)
    {
        if (session.ExpiresAt - now >= CustomerSessionRenewalThreshold)
        {
            return;
        }

        // Gia hạn tới 30 ngày sau request hiện tại. Người dùng đang hoạt động không bị đăng xuất.
        var extendedExpiresAt = now.Add(SessionCookies.CustomerSessionLifetime);
        await database.Sessions
            .Where(value => value.Id == session.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ExpiresAt, extendedExpiresAt), context.RequestAborted);

        // Trình duyệt vẫn xóa cookie theo hạn cũ nếu API không gửi lại cookie với hạn mới.
        SessionCookies.AppendSessionCookie(context.Response, rawToken, SessionCookies.CustomerSessionLifetime, environment.IsProduction());
    }

    private async Task DeleteExpiredSessionAsync(IdentityDbContext database, string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            await database.Sessions.Where(value => value.Id == sessionId).ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Dọn phiên hết hạn chỉ giữ DB gọn. Lỗi dọn dẹp không được làm hỏng request của người dùng.
            logger.LogWarning(exception, "Không xóa được phiên hết hạn {SessionId}.", sessionId);
        }
    }

    private static async Task<Actor> CreateGuestActorAsync(IdentityDbContext database, string anonymousId, CancellationToken cancellationToken)
    {
        var consent = await FindLatestConsentAsync(
            database.ConsentRecords.Where(record => record.AnonymousId == anonymousId),
            cancellationToken);

        // Khách vãng lai không có role và không có quyền back-office nào.
        return new Actor
        {
            AnonymousId = anonymousId,
            UserId = null,
            Kind = ActorKind.Guest,
            Roles = [],
            Permissions = [],
            Consent = consent,
        };
    }

    private static async Task<ConsentState> FindLatestConsentAsync(IQueryable<ConsentRecord> consentRecords, CancellationToken cancellationToken)
    {
        var latestConsent = await consentRecords
            .AsNoTracking()
            .OrderByDescending(record => record.CreatedAt)
            .Select(record => new ConsentState(record.Personalization, record.Marketing))
            .FirstOrDefaultAsync(cancellationToken);

        // Người dùng chưa chọn thì mọi lựa chọn đều tắt.
        return latestConsent ?? ConsentState.AllDisabled;
    }
}
