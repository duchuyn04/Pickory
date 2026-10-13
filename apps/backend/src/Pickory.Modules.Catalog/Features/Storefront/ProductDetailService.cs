using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Images;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Catalog.Reading;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Kết quả tra slug của route chi tiết: tìm thấy, đã đổi slug, hoặc không có.
public abstract record ProductSlugLookup
{
    public sealed record Found(ProductDetail Detail) : ProductSlugLookup;

    public sealed record Moved(string CurrentSlug) : ProductSlugLookup;

    public sealed record NotFound : ProductSlugLookup;
}

// Đọc chi tiết sản phẩm cho khách. Mọi truy vấn qua `VisibleToCustomers`, nên `draft` và `hidden` không lộ ra.
public sealed class ProductDetailService(CatalogDbContext database, CloudinaryCredentials credentials)
{
    public Task<ProductDetail?> FindByIdOrSlugAsync(string idOrSlug, CancellationToken cancellationToken) =>
        FindVisibleAsync(product => product.Id == idOrSlug || product.Slug == idOrSlug, cancellationToken);

    public async Task<IReadOnlyList<ProductDetail>> FindBySlugsAsync(
        IReadOnlyList<string> slugs, CancellationToken cancellationToken)
    {
        // Cùng bộ lọc và quan hệ của chi tiết đơn, nên sản phẩm nháp hoặc ẩn không lọt vào danh sách.
        var products = await VisibleProductsWithDetail()
            .Where(product => slugs.Contains(product.Slug))
            .ToListAsync(cancellationToken);
        // Tập rỗng không cần nạp danh mục, tránh một truy vấn không có sản phẩm để dựng.
        if (products.Count == 0)
        {
            return [];
        }

        // Nạp danh mục một lần cho cả tập, rồi dựng cùng dữ liệu giá, ảnh và bảo hành như route đơn.
        var categories = await CategoryAttributeIndex.LoadAsync(database, cancellationToken);
        var details = products.Select(product => ProductDetailAssembler.Build(product, categories, credentials));
        return RequestedIdOrder.Arrange(slugs, details, detail => detail.Slug);
    }

    public async Task<ProductSlugLookup> LookupBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        // Slug hiện tại thắng slug cũ. Sản phẩm khách không thấy được dừng ở đây và trả 404, không đi tiếp sang lịch sử.
        var slugIsCurrent = await database.Products.AnyAsync(product => product.Slug == slug, cancellationToken);
        if (slugIsCurrent)
        {
            var detail = await FindVisibleAsync(product => product.Slug == slug, cancellationToken);
            return detail is null ? new ProductSlugLookup.NotFound() : new ProductSlugLookup.Found(detail);
        }

        // Slug cũ chỉ chuyển hướng khi sản phẩm khách thấy được. Nếu không, đích chuyển hướng sẽ lộ sản phẩm `draft` hoặc `hidden`.
        var currentSlug = await database.ProductSlugHistories.AsNoTracking()
            .Where(history => history.OldSlug == slug)
            .Join(database.Products.AsNoTracking().VisibleToCustomers(),
                history => history.ProductId, product => product.Id, (history, product) => product.Slug)
            .FirstOrDefaultAsync(cancellationToken);
        return currentSlug is null ? new ProductSlugLookup.NotFound() : new ProductSlugLookup.Moved(currentSlug);
    }

    // Lấy một trang sản phẩm theo `Id` tăng dần cho `smart-search`. Lấy dư một dòng để biết còn trang sau hay không.
    public async Task<IndexingPage> ListForIndexingAsync(string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        IndexingPaging.EnsurePageSizeInRange(pageSize);

        var products = VisibleProductsWithDetail();
        if (!string.IsNullOrEmpty(cursor))
        {
            // Cursor là `Id` cuối của trang trước. Phép so sánh lớn hơn hẳn bảo đảm dòng cuối đó không xuất hiện lại.
            products = products.Where(product => product.Id.CompareTo(cursor) > 0);
        }

        var fetched = await products
            .OrderBy(product => product.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var (pageProducts, next) = IndexingPaging.Split(fetched, pageSize, product => product.Id);
        var categories = await CategoryAttributeIndex.LoadAsync(database, cancellationToken);
        var details = pageProducts
            .Select(product => ProductDetailAssembler.Build(product, categories, credentials))
            .ToList();
        return new IndexingPage(details, next);
    }

    private async Task<ProductDetail?> FindVisibleAsync(
        Expression<Func<Product, bool>> match, CancellationToken cancellationToken)
    {
        var product = await VisibleProductsWithDetail().FirstOrDefaultAsync(match, cancellationToken);
        if (product is null)
        {
            return null;
        }

        var categories = await CategoryAttributeIndex.LoadAsync(database, cancellationToken);
        return ProductDetailAssembler.Build(product, categories, credentials);
    }

    // Tách thành nhiều truy vấn nhỏ để SKU và ảnh không nhân bản dòng sản phẩm.
    private IQueryable<Product> VisibleProductsWithDetail() =>
        database.Products.AsNoTracking().AsSplitQuery()
            .VisibleToCustomers()
            .Include(product => product.Brand)
            .Include(product => product.Skus)
            .Include(product => product.Images);
}
