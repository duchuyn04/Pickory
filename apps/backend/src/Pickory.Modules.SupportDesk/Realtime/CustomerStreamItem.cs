namespace Pickory.Modules.SupportDesk.Realtime;

// Một dòng chờ ghi ra luồng SSE phía khách: sự kiện có tên và dữ liệu, hoặc dòng chú thích giữ kết nối.
internal abstract record CustomerStreamItem
{
    // Kiểu đóng: chỉ hub và streamer của module tạo dòng, nên kết quả ghi biết mọi loại dòng.
    private CustomerStreamItem()
    {
    }

    // Dòng chú thích giữ kết nối. Client bỏ qua dòng bắt đầu bằng `:`, nên dòng này không phải sự kiện.
    internal sealed record KeepAliveComment : CustomerStreamItem
    {
        // Dòng giữ kết nối không mang dữ liệu, nên một mẫu dùng chung là đủ.
        public static readonly KeepAliveComment Instance = new();
    }

    // Một sự kiện có tên để client chọn bộ nhận. Dữ liệu được tuần tự hóa thành JSON khi ghi ra luồng.
    internal sealed record Event(string Name, object Data) : CustomerStreamItem;
}
