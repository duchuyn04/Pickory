import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import { useCan } from '../../identity/hooks/use-actor';
import { AdjustStockDialog } from './adjust-stock-dialog';
import { toCatalogErrorMessage } from './catalog-errors';
import {
  describeOptionValues,
  formatDelta,
  formatMovementTime,
  movementTypeLabel,
  stockBadge,
  type AdjustStockResult,
  type InventoryMovement,
  type PagedList,
} from './inventory-logic';
import type { ProductDetail } from './product-form';
import { ErrorAlert } from '../../../shared/error-alert';
import './products.css';
import './inventory.css';

const PAGE_SIZE = 20;

// Mã đơn hoặc người làm của một movement. Chỉ có mã người dùng nên rút gọn 8 ký tự, tên đầy đủ nằm ở `title`.
function MovementSource({ movement }: { movement: InventoryMovement }) {
  if (movement.orderId) return <span className="wrap-anywhere">Đơn {movement.orderId}</span>;
  if (movement.actorId) {
    return (
      <span className="mono" title={movement.actorId}>
        Nhân viên {movement.actorId.slice(0, 8)}
      </span>
    );
  }
  return <span className="muted">-</span>;
}

// Trang tồn kho của một SKU: số tồn hiện tại, lịch sử movement và (nếu có quyền) hộp thoại điều chỉnh.
export function SkuInventoryPage() {
  const { productId = '', skuId = '' } = useParams();
  const canAdjust = useCan('inventory:write');
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [isDialogOpen, setIsDialogOpen] = useState(false);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  // Dùng chung khóa với form sản phẩm để hai màn không giữ hai bản số tồn khác nhau.
  const productQuery = useQuery<ProductDetail>({
    queryKey: ['admin-product', productId],
    queryFn: () => apiRequest(`/api/admin/products/${productId}`),
  });
  const movementsQuery = useQuery<PagedList<InventoryMovement>>({
    queryKey: ['sku-movements', skuId, page],
    queryFn: () => apiRequest(`/api/admin/skus/${skuId}/movements?page=${page}&limit=${PAGE_SIZE}`),
  });

  const product = productQuery.data;
  const sku = product?.variants.find((variant) => variant.id === skuId);
  const movements = movementsQuery.data;
  const pageCount = Math.ceil((movements?.total ?? 0) / (movements?.limit ?? PAGE_SIZE));

  function handleAdjusted(result: AdjustStockResult) {
    // Ghi kết quả vào cache ngay để số tồn và lịch sử đổi mà không chờ lần tải lại.
    queryClient.setQueryData<ProductDetail>(['admin-product', productId], (current) =>
      current
        ? {
            ...current,
            variants: current.variants.map((variant) =>
              variant.id === result.skuId ? { ...variant, stock: result.stock } : variant,
            ),
          }
        : current,
    );
    queryClient.setQueryData<PagedList<InventoryMovement>>(
      ['sku-movements', skuId, 1],
      (current) =>
        current
          ? {
              ...current,
              total: current.total + 1,
              items: [result.movement, ...current.items].slice(0, current.limit),
            }
          : current,
    );
    // Movement mới nằm ở trang đầu, nên quay về trang đầu để admin thấy ngay.
    setPage(1);
    setSuccessMessage(
      `Đã điều chỉnh ${formatDelta(result.movement.delta)}. Tồn kho hiện tại: ${result.stock}.`,
    );
    // Làm mới mọi truy vấn có thể đã cũ: sản phẩm, lịch sử, danh sách sắp hết hàng và danh sách sản phẩm.
    void queryClient.invalidateQueries({ queryKey: ['admin-product', productId] });
    void queryClient.invalidateQueries({ queryKey: ['sku-movements', skuId] });
    void queryClient.invalidateQueries({ queryKey: ['admin-low-stock'] });
    void queryClient.invalidateQueries({ queryKey: ['admin-products'] });
  }

  if (productQuery.isPending) {
    return (
      <div className="page">
        <p role="status" className="empty-state">
          Đang tải tồn kho...
        </p>
      </div>
    );
  }
  if (productQuery.isError) {
    return (
      <div className="page">
        <ErrorAlert
          message={toCatalogErrorMessage(productQuery.error, 'Không tải được sản phẩm.')}
        />
        {/* `.page` kéo dãn phần tử con, nên nhóm hành động giữ nút theo bề rộng nội dung. */}
        <div className="row-actions">
          <Link className="btn" to="/admin/inventory">
            Về danh sách tồn kho
          </Link>
        </div>
      </div>
    );
  }
  if (!product || !sku) {
    return (
      <div className="page">
        <ErrorAlert message="Không tìm thấy biến thể trong sản phẩm này." />
        {/* Nhánh thiếu SKU dùng cùng bề rộng nút với nhánh lỗi tải sản phẩm. */}
        <div className="row-actions">
          <Link className="btn" to="/admin/inventory">
            Về danh sách tồn kho
          </Link>
        </div>
      </div>
    );
  }

  const stock = sku.stock ?? 0;
  const badge = stockBadge(stock);
  return (
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Tồn kho {sku.code}</h1>
          <p>{product.name}</p>
        </div>
        {/* Chỉ quyền inventory:write được điều chỉnh. Người chỉ có products:read vẫn xem số tồn và lịch sử. */}
        {canAdjust && (
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => {
              // Thông báo của lần trước không còn đúng khi bắt đầu một lần điều chỉnh mới.
              setSuccessMessage(null);
              setIsDialogOpen(true);
            }}
          >
            Điều chỉnh tồn kho
          </button>
        )}
      </div>
      <nav className="inventory-links" aria-label="Điều hướng tồn kho">
        <Link to="/admin/inventory">Danh sách tồn kho</Link>
        <Link to={`/admin/products/${product.id}`}>Mở sản phẩm</Link>
      </nav>
      {successMessage && (
        <p role="status" className="alert alert-ok">
          {successMessage}
        </p>
      )}
      <section className="card" aria-label="Thông tin SKU">
        <dl className="sku-summary">
          <div>
            <dt>Mã SKU</dt>
            <dd className="wrap-anywhere">{sku.code}</dd>
          </div>
          <div>
            <dt>Tùy chọn</dt>
            <dd>{describeOptionValues(sku.optionValues)}</dd>
          </div>
          <div>
            <dt>Tồn kho hiện tại</dt>
            <dd className="sku-stock">
              <span data-testid="sku-stock">{stock}</span>
              <span className={badge.className}>{badge.label}</span>
            </dd>
          </div>
          {!sku.enabled && (
            <div>
              <dt>Trạng thái</dt>
              <dd>
                <span className="badge">Đang tắt</span>
              </dd>
            </div>
          )}
        </dl>
      </section>
      <section className="card card-flush">
        <div className="product-list-head">
          <h2>Lịch sử tồn kho</h2>
          {movements && <span className="muted small">{movements.total} lần thay đổi</span>}
        </div>
        {movementsQuery.isPending && (
          <p role="status" className="empty-state">
            Đang tải lịch sử...
          </p>
        )}
        {movementsQuery.isError && (
          <ErrorAlert
            message={toCatalogErrorMessage(movementsQuery.error, 'Không tải được lịch sử tồn kho.')}
            className="product-list-alert"
          />
        )}
        {movements && movements.items.length === 0 && (
          <p className="empty-state">SKU này chưa có thay đổi tồn kho.</p>
        )}
        {movements && movements.items.length > 0 && (
          <div className="table-wrap">
            <table className="tbl movement-table" aria-label="Lịch sử tồn kho">
              <thead>
                <tr>
                  <th scope="col">Thời điểm</th>
                  <th scope="col">Loại</th>
                  <th scope="col" className="num">
                    Thay đổi
                  </th>
                  <th scope="col">Lý do</th>
                  <th scope="col">Đơn hoặc người làm</th>
                </tr>
              </thead>
              <tbody>
                {movements.items.map((movement) => (
                  <tr key={movement.id}>
                    <td>{formatMovementTime(movement.createdAt)}</td>
                    <td>{movementTypeLabel(movement.type)}</td>
                    <td className="num">
                      <span className={movement.delta > 0 ? 'delta-up' : 'delta-down'}>
                        {formatDelta(movement.delta)}
                      </span>
                    </td>
                    <td className="movement-reason wrap-anywhere">{movement.reason}</td>
                    <td>
                      <MovementSource movement={movement} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {movements && pageCount > 1 && (
          <nav className="pager" aria-label="Phân trang lịch sử tồn kho">
            <button
              type="button"
              className="btn btn-sm"
              disabled={page === 1}
              onClick={() => setPage(page - 1)}
            >
              Trang trước
            </button>
            <span>
              Trang {movements.page} / {pageCount}
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
      {canAdjust && isDialogOpen && (
        <AdjustStockDialog
          skuId={sku.id!}
          skuLabel={sku.code ?? ''}
          currentStock={stock}
          onClose={() => setIsDialogOpen(false)}
          onAdjusted={handleAdjusted}
        />
      )}
    </div>
  );
}
