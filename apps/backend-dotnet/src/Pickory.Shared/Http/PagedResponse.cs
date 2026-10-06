namespace Pickory.Shared.Http;

// Trang dữ liệu kèm tổng số dòng. Giao diện tính số trang từ `Total` và `Limit`, giống bản Node.
public sealed record PagedResponse<TItem>(IReadOnlyList<TItem> Items, int Total, int Page, int Limit);
