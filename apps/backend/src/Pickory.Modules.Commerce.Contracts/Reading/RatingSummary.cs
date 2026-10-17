namespace Pickory.Modules.Commerce.Contracts.Reading;

// Tóm tắt đánh giá sản phẩm gồm điểm trung bình, tổng số lượng và phân bố theo sao.
// Phân bố luôn đủ năm mức sao từ 1 đến 5 để bên gọi vẽ biểu đồ thuận tiện.
public sealed record RatingSummary(
    double? Average,
    int Count,
    IReadOnlyDictionary<int, int> Distribution);
