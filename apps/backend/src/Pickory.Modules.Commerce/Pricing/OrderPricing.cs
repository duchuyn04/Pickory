namespace Pickory.Modules.Commerce.Pricing;

// Một dòng hàng đã có giá lấy từ danh mục. Giá và số lượng luôn do server cung cấp, không lấy từ frontend.
public readonly record struct PricedLine(int UnitPrice, int Quantity);

// Kết quả tính tiền của đơn, với mọi số tiền là đồng Việt Nam kiểu `int` khớp cột `integer`.
// `Total` bằng `Subtotal` cộng `ShippingFee` trừ `ShippingDiscount`.
public sealed record OrderPricingResult(
    int Subtotal,
    int ShippingFee,
    int ShippingDiscount,
    int CustomerShippingFee,
    int Total);

// Luật tính tiền thuần, tách khỏi DB và HTTP để unit test kiểm từng giá trị biên.
public static class OrderPricing
{
    public static OrderPricingResult Calculate(
        IReadOnlyCollection<PricedLine> lines,
        int carrierShippingFee,
        int freeShippingThreshold)
    {
        ArgumentNullException.ThrowIfNull(lines);

        // Giá trị âm làm tổng tiền thấp hơn thực tế, nên mọi đầu vào sai bị chặn trước khi tính.
        if (carrierShippingFee < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(carrierShippingFee), "Phí vận chuyển không được âm.");
        }

        if (freeShippingThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(freeShippingThreshold), "Ngưỡng miễn phí vận chuyển không được âm.");
        }

        // Dùng `checked` để tổng vượt `int` ném lỗi thay vì quay vòng thành số âm, vì tiền sai lệch nghiêm trọng hơn lỗi dừng.
        var subtotal = 0;
        foreach (var line in lines)
        {
            if (line.UnitPrice < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lines), "Đơn giá không được âm.");
            }

            if (line.Quantity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(lines), "Số lượng phải từ 1 trở lên.");
            }

            subtotal = checked(subtotal + checked(line.UnitPrice * line.Quantity));
        }

        // Tạm tính bằng hoặc trên ngưỡng thì shop chịu toàn bộ phí GHN. Khách trả phí bằng 0.
        var shippingDiscount = subtotal >= freeShippingThreshold ? carrierShippingFee : 0;
        var customerShippingFee = carrierShippingFee - shippingDiscount;
        var total = checked(subtotal + customerShippingFee);

        return new OrderPricingResult(subtotal, carrierShippingFee, shippingDiscount, customerShippingFee, total);
    }

    // Tổng tiền bằng hạn mức vẫn được chọn COD. Chỉ tổng lớn hơn hạn mức bị từ chối, vì giá trị cao dễ bị đặt ảo.
    public static bool IsCodAllowed(int total, int codMaxAmount) => total <= codMaxAmount;
}
