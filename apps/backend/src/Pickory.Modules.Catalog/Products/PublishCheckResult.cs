namespace Pickory.Modules.Catalog.Products;

// Kết quả kiểm tra đăng bán. `Reasons` liệt kê mọi điều kiện chưa đạt để admin sửa một lần.
public sealed record PublishCheckResult(IReadOnlyList<string> Reasons)
{
    public bool Publishable => Reasons.Count == 0;
}
