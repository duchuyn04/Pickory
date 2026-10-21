import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import { ActiveFilterBar, type ActiveFilter } from '../../../shared/active-filter-bar';
import { FilteredEmptyState } from '../../../shared/filtered-empty-state';
import { HighlightMatch } from '../../../shared/highlight-match';
import { Pager } from '../../../shared/pager';
import { rowNumber } from '../../../shared/row-number';
import { SearchInput } from '../../../shared/search-input';
import { useListFilters } from '../../../shared/use-list-filters';
import { LOW_STOCK_THRESHOLD } from '../../../shared/catalog/inventory';
import { useCan } from '../../identity/hooks/use-actor';
import { toCatalogErrorMessage } from './catalog-errors';
import type { ProductCategory, ProductDetail } from './product-form';
import {
  PRODUCT_STATUS_BADGE_CLASSES,
  PRODUCT_STATUS_NAMES,
  type ProductStatus,
} from './product-status';
import { ErrorAlert } from '../../../shared/error-alert';
import { ProductThumbnail } from './product-thumbnail';
import './products.css';

interface PagedProducts {
  items: ProductDetail[];
  total: number;
  page: number;
  limit: number;
}

// Tên tham số bộ lọc trên URL. Trùng tên tham số của `GET /api/admin/products`.
const FILTER_NAMES = ['search', 'status', 'categoryId', 'lowStock'] as const;

// Mã SKU của mọi biến thể, nối bằng dấu phẩy. Biến thể chưa có mã thì bỏ qua.
function skuCodesOf(product: ProductDetail): string {
  return product.variants
    .map((variant) => variant.code)
    .filter(Boolean)
    .join(', ');
}

interface ActiveFilterSource {
  keyword: string;
  status: string;
  categoryId: string;
  lowStock: boolean;
  categories: ProductCategory[] | undefined;
}

// Nhãn cho từng bộ lọc đang bật, theo thứ tự các ô trên hàng lọc.
function buildActiveFilters(
  source: ActiveFilterSource,
): ActiveFilter<(typeof FILTER_NAMES)[number]>[] {
  const filters: ActiveFilter<(typeof FILTER_NAMES)[number]>[] = [];
  if (source.keyword) filters.push({ name: 'search', label: `Từ khóa: ${source.keyword}` });
  if (source.status) {
    // Trạng thái đến từ URL nên có thể là giá trị lạ. Hiện nguyên giá trị đó, và chỉ tra bảng tên bằng khóa riêng của bảng.
    const statusName = Object.hasOwn(PRODUCT_STATUS_NAMES, source.status)
      ? PRODUCT_STATUS_NAMES[source.status as ProductStatus]
      : source.status;
    filters.push({ name: 'status', label: `Trạng thái: ${statusName}` });
  }
  if (source.categoryId) {
    // Danh mục chưa tải xong thì hiện mã. Nhãn vẫn có, nên người dùng vẫn bỏ được bộ lọc.
    const categoryName =
      source.categories?.find((category) => category.id === source.categoryId)?.name ??
      source.categoryId;
    filters.push({ name: 'categoryId', label: `Danh mục: ${categoryName}` });
  }
  if (source.lowStock) filters.push({ name: 'lowStock', label: 'Sắp hết hàng' });
  return filters;
}

export function ProductsPage() {
  const canWrite = useCan('products:write');
  // Bộ lọc và trang nằm trên URL. Mở lại link cho đúng danh sách đã lọc.
  const { values, page, setFilter, clearFilters, setPage } = useListFilters(FILTER_NAMES);
  const { search, status, categoryId } = values;
  const lowStock = values.lowStock === 'true';
  const hasFilters = FILTER_NAMES.some((name) => values[name] !== '');
  const categoriesQuery = useQuery<ProductCategory[]>({
    queryKey: ['admin-categories'],
    queryFn: () => apiRequest('/api/admin/categories'),
  });
  // Mỗi bộ lọc và số trang thuộc khóa cache để không hiển thị danh sách từ truy vấn khác.
  const productsQuery = useQuery<PagedProducts>({
    queryKey: ['admin-products', values, page],
    queryFn: () => {
      // Mã hóa từng bộ lọc để tên hoặc mã SKU có ký tự đặc biệt không đổi ý nghĩa URL.
      const params = new URLSearchParams({ page: String(page), limit: '20' });
      if (search.trim()) params.set('search', search.trim());
      if (status) params.set('status', status);
      if (categoryId) params.set('categoryId', categoryId);
      if (lowStock) params.set('lowStock', 'true');
      return apiRequest<PagedProducts>(`/api/admin/products?${params}`);
    },
    // Giữ bảng cũ trong lúc tải kết quả mới, để bảng không biến mất rồi hiện lại mỗi lần gõ.
    placeholderData: keepPreviousData,
  });
  const products = productsQuery.data;
  // Dữ liệu đang hiện là của truy vấn trước. Bảng mờ đi và ô tìm hiện vòng xoay cho tới khi kết quả mới về.
  const isStale = productsQuery.isPlaceholderData;
  const keyword = search.trim();
  const activeFilters = buildActiveFilters({
    keyword,
    status,
    categoryId,
    lowStock,
    categories: categoriesQuery.data,
  });
  const summary = products
    ? keyword
      ? `${products.total} sản phẩm khớp "${keyword}"`
      : `${products.total} sản phẩm`
    : undefined;
  return (
    <div className="page product-page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Sản phẩm</h1>
          <p>Tra cứu và cập nhật danh mục bán hàng.</p>
        </div>
        {/* Chỉ quyền products:write được mở trang tạo sản phẩm. */}
        {canWrite && (
          <Link className="btn btn-primary" to="/admin/products/new">
            Tạo sản phẩm
          </Link>
        )}
      </div>
      {categoriesQuery.isError && (
        <ErrorAlert
          message={toCatalogErrorMessage(categoriesQuery.error, 'Không tải được danh mục.')}
        />
      )}
      <section className="card card-flush">
        <div className="filters-row">
          <SearchInput
            label="Tên hoặc mã SKU"
            value={search}
            onSearch={(text) => setFilter('search', text)}
            busy={isStale}
          />
          <div className="field">
            <label htmlFor="filter-status">Trạng thái</label>
            <select
              id="filter-status"
              value={status}
              onChange={(event) => setFilter('status', event.target.value)}
            >
              <option value="">Mọi trạng thái</option>
              {Object.entries(PRODUCT_STATUS_NAMES).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor="filter-category">Danh mục</label>
            <select
              id="filter-category"
              value={categoryId}
              onChange={(event) => setFilter('categoryId', event.target.value)}
            >
              <option value="">Mọi danh mục</option>
              {categoriesQuery.data?.map((category) => (
                <option key={category.id} value={category.id}>
                  {category.name}
                </option>
              ))}
            </select>
          </div>
          <label className="check product-low-stock">
            <input
              type="checkbox"
              checked={lowStock}
              // Tắt ô thì xóa tham số. URL không giữ `lowStock=false` vô nghĩa.
              onChange={(event) => setFilter('lowStock', event.target.checked ? 'true' : '')}
            />
            Sắp hết hàng (tối đa {LOW_STOCK_THRESHOLD})
          </label>
        </div>
        <ActiveFilterBar
          filters={activeFilters}
          summary={summary}
          onRemove={(name) => setFilter(name, '')}
          onClearAll={clearFilters}
        />
        <div className="product-list-head">
          <h2>Danh sách</h2>
          {/* Thanh bộ lọc đã hiện số kết quả. Có thanh thì bỏ dòng này để số không lặp hai lần. */}
          {products && activeFilters.length === 0 && (
            <span className="muted small">{products.total} sản phẩm</span>
          )}
        </div>
        {productsQuery.isPending && (
          <p role="status" className="empty-state">
            Đang tải sản phẩm...
          </p>
        )}
        {productsQuery.isError && (
          <ErrorAlert
            message={toCatalogErrorMessage(productsQuery.error, 'Không tải được sản phẩm.')}
            className="product-list-alert"
          />
        )}
        {/* Bảng cũ còn hiện trong lúc chờ kết quả mới. `aria-busy` báo cho trình đọc màn hình rằng nội dung sắp đổi. */}
        <div
          className={isStale ? 'product-list-body is-stale' : 'product-list-body'}
          aria-busy={isStale || undefined}
        >
          {products && products.items.length === 0 && (hasFilters || page > 1) && (
            <FilteredEmptyState page={page} onFirstPage={() => setPage(1)} />
          )}
          {products && products.items.length === 0 && !hasFilters && page === 1 && (
            <p className="empty-state">Không có sản phẩm phù hợp.</p>
          )}
          {products && products.items.length > 0 && (
            <div className="table-wrap">
              <table className="tbl product-list-table">
                <thead>
                  <tr>
                    <th scope="col" className="num col-row-number">
                      STT
                    </th>
                    <th scope="col">Sản phẩm</th>
                    <th scope="col">Danh mục</th>
                    <th scope="col">Trạng thái</th>
                    <th scope="col" className="num">
                      Giá thấp nhất
                    </th>
                    <th scope="col">
                      <span className="sr">Thao tác</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {products.items.map((product, index) => (
                    <tr key={product.id}>
                      {/* STT theo `page` và `limit` của response đang hiện, không theo URL. Bảng cũ còn giữ STT cũ cho tới khi response mới về. */}
                      <td className="num col-row-number">{rowNumber(products, index)}</td>
                      <th scope="row">
                        {/* Ảnh bên trái, khối tên và mã SKU bên phải. Ô `th` giữ nguyên là ô bảng, nên flex nằm ở thẻ con. */}
                        <div className="product-cell">
                          <ProductThumbnail images={product.images} productName={product.name} />
                          <div className="product-cell-text">
                            <HighlightMatch text={product.name} keyword={keyword} />
                            {/* Mã SKU nằm dưới tên như bản mẫu, để bảng ít cột và vừa màn rộng. */}
                            <span className="cell-sub wrap-anywhere">
                              {/* Tô trên cả chuỗi mã đã nối, vì mã SKU có thể khớp từ khóa ở bất kỳ biến thể nào. */}
                              <HighlightMatch text={skuCodesOf(product)} keyword={keyword} />
                            </span>
                          </div>
                        </div>
                      </th>
                      <td>
                        {categoriesQuery.data?.find(
                          (category) => category.id === product.categoryId,
                        )?.name ?? product.categoryId}
                      </td>
                      <td>
                        <span
                          className={
                            PRODUCT_STATUS_BADGE_CLASSES[product.status as ProductStatus] ?? 'badge'
                          }
                        >
                          {PRODUCT_STATUS_NAMES[product.status as ProductStatus] ?? product.status}
                        </span>
                      </td>
                      <td className="num">
                        {product.minPrice === null
                          ? 'Chưa có'
                          : `${product.minPrice.toLocaleString('vi-VN')} đ`}
                      </td>
                      <td className="num">
                        <Link className="btn btn-sm" to={`/admin/products/${product.id}`}>
                          {canWrite ? 'Chỉnh sửa' : 'Xem'}
                        </Link>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {products && (
            <Pager
              page={page}
              total={products.total}
              limit={products.limit}
              onPageChange={setPage}
              label="Phân trang sản phẩm"
            />
          )}
        </div>
      </section>
    </div>
  );
}
