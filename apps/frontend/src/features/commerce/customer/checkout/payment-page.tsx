import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { ApiError } from '../../../../shared/api';
import { ErrorAlert } from '../../../../shared/error-alert';
import { Icon } from '../../../../shared/icons';
import { formatProductPrice } from '../../../catalog/customer/product-display';
import { addCartItem } from '../cart-api';
import { PurchaseProgress } from '../purchase-progress';
import { CART_QUERY_KEY } from '../use-cart';
import { fetchOrderDetail } from '../orders/orders-api';
import { fetchOrderPayment } from './payment-api';
import { buildVietQrUrl, formatPaymentCountdown } from './payment-format';
import './payment-page.css';

export const PAYMENT_POLL_INTERVAL_MS = 3000;
const CLOCK_INTERVAL_MS = 1000;
const URGENT_WINDOW_MS = 5 * 60 * 1000;

interface TransferRowProps {
  label: string;
  value: string;
  emphasized?: boolean;
  onCopy: () => void;
}

function TransferRow({ label, value, emphasized = false, onCopy }: TransferRowProps) {
  return (
    <div className={`payment-transfer-row${emphasized ? ' payment-transfer-important' : ''}`}>
      <div className="payment-transfer-text">
        <dt>{label}</dt>
        <dd>{value}</dd>
      </div>
      <button
        type="button"
        className="btn btn-outline btn-sm"
        onClick={onCopy}
        aria-label={`Sao chép ${label.toLowerCase()}`}
      >
        <Icon name="copy" /> <span>Sao chép</span>
      </button>
    </div>
  );
}

export function PaymentPage() {
  const { code } = useParams();
  const [searchParams] = useSearchParams();
  const token = searchParams.get('t');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [now, setNow] = useState(() => Date.now());
  const [copyNotice, setCopyNotice] = useState('');
  const [reorderError, setReorderError] = useState('');
  const [isReordering, setIsReordering] = useState(false);

  // Gỡ đồng hồ khi rời trang để không cập nhật trạng thái của màn đã đóng.
  useEffect(() => {
    const clock = window.setInterval(() => setNow(Date.now()), CLOCK_INTERVAL_MS);
    return () => window.clearInterval(clock);
  }, []);

  // Khóa query gồm token để không chia sẻ dữ liệu đơn giữa các liên kết của khách.
  const paymentQuery = useQuery({
    queryKey: ['order-payment', code, token],
    queryFn: () => fetchOrderPayment(code!, token),
    enabled: Boolean(code),
    retry: false,
    refetchInterval: (query) => {
      const payment = query.state.data;
      // Đơn đã trả hoặc đã hủy không còn cần hỏi máy chủ. Đơn hết hạn tại client vẫn phải chờ job hủy.
      if (
        payment?.paymentStatus === 'paid' ||
        payment?.status === 'cancelled' ||
        payment?.paymentMethod === 'cod'
      )
        return false;
      return PAYMENT_POLL_INTERVAL_MS;
    },
  });

  const payment = paymentQuery.data;
  const paid = payment?.paymentStatus === 'paid';
  // Đơn bị hủy sớm không được gắn nguyên nhân hết hạn khi đồng hồ vẫn còn thời gian.
  const cancelled = payment?.status === 'cancelled';
  const deadlinePassed =
    payment?.paymentDeadline != null && now >= Date.parse(payment.paymentDeadline);
  const paymentStopped = cancelled || deadlinePassed;

  useEffect(() => {
    if (!payment || !code) return;
    // Ưu tiên xác nhận đã trả ngay cả khi đồng hồ client vừa về 0.
    if (paid) {
      navigate(`/orders/${encodeURIComponent(code)}/success`, {
        replace: true,
        state: { accessToken: token, paymentMethod: 'sepay', paymentConfirmed: true },
      });
    } else if (payment.paymentMethod === 'cod') {
      // Đơn COD không dùng màn chuyển khoản, nên quay về chi tiết đơn tương ứng.
      const query = token ? `?t=${encodeURIComponent(token)}` : '';
      navigate(`/orders/${encodeURIComponent(code)}${query}`, { replace: true });
    }
  }, [payment, paid, code, token, navigate]);

  async function copy(value: string, label: string) {
    try {
      // Clipboard có thể bị trình duyệt chặn, nên xác nhận sau khi thao tác hoàn thành.
      await navigator.clipboard.writeText(value);
      setCopyNotice(`Đã sao chép ${label.toLowerCase()}.`);
    } catch {
      // Hiển thị cách thay thế khi quyền clipboard bị từ chối.
      setCopyNotice(`Không thể sao chép ${label.toLowerCase()}. Hãy chọn và sao chép thủ công.`);
    }
  }

  async function reorder() {
    if (!code || isReordering) return;
    setIsReordering(true);
    setReorderError('');
    try {
      // Lấy SKU đã lưu ở đơn cũ thay vì đoán biến thể từ tên sản phẩm.
      const detail = await fetchOrderDetail(code, token);
      let failedCount = 0;
      for (const item of detail.items) {
        try {
          // Tiếp tục thêm các dòng khác nếu một SKU đã hết hàng hoặc ngừng bán.
          await addCartItem(item.skuId, item.quantity);
        } catch {
          failedCount += 1;
        }
      }
      // Giỏ được nạp mới sau khi chuyển trang để thấy chính xác các dòng thêm thành công.
      await queryClient.invalidateQueries({ queryKey: CART_QUERY_KEY });
      navigate('/cart', { state: { reorderFailedCount: failedCount } });
    } catch {
      // Nếu không đọc được đơn cũ thì không thể biết những SKU nào cần thêm.
      setReorderError('Chưa tải được sản phẩm của đơn. Vui lòng thử lại.');
    } finally {
      setIsReordering(false);
    }
  }

  if (paymentQuery.isPending)
    return (
      <main className="catalog-page payment-page">
        <p role="status">Đang tải thông tin thanh toán...</p>
      </main>
    );
  if (!payment) {
    const notFound = paymentQuery.error instanceof ApiError && paymentQuery.error.status === 404;
    return (
      <main className="catalog-page payment-page">
        <section className="card payment-empty">
          <h1>{notFound ? 'Không tìm thấy đơn hàng' : 'Chưa tải được thông tin thanh toán'}</h1>
          <p className="muted">
            {notFound
              ? 'Liên kết xem đơn có thể đã hết hiệu lực.'
              : 'Vui lòng kiểm tra kết nối và thử lại.'}
          </p>
          {notFound ? (
            <Link className="btn btn-primary" to="/">
              Về trang chủ
            </Link>
          ) : (
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => paymentQuery.refetch()}
            >
              Thử lại
            </button>
          )}
        </section>
      </main>
    );
  }

  if (paid || payment.paymentMethod === 'cod') return null;
  const remainingMs = payment.paymentDeadline ? Date.parse(payment.paymentDeadline) - now : 0;
  const canTransfer =
    !paymentStopped &&
    Boolean(payment.bankAccountNumber && payment.bankCode && payment.paymentDeadline);
  // Giữ nguyên nhân hủy đúng với hạn server khi khách mở nhiều tab cùng một đơn.
  let paymentTitle = 'Thanh toán chuyển khoản';
  if (deadlinePassed) paymentTitle = 'Thời gian thanh toán đã hết';
  else if (cancelled) paymentTitle = 'Đơn hàng đã hủy';

  return (
    <main className="catalog-page payment-page">
      <PurchaseProgress stage="payment" />
      <div className="payment-heading">
        <div>
          <p className="payment-eyebrow">Đơn hàng {payment.code}</p>
          <h1 id="payment-page-title">{paymentTitle}</h1>
          {!paymentStopped && (
            <p className="muted">Quét mã bằng ứng dụng ngân hàng để hoàn tất đơn hàng.</p>
          )}
        </div>
        <Link
          to={`/orders/${encodeURIComponent(payment.code)}${token ? `?t=${encodeURIComponent(token)}` : ''}`}
          className="payment-detail-link"
        >
          Xem chi tiết đơn
        </Link>
      </div>
      {paymentQuery.isRefetchError && (
        <p className="payment-connection" role="status">
          Đang kết nối lại
        </p>
      )}
      <div className="payment-layout">
        <section
          className="card payment-qr-card"
          aria-labelledby={paymentStopped ? 'payment-page-title' : 'payment-qr-title'}
        >
          {paymentStopped ? (
            <div className="payment-state">
              <Icon
                name={deadlinePassed ? 'clock' : 'alertCircle'}
                className="payment-state-icon"
              />
              <p>Đã chuyển khoản? Shop sẽ liên hệ để xác nhận hoặc hoàn tiền.</p>
              <button
                type="button"
                className="btn btn-primary"
                onClick={reorder}
                disabled={isReordering}
              >
                {isReordering ? 'Đang thêm vào giỏ...' : 'Đặt lại đơn'}
              </button>
              {reorderError && <ErrorAlert message={reorderError} />}
            </div>
          ) : !canTransfer ? (
            <div className="payment-state">
              <Icon name="alertCircle" className="payment-state-icon" />
              <h2 id="payment-qr-title">Chưa thể chuyển khoản</h2>
              <p>
                Shop chưa sẵn sàng nhận chuyển khoản. Vui lòng liên hệ shop trước khi thanh toán.
              </p>
            </div>
          ) : (
            <>
              <div className="payment-qr-head">
                <div>
                  <p className="payment-eyebrow">Bước cuối cùng</p>
                  <h2 id="payment-qr-title">Quét mã để thanh toán</h2>
                </div>
                <span className="payment-bank-label">VietQR</span>
              </div>
              <div className="payment-qr-image">
                <img
                  src={buildVietQrUrl(
                    payment.bankAccountNumber!,
                    payment.bankCode!,
                    payment.total,
                    payment.code,
                  )}
                  alt={`Mã QR chuyển khoản cho đơn ${payment.code}`}
                />
              </div>
              <div
                className={`payment-timer${remainingMs < URGENT_WINDOW_MS ? ' payment-timer-urgent' : ''}`}
              >
                <Icon name="clock" />
                <span>Còn lại</span>
                <strong role="timer">
                  {formatPaymentCountdown(payment.paymentDeadline!, now)}
                </strong>
              </div>
              <p className="payment-guide">
                Quét QR bằng ứng dụng ngân hàng. Giữ nguyên nội dung chuyển khoản để hệ thống tự xác
                nhận.
              </p>
            </>
          )}
        </section>
        <div className="payment-sidebar">
          {canTransfer && (
            <section
              className="card payment-transfer-card"
              aria-labelledby="payment-transfer-title"
            >
              <h2 id="payment-transfer-title">Thông tin chuyển khoản</h2>
              <dl className="payment-transfer-list">
                <TransferRow
                  label="Ngân hàng"
                  value={payment.bankCode!}
                  onCopy={() => copy(payment.bankCode!, 'Ngân hàng')}
                />
                <TransferRow
                  label="Số tài khoản"
                  value={payment.bankAccountNumber!}
                  onCopy={() => copy(payment.bankAccountNumber!, 'Số tài khoản')}
                />
                <TransferRow
                  label="Số tiền"
                  value={formatProductPrice(payment.total)}
                  emphasized
                  onCopy={() => copy(String(payment.total), 'Số tiền')}
                />
                <TransferRow
                  label="Nội dung chuyển khoản"
                  value={payment.code}
                  emphasized
                  onCopy={() => copy(payment.code, 'Nội dung chuyển khoản')}
                />
              </dl>
              <p className="payment-copy-notice" role="status" aria-live="polite">
                {copyNotice}
              </p>
            </section>
          )}
          <section className="card payment-summary-card" aria-labelledby="payment-summary-title">
            <h2 id="payment-summary-title">Đơn hàng của bạn</h2>
            <p className="payment-summary-line">
              <span>Mã đơn</span>
              <strong>{payment.code}</strong>
            </p>
            <p className="payment-summary-line payment-summary-total">
              <span>Tổng thanh toán</span>
              <strong>{formatProductPrice(payment.total)}</strong>
            </p>
            {!paymentStopped && (
              <p className="muted small">
                Shop tự cập nhật trang này khi nhận được tiền. Không cần bấm xác nhận.
              </p>
            )}
          </section>
        </div>
      </div>
    </main>
  );
}
