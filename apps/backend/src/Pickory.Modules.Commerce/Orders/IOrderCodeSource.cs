namespace Pickory.Modules.Commerce.Orders;

// Nguồn mã đơn của route đặt đơn. Tách thành interface để test ép được tình huống trùng mã.
public interface IOrderCodeSource
{
    string Next();
}

// Nguồn dùng trong production. Mã lấy từ bộ sinh số mật mã, để kẻ tấn công không đoán được mã đơn kế tiếp.
public sealed class RandomOrderCodeSource : IOrderCodeSource
{
    public string Next() => OrderCodeGenerator.Generate();
}
