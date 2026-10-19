namespace Pickory.Modules.Shipping.FallbackFees;

// Bộ khóa của một phí GHN đã lưu. Cả sáu giá trị phải khớp, vì GHN tính phí theo từng giá trị này.
internal sealed record QuoteCacheKey(
    string ReceiverWardCode,
    string PickupWardCode,
    int ServiceTypeId,
    long ChargeableWeightGrams,
    int InsuredValue,
    int CodAmount);
