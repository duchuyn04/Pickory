namespace Pickory.Modules.Commerce.Contracts.Reading;

// Dòng sản phẩm trong đơn hàng kèm tên biến thể và giá trị thành tiền.
public sealed record OrderLine(
    string ProductId,
    string SkuId,
    string ProductName,
    string? VariantName,
    int Quantity,
    int UnitPrice,
    int LineTotal);
