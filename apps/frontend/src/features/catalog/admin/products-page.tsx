import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import { FilteredEmptyState } from '../../../shared/filtered-empty-state';
import { Pager } from '../../../shared/pager';
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
import './products.css';

interface PagedProducts {
  items: ProductDetail[];
  total: number;
  page: number;
  limit: number;
}

// Tên tham số bộ lọc trên URL. Trùng tên tham số của `GET /api/admin/products`.
const FILTER_NAMES = ['search', 'status', 'categoryId', 'lowStock'] as const;

export function ProductsPage() {
  const canWrite = useCan('products:write');
  // Bộ lọc và trang nằm trên URL. Mở lại link cho đúng danh sách đã lọc.
  const { values, page, setFilter, setPage } = useListFilters(FILTER_NAMES);
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
  });
  const products = productsQuery.data;
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
        <div className="product-list-head">
          <h2>Danh sách</h2>
          {products && <span className="muted small">{products.total} sản phẩm</span>}
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
                {products.items.map((product) => (
                  <tr key={product.id}>
                    <th scope="row">
                      {product.name}
                      {/* Mã SKU nằm dưới tên như bản mẫu, để bảng ít cột và vừa màn rộng. */}
                      <span className="cell-sub wrap-anywhere">
                        {product.variants
                          .map((variant) => variant.code)
                          .filter(Boolean)
                          .join(', ')}
                      </span>
                    </th>
                    <td>
                      {categoriesQuery.data?.find((category) => category.id === product.categoryId)
                        ?.name ?? product.categoryId}
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
      </section>
    </div>
  );
}
