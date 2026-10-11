namespace Pickory.Modules.Commerce.Contracts.Orders;

// Phương thức thanh toán của đơn. Nhãn trong DB là `cod` và `sepay`.
public enum PaymentMethod
{
    Cod,
    Sepay,
}
