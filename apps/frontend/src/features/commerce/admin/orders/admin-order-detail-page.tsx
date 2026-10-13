import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '../../../../shared/api';
import { ErrorAlert } from '../../../../shared/error-alert';
import { ForbiddenNotice } from '../../../../shared/forbidden-notice';
import { formatMovementTime } from '../../../catalog/admin/inventory-logic';
import { formatProductPrice } from '../../../catalog/customer/product-display';
import { useCan } from '../../../identity/hooks/use-actor';
import {
  formatOrderFlag,
  formatOrderStatus,
  formatPaymentMethod,
  formatPaymentStatus,
  formatShipmentStatus,
} from '../../order-labels';
import {
  fetchAdminOrderDetail,
  type AdminOrderDetail,
  type AdminOrderHistoryEntry,
} from './admin-orders-api';
import {
  orderFlagBadgeClass,
  orderStatusBadgeClass,
  paymentStatusBadgeClass,
} from './order-badges';
import './admin-orders.css';

// Người làm của một lần đổi trạng thái. Chỉ có mã người dùng nên rút gọn 8 ký tự, mã đầy đủ nằm ở `title`.
function HistoryActor({ actorId }: { actorId: string | null }) {
  // `null` nghĩa là hệ thống hoặc khách vãng lai chuyển, không có nhân viên nào đứng tên.
  if (actorId === null) return <span>Hệ thống</span>;
  return (
    <span className="mono" title={actorId}>
      Nhân viên {actorId.slice(0, 8)}
    </span>
  );
}

// Sắp lịch sử theo thời điểm tăng dần để đọc từ lúc đặt đơn tới hiện tại.
function sortHistory(entries: AdminOrderHistoryEntry[]): AdminOrderHistoryEntry[] {
  return [...entries].sort(
    (left, right) => new Date(left.createdAt).getTime() - new Date(right.createdAt).getTime(),
  );
}

function orderAddressLine(address: AdminOrderDetail['shippingAddress']): string {
  return [address.line, address.wardName, address.provinceName]
    .filter((part): part is string => Boolean(part))
    .join(', ');
}

// Chi tiết một đơn cho nhân viên. Phần 1 chỉ đọc, các nút thao tác thêm ở phần 2.
export function AdminOrderDetailPage() {
  const { code = '' } = useParams();
  const canReadOrders = useCan('orders:read');

  const {
    data: order,
    isLoading,
    error,
  } = useQuery<AdminOrderDetail>({
    queryKey: ['admin-order', code],
    queryFn: () => fetchAdminOrderDetail(code),
    // Không gọi API khi không có quyền hoặc chưa có mã đơn.
    enabled: canReadOrders && code !== '',
  });

  // Hook ở trên phải chạy hết trước lệnh return sớm này. Backend vẫn chặn mọi request thật sự.
  if (!canReadOrders) {
    return <ForbiddenNotice permission="orders:read" subject="chi tiết đơn hàng" />;
  }

  if (isLoading) {
    return (
      <div className="page">
        <p role="status" className="empty-state">
          Đang nạp chi tiết đơn hàng...
        </p>
      </div>
    );
  }

  // Mã đơn không có trong hệ thống. Cho nhân viên đường về danh sách thay vì để trang trống.
  if (error instanceof ApiError && error.status === 404) {
    return (
      <div className="page">
        <ErrorAlert message={error.message} />
        <div className="row-actions">
          <Link className="btn" to="/admin/orders">
            Về danh sách đơn hàng
          </Link>
        </div>
      </div>
    );
  }

  if (error || !order) {
    return (
      <div className="page">
        <ErrorAlert message={error instanceof Error ? error.message : 'Tải đơn hàng thất bại.'} />
        <div className="row-actions">
          <Link className="btn" to="/admin/orders">
            Về danh sách đơn hàng
          </Link>
        </div>
      </div>
    );
  }

  const history = sortHistory(order.statusHistory);

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Đơn hàng {order.code}</h1>
          <p>Đặt lúc {formatMovementTime(order.createdAt)}</p>
        </div>
        {/* Chỗ đặt các nút thao tác theo quyền (xác nhận, tạo vận đơn, hủy, hóa đơn, COD, hoàn tiền). Phần 2 của COM-UI5 thêm vào đây. */}
        <div className="order-actions" />
      </div>

      <section className="card" aria-label="Tổng quan đơn">
        <dl className="order-overview">
          <div>
            <dt>Trạng thái đơn</dt>
            <dd>
              <span className={orderStatusBadgeClass(order.status)}>
                {formatOrderStatus(order.status)}
              </span>
            </dd>
          </div>
          <div>
            <dt>Thanh toán</dt>
            <dd>
              <span className={paymentStatusBadgeClass(order.paymentStatus)}>
                {formatPaymentStatus(order.paymentStatus)}
              </span>
            </dd>
          </div>
          <div>
            <dt>Phương thức</dt>
            <dd>{formatPaymentMethod(order.paymentMethod)}</dd>
          </div>
          {order.paymentDeadline && (
            <div>
              <dt>Hạn thanh toán</dt>
              <dd>{formatMovementTime(order.paymentDeadline)}</dd>
            </div>
          )}
          {order.flags.length > 0 && (
            <div>
              <dt>Cờ đơn</dt>
              <dd>
                <div className="row-actions">
                  {order.flags.map((flag) => (
                    <span key={flag} className={orderFlagBadgeClass(flag)}>
                      {formatOrderFlag(flag)}
                    </span>
                  ))}
                </div>
              </dd>
            </div>
          )}
        </dl>
      </section>

      <div className="admin-order-grid">
        <div className="admin-order-main">
          <section className="card card-flush">
            <h2 className="order-section-title">Sản phẩm</h2>
            <div className="table-wrap">
              <table className="tbl order-items-table">
                <thead>
                  <tr>
                    <th scope="col">Sản phẩm</th>
                    <th scope="col" className="num">
                      Số lượng
                    </th>
                    <th scope="col" className="num">
                      Đơn giá
                    </th>
                    <th scope="col" className="num">
                      Thành tiền
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {order.items.map((item) => (
                    <tr key={item.skuId}>
                      <th scope="row">
                        {item.name}
                        {item.variantName && (
                          <span className="cell-sub wrap-anywhere">{item.variantName}</span>
                        )}
                      </th>
                      <td className="num">{item.quantity}</td>
                      <td className="num">{formatProductPrice(item.unitPrice)}</td>
                      <td className="num">{formatProductPrice(item.lineTotal)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section className="card card-flush">
            <h2 className="order-section-title">Lịch sử trạng thái</h2>
            {history.length === 0 ? (
              <p className="empty-state">Đơn chưa có thay đổi trạng thái nào.</p>
            ) : (
              <div className="table-wrap">
                <table className="tbl order-history-table">
                  <thead>
                    <tr>
                      <th scope="col">Thời điểm</th>
                      <th scope="col">Từ</th>
                      <th scope="col">Sang</th>
                      <th scope="col">Lý do</th>
                      <th scope="col">Người làm</th>
                    </tr>
                  </thead>
                  <tbody>
                    {history.map((entry, index) => (
                      <tr key={`${entry.createdAt}-${index}`}>
                        <td className="order-time">{formatMovementTime(entry.createdAt)}</td>
                        <td>{formatOrderStatus(entry.fromStatus)}</td>
                        <td>{formatOrderStatus(entry.toStatus)}</td>
                        <td className="wrap-anywhere">{entry.reason ?? ''}</td>
                        <td>
                          <HistoryActor actorId={entry.actorId} />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </div>

        <aside className="admin-order-side">
          <section className="card">
            <h2 className="order-section-title">Người nhận</h2>
            <p className="order-block">
              <strong>{order.contact.name || 'Chưa có tên'}</strong>
              <span className="cell-sub">{order.contact.phone || 'Chưa có số điện thoại'}</span>
              <span className="cell-sub wrap-anywhere">
                {order.contact.email || 'Chưa có email'}
              </span>
            </p>
            <h2 className="order-section-title">Địa chỉ giao hàng</h2>
            <p className="order-block wrap-anywhere">
              {orderAddressLine(order.shippingAddress) || 'Chưa có địa chỉ'}
            </p>
          </section>

          {order.invoice && (
            <section className="card">
              <h2 className="order-section-title">Hóa đơn VAT</h2>
              <p className="order-block wrap-anywhere">
                <strong>{order.invoice.companyName}</strong>
                <span className="cell-sub">Mã số thuế: {order.invoice.taxCode}</span>
                <span className="cell-sub">{order.invoice.address}</span>
                <span className="cell-sub">{order.invoice.email}</span>
              </p>
            </section>
          )}

          <section className="card">
            <h2 className="order-section-title">Thanh toán</h2>
            <dl className="order-totals">
              <div>
                <dt>Tạm tính</dt>
                <dd>{formatProductPrice(order.subtotal)}</dd>
              </div>
              <div>
                <dt>Phí vận chuyển</dt>
                <dd>{formatProductPrice(order.shippingFee)}</dd>
              </div>
              {order.shippingDiscount > 0 && (
                <div>
                  <dt>Giảm phí vận chuyển</dt>
                  <dd>-{formatProductPrice(order.shippingDiscount)}</dd>
                </div>
              )}
              <div className="order-totals-total">
                <dt>Tổng cộng</dt>
                <dd>{formatProductPrice(order.total)}</dd>
              </div>
            </dl>
            {order.refundTransactionRef && (
              <p className="order-block">
                <span className="cell-sub">Mã giao dịch hoàn tiền</span>
                <strong className="mono wrap-anywhere">{order.refundTransactionRef}</strong>
              </p>
            )}
          </section>

          {order.shipment && (
            <section className="card">
              <h2 className="order-section-title">Vận đơn</h2>
              <dl className="order-totals">
                <div>
                  <dt>Mã vận đơn</dt>
                  <dd className="mono wrap-anywhere">{order.shipment.trackingCode}</dd>
                </div>
                <div>
                  <dt>Trạng thái</dt>
                  <dd>{formatShipmentStatus(order.shipment.status)}</dd>
                </div>
                <div>
                  <dt>Phí</dt>
                  <dd>{formatProductPrice(order.shipment.fee)}</dd>
                </div>
              </dl>
            </section>
          )}
        </aside>
      </div>
    </div>
  );
}
