import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import { LOW_STOCK_THRESHOLD } from '../../../shared/catalog/inventory';
import { toCatalogErrorMessage } from './catalog-errors';
import {
  describeOptionValues,
  stockBadge,
  type LowStockSku,
  type PagedList,
} from './inventory-logic';
import './products.css';
import './inventory.css';

const PAGE_SIZE = 20;

// Danh sách SKU đang bật có tồn kho từ ngưỡng trở xuống. Nhân viên có quyền xem sản phẩm đều mở được.
export function InventoryPage() {
  const [page, setPage] = useState(1);
  // Số trang thuộc khóa cache để không hiện danh sách của trang khác.
  const lowStockQuery = useQuery<PagedList<LowStockSku>>({
    queryKey: ['admin-low-stock', page],
    queryFn: () => apiRequest(`/api/admin/inventory/low-stock?page=${page}&limit=${PAGE_SIZE}`),
  });
  const lowStock = lowStockQuery.data;
  // Dùng tổng do API trả để khóa nút trang sau ở trang cuối.
  const pageCount = Math.ceil((lowStock?.total ?? 0) / (lowStock?.limit ?? PAGE_SIZE));

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
          <p role="alert" className="alert product-list-alert">
            {toCatalogErrorMessage(lowStockQuery.error, 'Không tải được danh sách tồn kho.')}
          </p>
        )}
        {lowStock && lowStock.items.length === 0 && (
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
        {lowStock && pageCount > 1 && (
          <nav className="pager" aria-label="Phân trang tồn kho">
            <button
              type="button"
              className="btn btn-sm"
              disabled={page === 1}
              onClick={() => setPage(page - 1)}
            >
              Trang trước
            </button>
            <span>
              Trang {lowStock.page} / {pageCount}
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
