namespace Pickory.Modules.Identity.Contracts.Actors;

// Người đang gửi request. Actor middleware của identity tạo đối tượng này cho mọi request.
public sealed record Actor
{
    // Luôn có giá trị từ cookie `pk_anon`. Giá trị này gộp dữ liệu của khách khi họ đăng nhập sau.
    public required string AnonymousId { get; init; }

    // Bằng null khi người gửi chưa đăng nhập.
    public string? UserId { get; init; }

    public required ActorKind Kind { get; init; }

    public required IReadOnlyList<string> Roles { get; init; }

    // Middleware nạp lại quyền ở mỗi request. Quyền mới có hiệu lực ngay ở request kế tiếp.
    public required IReadOnlyList<string> Permissions { get; init; }

    public required ConsentState Consent { get; init; }

    // Thời điểm tạo phiên hiện tại. Thao tác nhạy cảm dùng giá trị này để yêu cầu đăng nhập lại gần đây.
    public DateTime? SessionCreatedAt { get; init; }

    public bool IsAuthenticated => UserId is not null;

    public bool HasPermission(string permission)
    {
        return Permissions.Contains(permission, StringComparer.Ordinal);
    }
}
