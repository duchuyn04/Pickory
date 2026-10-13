import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import { FilteredEmptyState } from '../../../shared/filtered-empty-state';
import { Pager } from '../../../shared/pager';
import { SearchInput } from '../../../shared/search-input';
import { useListFilters } from '../../../shared/use-list-filters';
import { LOW_STOCK_THRESHOLD } from '../../../shared/catalog/inventory';
import { toCatalogErrorMessage } from './catalog-errors';
import {
  describeOptionValues,
  stockBadge,
  type LowStockSku,
  type PagedList,
} from './inventory-logic';
import { ErrorAlert } from '../../../shared/error-alert';
import './products.css';
import './inventory.css';

const PAGE_SIZE = 20;

// Tên tham số bộ lọc trên URL. Trùng tên tham số của `GET /api/admin/inventory/low-stock`.
const FILTER_NAMES = ['search'] as const;

// Danh sách SKU đang bật có tồn kho từ ngưỡng trở xuống. Nhân viên có quyền xem sản phẩm đều mở được.
export function InventoryPage() {
  // Từ khóa và trang nằm trên URL. Mở lại link cho đúng danh sách đã lọc.
  const { values, page, setFilter, setPage } = useListFilters(FILTER_NAMES);
  // Từ khóa và số trang thuộc khóa cache để không hiện danh sách của truy vấn khác.
  const lowStockQuery = useQuery<PagedList<LowStockSku>>({
    queryKey: ['admin-low-stock', values, page],
    queryFn: () => {
      // `URLSearchParams` mã hóa ký tự đặc biệt trong từ khóa để URL không sai.
      const params = new URLSearchParams({ page: String(page), limit: String(PAGE_SIZE) });
      if (values.search !== '') params.set('search', values.search);
      return apiRequest(`/api/admin/inventory/low-stock?${params}`);
    },
  });
  const lowStock = lowStockQuery.data;
  const hasFilters = values.search !== '';

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Tồn kho</h1>
          <p>
            SKU đang bán có tồn kho từ {LOW_STOCK_THRESHOLD} trở xuống. Chọn SKU để xem lịch sử hoặc
            điều chỉnh.
          </p>
        </div>
      </div>
      <section className="card card-flush">
        <div className="filters-row">
          <SearchInput
            label="Tìm theo mã SKU hoặc tên sản phẩm"
            value={values.search}
            onSearch={(text) => setFilter('search', text)}
          />
        </div>
        <div className="product-list-head">
          <h2>SKU sắp hết hàng</h2>
          {lowStock && <span className="muted small">{lowStock.total} SKU</span>}
        </div>
        {lowStockQuery.isPending && (
          <p role="status" className="empty-state">
            Đang tải danh sách tồn kho...
          </p>
        )}
        {lowStockQuery.isError && (
          <ErrorAlert
            message={toCatalogErrorMessage(
              lowStockQuery.error,
              'Không tải được danh sách tồn kho.',
            )}
            className="product-list-alert"
          />
        )}
        {lowStock && lowStock.items.length === 0 && (hasFilters || page > 1) && (
          <FilteredEmptyState page={page} onFirstPage={() => setPage(1)} />
        )}
        {lowStock && lowStock.items.length === 0 && !hasFilters && page === 1 && (
          <p className="empty-state">Không có SKU nào sắp hết hàng.</p>
        )}
        {lowStock && lowStock.items.length > 0 && (
          <div className="table-wrap">
            <table className="tbl inventory-table">
              <thead>
                <tr>
                  <th scope="col">SKU</th>
                  <th scope="col">Tùy chọn</th>
                  <th scope="col" className="num">
                    Tồn kho
                  </th>
                  <th scope="col">Tình trạng</th>
                  <th scope="col">
                    <span className="sr">Thao tác</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {lowStock.items.map((sku) => {
                  const badge = stockBadge(sku.stock);
                  return (
                    <tr key={sku.skuId}>
                      <th scope="row">
                        {/* Mã SKU là liên kết để ở màn hẹp, nơi cột thao tác nằm ngoài vùng nhìn, vẫn mở được trang SKU. */}
                        <Link
                          className="wrap-anywhere"
                          to={`/admin/products/${sku.productId}/skus/${sku.skuId}/inventory`}
                        >
                          {sku.code}
                        </Link>
                        <span className="cell-sub wrap-anywhere">{sku.productName}</span>
                      </th>
                      <td>{describeOptionValues(sku.optionValues)}</td>
                      <td className="num">{sku.stock}</td>
                      <td>
                        <span className={badge.className}>{badge.label}</span>
                      </td>
                      <td className="num">
                        <Link
                          className="btn btn-sm"
                          aria-label={`Xem tồn kho ${sku.code}`}
                          to={`/admin/products/${sku.productId}/skus/${sku.skuId}/inventory`}
                        >
                          Xem tồn kho
                        </Link>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
        {lowStock && (
          <Pager
            page={page}
            total={lowStock.total}
            limit={lowStock.limit}
            onPageChange={setPage}
            label="Phân trang tồn kho"
          />
        )}
      </section>
    </div>
  );
}
