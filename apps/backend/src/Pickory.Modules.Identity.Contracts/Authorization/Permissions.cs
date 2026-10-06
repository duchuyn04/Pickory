namespace Pickory.Modules.Identity.Contracts.Authorization;

// Danh mục quyền nằm trong code, không nằm trong DB. Thêm quyền mới thuộc nhóm "Ask first" của dự án.
public static class Permissions
{
    // identity
    public const string StaffRead = "staff:read";
    public const string StaffManage = "staff:manage";
    public const string RolesManage = "roles:manage";
    public const string CustomersRead = "customers:read";
    public const string CustomersManage = "customers:manage";
    public const string AuditLogsRead = "audit-logs:read";

    // catalog
    public const string ProductsRead = "products:read";
    public const string ProductsWrite = "products:write";
    public const string InventoryWrite = "inventory:write";

    // commerce
    public const string OrdersRead = "orders:read";
    public const string OrdersUpdateStatus = "orders:update-status";
    public const string OrdersCancel = "orders:cancel";
    public const string OrdersFulfill = "orders:fulfill";
    public const string PaymentsRead = "payments:read";
    public const string PaymentsReconcile = "payments:reconcile";
    public const string ReviewsModerate = "reviews:moderate";
    public const string CommerceSettingsManage = "commerce-settings:manage";
    public const string RefundsManage = "refunds:manage";

    // support-desk
    public const string ConversationsRead = "conversations:read";
    public const string ConversationsReply = "conversations:reply";
    public const string SupportSettingsManage = "support-settings:manage";

    // shipping
    public const string ShippingSettingsManage = "shipping-settings:manage";

    // knowledge-base
    public const string KnowledgeRead = "knowledge:read";
    public const string KnowledgeWrite = "knowledge:write";

    // ai-gateway
    public const string AiUsageRead = "ai-usage:read";
    public const string AiSettingsManage = "ai-settings:manage";

    // admin-console
    public const string ReportsRead = "reports:read";
    public const string ReportsExport = "reports:export";

    // Giữ thứ tự như bản Node. `GET /api/me` trả quyền của admin theo thứ tự này.
    public static IReadOnlyList<string> All { get; } =
    [
        StaffRead,
        StaffManage,
        RolesManage,
        CustomersRead,
        CustomersManage,
        AuditLogsRead,
        ProductsRead,
        ProductsWrite,
        InventoryWrite,
        OrdersRead,
        OrdersUpdateStatus,
        OrdersCancel,
        OrdersFulfill,
        PaymentsRead,
        PaymentsReconcile,
        ReviewsModerate,
        CommerceSettingsManage,
        RefundsManage,
        ConversationsRead,
        ConversationsReply,
        SupportSettingsManage,
        ShippingSettingsManage,
        KnowledgeRead,
        KnowledgeWrite,
        AiUsageRead,
        AiSettingsManage,
        ReportsRead,
        ReportsExport,
    ];

    private static readonly HashSet<string> KnownPermissions = new(All, StringComparer.Ordinal);

    // Chuỗi từ DB hoặc từ client có thể chứa quyền đã bị xóa khỏi code. Chỉ quyền trong danh mục mới có hiệu lực.
    public static bool IsKnown(string permission)
    {
        return KnownPermissions.Contains(permission);
    }
}
