namespace Pickory.Modules.Shipping;

// Luật thuần của báo phí, tách khỏi DB và HTTP để unit test kiểm từng giá trị biên.
internal static class QuoteRules
{
    // GHN chỉ nhận `insurance_value` tối đa 5.000.000 đồng (plan, kết quả tra tài liệu điểm 5).
    public const int MaxInsuredValue = 5_000_000;

    // GHN chỉ nhận `cod_amount` tối đa 50.000.000 đồng (plan, kết quả tra tài liệu điểm 5).
    public const int MaxCodAmount = 50_000_000;

    // Gói `service_type_id = 2` là hàng nhẹ (dưới 20 kg) của GHN. Dùng khi admin chưa chọn gói mặc định.
    public const int LightGoodsServiceTypeId = 2;

    // Gói `service_type_id = 5` là hàng nặng của GHN. Gói này tính phí theo từng kiện nên GHN bắt buộc `items`.
    public const int HeavyGoodsServiceTypeId = 5;

    // Giá trị hàng cao hơn trần thì khai đúng trần, vì GHN từ chối giá trị lớn hơn.
    public static int ClampInsuredValue(int insuredValue) => Math.Min(insuredValue, MaxInsuredValue);

    public static int ResolveServiceTypeId(int? configuredServiceTypeId) =>
        configuredServiceTypeId ?? LightGoodsServiceTypeId;
}
