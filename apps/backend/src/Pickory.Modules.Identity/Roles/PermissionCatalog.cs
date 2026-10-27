using Pickory.Modules.Identity.Contracts.Authorization;

namespace Pickory.Modules.Identity.Roles;

// Thông tin hiển thị của từng quyền cho bảng tick trên trang quản lý role. Nội dung giữ nguyên bản Node.
public static class PermissionCatalog
{
    // Thứ tự khớp `Permissions.All`. Giao diện gom quyền theo `Group` và giữ thứ tự trong mỗi nhóm.
    public static IReadOnlyList<PermissionCatalogEntry> Entries { get; } =
    [
        // identity
        new(Permissions.StaffRead, "identity", "Xem nhân viên", "Xem danh sách tài khoản nhân viên"),
        new(Permissions.StaffManage, "identity", "Quản lý nhân viên", "Mời, khóa, mở khóa nhân viên và gán role"),
        new(Permissions.RolesManage, "identity", "Quản lý role", "Tạo, sửa, xóa role và phân quyền"),
        new(Permissions.CustomersRead, "identity", "Xem khách hàng", "Xem danh sách và chi tiết tài khoản khách hàng"),
        new(Permissions.CustomersManage, "identity", "Quản lý khách hàng", "Khóa và mở khóa tài khoản khách hàng"),
        new(Permissions.AuditLogsRead, "identity", "Xem nhật ký thao tác", "Xem nhật ký hành động trong back-office"),

        // catalog
        new(Permissions.ProductsRead, "catalog", "Xem sản phẩm", "Xem sản phẩm bao gồm cả sản phẩm ẩn"),
        new(Permissions.ProductsWrite, "catalog", "Sửa sản phẩm", "Tạo, sửa thông tin sản phẩm và danh mục"),

        // commerce
        new(Permissions.OrdersRead, "commerce", "Xem đơn hàng", "Xem danh sách và chi tiết đơn hàng"),
        new(Permissions.OrdersUpdateStatus, "commerce", "Đổi trạng thái đơn", "Cập nhật trạng thái xử lý đơn hàng"),
        new(Permissions.OrdersCancel, "commerce", "Hủy đơn hàng", "Hủy đơn hàng"),
        new(Permissions.OrdersFulfill, "commerce", "Vận chuyển đơn hàng", "Tạo, hủy vận đơn GHN cho đơn hàng"),
        new(Permissions.PaymentsRead, "commerce", "Xem thanh toán", "Xem giao dịch và thanh toán"),
        new(Permissions.PaymentsReconcile, "commerce", "Đối soát thanh toán", "Khớp tay giao dịch SePay"),
        new(Permissions.ReviewsModerate, "commerce", "Kiểm duyệt đánh giá", "Ẩn, hiện đánh giá sản phẩm của khách"),
        new(Permissions.CommerceSettingsManage, "commerce", "Cấu hình thương mại", "Ngưỡng freeship, hạn mức COD, tài khoản SePay"),
        new(Permissions.RefundsManage, "commerce", "Quản lý hoàn tiền", "Đánh dấu đã hoàn tiền"),

        // support-desk
        new(Permissions.ConversationsRead, "support-desk", "Xem hội thoại CSKH", "Xem hội thoại khách hàng"),
        new(Permissions.ConversationsReply, "support-desk", "Trả lời CSKH", "Tiếp nhận và phản hồi hội thoại chatbot chuyển sang"),
        new(Permissions.SupportSettingsManage, "support-desk", "Cấu hình CSKH", "Giờ làm việc, ngày nghỉ, câu trả lời mẫu"),

        // shipping
        new(Permissions.ShippingSettingsManage, "shipping", "Cấu hình vận chuyển", "Địa chỉ lấy hàng, cấu hình kết nối GHN"),

        // knowledge-base
        new(Permissions.KnowledgeRead, "knowledge-base", "Xem cơ sở tri thức", "Xem mọi tài liệu kể cả nội bộ"),
        new(Permissions.KnowledgeWrite, "knowledge-base", "Soạn tài liệu tri thức", "Soạn thảo, nhập và đăng tài liệu"),

        // admin-console
        new(Permissions.ReportsRead, "admin-console", "Xem báo cáo", "Xem dashboard và báo cáo tổng hợp"),
        new(Permissions.ReportsExport, "admin-console", "Xuất dữ liệu báo cáo", "Xuất báo cáo, đơn hàng ra file Excel/CSV"),
    ];
}
