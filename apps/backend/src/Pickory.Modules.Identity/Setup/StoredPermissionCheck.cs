using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Persistence;

namespace Pickory.Modules.Identity.Setup;

// Chạy một lần khi API khởi động. Người vận hành biết DB còn quyền đã bị xóa khỏi code.
public sealed class StoredPermissionCheck(IServiceScopeFactory scopeFactory, ILogger<StoredPermissionCheck> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            // `DbContext` là dịch vụ scoped. Hosted service tạo scope riêng để lấy `DbContext`.
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await WarnAboutUnknownPermissionsAsync(database, logger, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Bước kiểm tra chỉ để cảnh báo. DB tạm thời không kết nối được thì API vẫn khởi động, giống bản Node.
            logger.LogWarning(exception, "Không thể kiểm tra role_permissions khi khởi động.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public static async Task WarnAboutUnknownPermissionsAsync(IdentityDbContext database, ILogger logger, CancellationToken cancellationToken)
    {
        var storedPermissions = await database.RolePermissions
            .AsNoTracking()
            .Select(rolePermission => new { rolePermission.Permission, RoleCode = rolePermission.Role!.Code })
            .ToListAsync(cancellationToken);

        foreach (var storedPermission in storedPermissions.Where(value => !Permissions.IsKnown(value.Permission)))
        {
            // Chỉ cảnh báo, không xóa. Xóa dữ liệu là quyết định của người vận hành.
            logger.LogWarning(
                "Quyền lạ {Permission} trong role {RoleCode} được phát hiện khi khởi động.",
                storedPermission.Permission,
                storedPermission.RoleCode);
        }
    }
}
