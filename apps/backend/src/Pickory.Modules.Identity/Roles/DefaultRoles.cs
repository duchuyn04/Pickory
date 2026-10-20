using Pickory.Modules.Identity.Contracts.Authorization;

namespace Pickory.Modules.Identity.Roles;

// Bốn role tạo sẵn và quyền mặc định theo bảng trong spec. Giá trị giữ nguyên bản Node.
public static class DefaultRoles
{
    public const string CustomerCode = "customer";
    public const string AdminCode = "admin";
    public const string SalesCode = "sales";
    public const string SupportCode = "support";

    // Role `customer` và `admin` là role hệ thống. Admin không sửa hoặc xóa được hai role này.
    public static IReadOnlyList<DefaultRole> All { get; } =
    [
        new(CustomerCode, "Khách hàng", IsSystem: true, []),
        // Admin không có dòng quyền trong DB. `ActorPermissions` cấp mọi quyền cho admin, kể cả quyền thêm sau này.
        new(AdminCode, "Quản trị viên", IsSystem: true, []),
        new(SalesCode, "Nhân viên bán hàng", IsSystem: false,
        [
            Permissions.CustomersRead,
            Permissions.ProductsRead,
            Permissions.OrdersRead,
            Permissions.OrdersUpdateStatus,
            Permissions.OrdersCancel,
            Permissions.OrdersFulfill,
            Permissions.PaymentsRead,
            Permissions.PaymentsReconcile,
            Permissions.KnowledgeRead,
            Permissions.ReportsRead,
        ]),
        new(SupportCode, "Nhân viên CSKH", IsSystem: false,
        [
            Permissions.CustomersRead,
            Permissions.ProductsRead,
            Permissions.OrdersRead,
            Permissions.ReviewsModerate,
            Permissions.ConversationsRead,
            Permissions.ConversationsReply,
            Permissions.KnowledgeRead,
        ]),
    ];

    public sealed record DefaultRole(string Code, string Name, bool IsSystem, IReadOnlyList<string> Permissions);
}
