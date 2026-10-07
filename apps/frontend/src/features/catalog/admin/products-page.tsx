import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import { LOW_STOCK_THRESHOLD } from '../../../shared/catalog/inventory';
import { useCan } from '../../identity/hooks/use-actor';
import { toCatalogErrorMessage } from './catalog-errors';
import type { ProductCategory, ProductDetail } from './product-form';
import './products.css';

interface PagedProducts {
  items: ProductDetail[];
  total: number;
  page: number;
  limit: number;
}
const statusNames: Record<string, string> = {
  draft: 'Bản nháp',
  active: 'Đang bán',
  hidden: 'Đã ẩn',
  discontinued: 'Ngừng bán',
};
// Màu badge theo trạng thái: đang bán là xanh, đã ẩn là vàng, ngừng bán là đỏ, bản nháp trung tính.
const statusBadgeClasses: Record<string, string> = {
  draft: 'badge',
  active: 'badge badge-ok',
  hidden: 'badge badge-warn',
  discontinued: 'badge badge-bad',
};

export function ProductsPage() {
  const canWrite = useCan('products:write');
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [lowStock, setLowStock] = useState(false);
  const [page, setPage] = useState(1);
  const categoriesQuery = useQuery<ProductCategory[]>({
    queryKey: ['admin-categories'],
    queryFn: () => apiRequest('/api/admin/categories'),
  });
  // Mỗi bộ lọc và số trang thuộc khóa cache để không hiển thị danh sách từ truy vấn khác.
  const productsQuery = useQuery<PagedProducts>({
    queryKey: ['admin-products', search, status, categoryId, lowStock, page],
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
  // Dùng tổng do API trả để khóa nút trang sau ở trang cuối.
  const pageCount = Math.ceil((products?.total ?? 0) / (products?.limit ?? 20));
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
        <p role="alert" className="alert">
          {toCatalogErrorMessage(categoriesQuery.error, 'Không tải được danh mục.')}
        </p>
      )}
      <section className="card card-flush">
        <div className="filters-row">
          <div className="field">
            <label htmlFor="filter-search">Tên hoặc mã SKU</label>
            <input
              id="filter-search"
              type="search"
              value={search}
              onChange={(event) => {
                setSearch(event.target.value);
                // Bộ lọc mới bắt đầu từ trang đầu để không rơi vào trang trống của kết quả cũ.
                setPage(1);
              }}
            />
          </div>
          <div className="field">
            <label htmlFor="filter-status">Trạng thái</label>
            <select
              id="filter-status"
              value={status}
              onChange={(event) => {
                setStatus(event.target.value);
                // Trạng thái mới có thể giảm tổng số trang nên quay lại trang đầu.
                setPage(1);
              }}
            >
              <option value="">Mọi trạng thái</option>
              {Object.entries(statusNames).map(([value, label]) => (
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
              onChange={(event) => {
                setCategoryId(event.target.value);
                // Danh mục khác có thể giảm tổng số trang nên quay lại trang đầu.
                setPage(1);
              }}
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
              onChange={(event) => {
                setLowStock(event.target.checked);
                // Bộ lọc tồn kho thay đổi tổng số trang nên quay lại trang đầu.
                setPage(1);
              }}
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
          <p role="alert" className="alert product-list-alert">
            {toCatalogErrorMessage(productsQuery.error, 'Không tải được sản phẩm.')}
          </p>
        )}
        {products && products.items.length === 0 && (
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
                      <span className={statusBadgeClasses[product.status] ?? 'badge'}>
                        {statusNames[product.status] ?? product.status}
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
        {products && pageCount > 1 && (
          <nav className="pager" aria-label="Phân trang sản phẩm">
            <button
              type="button"
              className="btn btn-sm"
              disabled={page === 1}
              onClick={() => setPage(page - 1)}
            >
              Trang trước
            </button>
            <span>
              Trang {products.page} / {pageCount}
            </span>
            <button
              type="button"
              className="btn btn-sm"
              disabled={page >= pageCount}
              onClick={() => setPage(page + 1)}
            >
              Trang sau
            </button>
          </nav>
        )}
      </section>
    </div>
  );
}
