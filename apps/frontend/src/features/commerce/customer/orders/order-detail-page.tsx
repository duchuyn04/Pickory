import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { ApiError } from '../../../../shared/api';
import { ErrorAlert } from '../../../../shared/error-alert';
import { formatProductPrice } from '../../../catalog/customer/product-display';
import { CancelOrderDialog } from './cancel-order-dialog';
import { ProductThumbnail } from '../product-thumbnail';
import { formatOrderStatus, formatPaymentMethod, formatPaymentStatus } from './order-labels';
import { formatDateTime } from './order-format';
import { cancelOrder, fetchOrderDetail } from './orders-api';
import './orders.css';

// Trang hiển thị thông tin chi tiết và tiến trình trạng thái của đơn hàng.
export function OrderDetailPage() {
  const { code } = useParams();
  const [searchParams] = useSearchParams();
  const token = searchParams.get('t');

  const queryClient = useQueryClient();
  const [isCancelDialogOpen, setIsCancelDialogOpen] = useState(false);
  const [cancelNotice, setCancelNotice] = useState<string | null>(null);
  const cancelNoticeRef = useRef<HTMLParagraphElement>(null);
  // Nút mở biến mất sau khi hủy nên chuyển focus tới thông báo sau khi đóng hộp thoại.
  useEffect(() => {
    if (cancelNotice && !isCancelDialogOpen) cancelNoticeRef.current?.focus();
  }, [cancelNotice, isCancelDialogOpen]);

  // Khóa query bao gồm mã đơn và token để cô lập dữ liệu giữa các liên kết.
  const queryKey = ['order', code, token];

  const {
    data: order,
    isLoading,
    error,
  } = useQuery({
    queryKey,
    queryFn: () => fetchOrderDetail(code!, token),
    enabled: Boolean(code),
  });

  async function handleConfirmCancel() {
    if (!order) return;
    try {
      const updated = await cancelOrder(order.code, token);
      // Cập nhật bộ nhớ đệm ngay lập tức để giao diện hiển thị trạng thái đã hủy.
      queryClient.setQueryData(queryKey, updated);
      setIsCancelDialogOpen(false);
      setCancelNotice('Đã hủy đơn hàng.');
    } catch (err) {
      // Khi gặp lỗi xung đột trạng thái thì tải lại dữ liệu mới nhất từ máy chủ.
      if (err instanceof ApiError && err.status === 409) {
        await queryClient.invalidateQueries({ queryKey });
      }
      throw err;
    }
  }

  if (isLoading) {
    return (
      <main className="catalog-page order-detail-page">
        <p role="status" className="muted">
          Đang tải thông tin đơn hàng...
        </p>
      </main>
    );
  }

  // Khi gặp lỗi 404 hiển thị thông báo chung và nút điều hướng về trang chủ.
  if (error instanceof ApiError && error.status === 404) {
    return (
      <main className="catalog-page order-detail-page">
        <div className="card order-empty-state stack">
          <h1>Không tìm thấy đơn hàng.</h1>
          <div>
            <Link to="/" className="btn btn-primary">
              Về trang chủ
            </Link>
          </div>
        </div>
      </main>
    );
  }

  if (error || !order) {
    return (
      <main className="catalog-page order-detail-page">
        <ErrorAlert message={error instanceof Error ? error.message : 'Lỗi tải đơn hàng.'} />
      </main>
    );
  }

  return (
    <main className="catalog-page order-detail-page">
      <div className="order-detail-header">
        <div>
          <h1>Đơn hàng #{order.code}</h1>
          <p className="muted small">Đặt ngày {formatDateTime(order.createdAt)}</p>
        </div>

        {/* Nút hủy đơn chỉ xuất hiện khi cờ canCancel của máy chủ cho phép */}
        {order.canCancel && (
          <button
            type="button"
            className="btn btn-outline"
            onClick={() => setIsCancelDialogOpen(true)}
          >
            Hủy đơn hàng
          </button>
        )}
      </div>

      {cancelNotice && (
        <p ref={cancelNoticeRef} tabIndex={-1} role="status" className="alert alert-ok">
          {cancelNotice}
        </p>
      )}

      <div className="order-detail-layout">
        <div>
          {/* Thông tin trạng thái và thanh toán */}
          <section className="card order-card">
            <div className="order-meta-grid">
              <div className="order-meta-item">
                <small>Trạng thái đơn hàng</small>
                <span className="order-badge">{formatOrderStatus(order.status)}</span>
              </div>
              <div className="order-meta-item">
                <small>Trạng thái thanh toán</small>
                <span className="order-badge">{formatPaymentStatus(order.paymentStatus)}</span>
              </div>
              <div className="order-meta-item">
                <small>Phương thức thanh toán</small>
                <strong>{formatPaymentMethod(order.paymentMethod)}</strong>
              </div>
              {order.paymentDeadline && (
                <div className="order-meta-item">
                  <small>Hạn thanh toán</small>
                  <strong>{formatDateTime(order.paymentDeadline)}</strong>
                </div>
              )}
              {order.trackingCode && (
                <div className="order-meta-item">
                  <small>Mã vận đơn</small>
                  <strong>{order.trackingCode}</strong>
                </div>
              )}
            </div>
          </section>

          {/* Danh sách các sản phẩm trong đơn hàng */}
          <section className="card order-card">
            <h2>Sản phẩm đã đặt</h2>
            <ul className="order-items-list">
              {order.items.map((item, index) => (
                <li key={`${item.name}-${index}`} className="order-item-row">
                  <ProductThumbnail
                    imageUrl={item.imageUrl}
                    size={64}
                    className="order-line-image"
                  />
                  <div className="order-item-info">
                    <p className="order-item-name">{item.name}</p>
                    {item.variantName && <p className="order-item-meta">{item.variantName}</p>}
                    <p className="order-item-meta">Số lượng: {item.quantity}</p>
                  </div>
                  <div className="order-item-price">
                    <div className="order-item-unit-price">
                      {formatProductPrice(item.unitPrice)}
                    </div>
                    <div className="order-item-line-total">
                      {formatProductPrice(item.lineTotal)}
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          </section>

          {/* Lịch sử và tiến trình thay đổi trạng thái của đơn */}
          {order.statusHistory.length > 0 && (
            <section className="card order-card">
              <h2>Tiến trình đơn hàng</h2>
              <ol className="order-history-list">
                {order.statusHistory.map((history, index) => (
                  <li key={index} className="order-history-item">
                    <strong>{formatOrderStatus(history.toStatus)}</strong>
                    <span className="order-history-time">
                      ({formatDateTime(history.createdAt)})
                    </span>
                  </li>
                ))}
              </ol>
            </section>
          )}
        </div>

        {/* Cột thông tin phụ bên phải: địa chỉ, hóa đơn và tổng tiền */}
        <aside>
          <section className="card order-card">
            <h2>Người nhận hàng</h2>
            <p>
              <strong>{order.contact.name}</strong>
              <br />
              {order.contact.phone}
              <br />
              {order.contact.email}
            </p>
            <hr className="order-divider" />
            <h2>Địa chỉ giao hàng</h2>
            <p>
              {order.shippingAddress.line}
              {order.shippingAddress.wardName && `, ${order.shippingAddress.wardName}`}
              {order.shippingAddress.provinceName && `, ${order.shippingAddress.provinceName}`}
            </p>
          </section>

          {/* Hóa đơn VAT nếu có */}
          {order.invoice && (
            <section className="card order-card">
              <h2>Hóa đơn VAT</h2>
              <p>
                <strong>{order.invoice.companyName}</strong>
                <br />
                Mã số thuế: {order.invoice.taxCode}
                <br />
                {order.invoice.address}
                <br />
                {order.invoice.email}
              </p>
            </section>
          )}

          {/* Tóm tắt thanh toán */}
          <section className="card order-card">
            <h2>Thanh toán</h2>
            <div className="stack">
              <div className="order-summary-row">
                <span>Tạm tính</span>
                <strong>{formatProductPrice(order.subtotal)}</strong>
              </div>
              <div className="order-summary-row">
                <span>Phí vận chuyển</span>
                <strong>{formatProductPrice(order.shippingFee)}</strong>
              </div>
              {order.shippingDiscount > 0 && (
                <div className="order-summary-row order-summary-discount">
                  <span>Miễn phí vận chuyển</span>
                  <strong>-{formatProductPrice(order.shippingDiscount)}</strong>
                </div>
              )}
              <div className="order-summary-row order-summary-total">
                <span>Tổng cộng</span>
                <strong className="order-total-price">{formatProductPrice(order.total)}</strong>
              </div>
            </div>
          </section>
        </aside>
      </div>

      {isCancelDialogOpen && (
        <CancelOrderDialog
          code={order.code}
          onConfirm={handleConfirmCancel}
          onClose={() => setIsCancelDialogOpen(false)}
        />
      )}
    </main>
  );
}
