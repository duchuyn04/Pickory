namespace Pickory.Modules.Commerce.Orders;

// Bên thực hiện một lần chuyển trạng thái. Quyền chi tiết của nhân viên do `RequirePermission` kiểm ở route, không kiểm ở đây.
public enum TransitionActor
{
    Customer,
    Staff,
    System,
}
