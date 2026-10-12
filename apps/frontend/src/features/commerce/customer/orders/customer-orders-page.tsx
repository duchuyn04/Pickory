import { useQuery } from '@tanstack/react-query';
import { Link, Navigate, useSearchParams } from 'react-router-dom';
import { useActor } from '../../../identity/hooks/use-actor';
import { ErrorAlert } from '../../../../shared/error-alert';
import { formatProductPrice } from '../../../catalog/customer/product-display';
import { ProductThumbnail } from '../product-thumbnail';
import { formatOrderStatus } from './order-labels';
import { formatDateTime } from './order-format';
import { fetchCustomerOrders } from './orders-api';
import './orders.css';

// Trang hiển thị danh sách lịch sử đơn hàng của tài khoản đã đăng nhập.
export function CustomerOrdersPage() {
  const { data: actor, isLoading: isActorLoading } = useActor();
  const [searchParams, setSearchParams] = useSearchParams();
  const page = Math.max(1, Number(searchParams.get('page')) || 1);

  const { data, isLoading, error } = useQuery({
    queryKey: ['customer-orders', page],
    queryFn: () => fetchCustomerOrders(page),
    enabled: Boolean(actor && actor.kind === 'customer'),
  });

  if (isActorLoading) {
    return (
      <main className="catalog-page customer-orders-page">
        <p role="status" className="muted">
          Đang tải thông tin tài khoản...
        </p>
      </main>
    );
  }

  // Khách chưa đăng nhập bị chuyển hướng về trang đăng nhập theo thiết kế.
  if (!actor || actor.kind !== 'customer') {
    return <Navigate to="/login" replace />;
  }

  function handlePageChange(nextPage: number) {
    // Cập nhật số trang vào URL để người dùng có thể chia sẻ hoặc quay lại trang trước.
    setSearchParams({ page: String(nextPage) });
  }

  const pageCount = data ? Math.ceil(data.total / data.limit) : 0;

  return (
    <main className="catalog-page customer-orders-page">
      <div className="customer-orders-header">
        <h1>Đơn hàng của tôi</h1>
      </div>

      {isLoading && (
        <p role="status" className="muted">
          Đang tải danh sách đơn hàng...
        </p>
      )}

      {error && (
        <ErrorAlert message={error instanceof Error ? error.message : 'Lỗi tải đơn hàng.'} />
      )}

      {data && data.items.length === 0 && (
        <div className="card order-empty-state stack">
          {/* Trang rỗng ngoài giới hạn không có nghĩa tài khoản chưa có đơn. */}
          {data.total > 0 ? (
            <>
              <p className="muted">Trang này không có đơn hàng.</p>
              <Link to="/me/orders?page=1" className="btn btn-outline">
                Về trang 1
              </Link>
            </>
          ) : (
            <>
              <p className="muted">Bạn chưa có đơn hàng nào.</p>
              <Link to="/" className="btn btn-outline">
                Tiếp tục mua sắm
              </Link>
            </>
          )}
        </div>
      )}

      {data && data.items.length > 0 && (
        <>
          <div className="customer-orders-list">
            {data.items.map((order) => (
              <div key={order.code} className="card customer-order-card">
                <ProductThumbnail
                  imageUrl={order.firstItemImageUrl}
                  size={64}
                  className="order-line-image"
                />
                <div className="customer-order-info">
                  <Link
                    to={`/orders/${encodeURIComponent(order.code)}`}
                    className="customer-order-code"
                  >
                    Đơn hàng #{order.code}
                  </Link>
                  <div className="customer-order-meta">
                    <span>{formatDateTime(order.createdAt)}</span>
                    <span>
                      {order.firstItemName} ({order.itemCount} sản phẩm)
                    </span>
                    <span className="order-badge">{formatOrderStatus(order.status)}</span>
                  </div>
                </div>
                <div className="customer-order-total">
                  <span className="order-item-line-total">{formatProductPrice(order.total)}</span>
                </div>
              </div>
            ))}
          </div>

          {pageCount > 1 && (
            <nav className="order-pager" aria-label="Phân trang đơn hàng">
              <button
                type="button"
                className="btn btn-sm btn-outline"
                disabled={page <= 1}
                onClick={() => handlePageChange(page - 1)}
              >
                Trang trước
              </button>
              <span className="muted small">
                Trang {data.page} / {pageCount} ({data.total} đơn hàng)
              </span>
              <button
                type="button"
                className="btn btn-sm btn-outline"
                disabled={page >= pageCount}
                onClick={() => handlePageChange(page + 1)}
              >
                Trang sau
              </button>
            </nav>
          )}
        </>
      )}
    </main>
  );
}
