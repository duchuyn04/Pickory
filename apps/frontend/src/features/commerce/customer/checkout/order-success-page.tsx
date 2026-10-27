import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useLocation, useParams } from 'react-router-dom';
import { ErrorAlert } from '../../../../shared/error-alert';
import { Icon } from '../../../../shared/icons';
import { ResultIcon } from '../../../../shared/result-panel';
import { useActor } from '../../../identity/hooks/use-actor';
import { formatPaymentMethod } from '../../order-labels';
import { PurchaseProgress } from '../purchase-progress';
import { buildNextSteps, type NextStepState } from '../orders/order-next-steps';
import { OrderItemsList, OrderPaymentRows, formatShippingAddress } from '../orders/order-sections';
import { buildOrderSuccessLink } from '../orders/order-success-link';
import { fetchOrderDetail, type OrderDetail } from '../orders/orders-api';
import './checkout-page.css';

interface OrderSuccessState {
  accessToken?: string | null;
  paymentMethod?: string | null;
  paymentConfirmed?: boolean;
}

const COPIED_MESSAGE = 'Đã sao chép';
const COPY_FAILED_MESSAGE = 'Không sao chép được';

// Chữ đọc thêm cho trình đọc màn hình, vì dấu tích và số thứ tự chỉ là hình.
const STEP_STATE_LABELS: Record<NextStepState, string> = {
  done: 'Đã xong',
  current: 'Đang diễn ra',
  upcoming: 'Sắp tới',
};

// Khung giữ chỗ cùng bố cục với nội dung thật, nên trang không nhảy khi dữ liệu về.
function OrderSuccessSkeleton() {
  return (
    <div className="order-detail-layout" aria-busy="true">
      <p role="status" className="sr">
        Đang tải chi tiết đơn hàng...
      </p>
      <section className="card order-card order-success-skeleton" aria-hidden="true">
        <span className="skeleton-line skeleton-line-title" />
        <span className="skeleton-line" />
        <span className="skeleton-line" />
        <span className="skeleton-line skeleton-line-short" />
      </section>
      <aside className="card order-card order-success-skeleton" aria-hidden="true">
        <span className="skeleton-line skeleton-line-title" />
        <span className="skeleton-line" />
        <span className="skeleton-line skeleton-line-short" />
      </aside>
    </div>
  );
}

// Nội dung đơn đã tải: sản phẩm, các bước tiếp theo bên trái, tiền và nơi nhận bên phải.
function OrderSuccessDetails({ order }: { order: OrderDetail }) {
  const steps = buildNextSteps(order);
  return (
    <div className="order-detail-layout">
      <div>
        <section className="card order-card">
          <h2>Sản phẩm đã đặt</h2>
          <OrderItemsList items={order.items} />
        </section>

        {steps.length > 0 && (
          <section className="card order-card">
            <h2>Các bước tiếp theo</h2>
            <ol className="order-next-steps">
              {steps.map((step, index) => (
                <li key={step.key} className="order-next-step" data-state={step.state}>
                  <span className="order-next-step-marker" aria-hidden="true">
                    {step.state === 'done' ? <Icon name="check" /> : index + 1}
                  </span>
                  <div>
                    <p className="order-next-step-title">
                      {step.title}
                      <span className="sr"> ({STEP_STATE_LABELS[step.state]})</span>
                    </p>
                    <p className="order-next-step-description">{step.description}</p>
                  </div>
                </li>
              ))}
            </ol>
          </section>
        )}
      </div>

      <aside>
        <section className="card order-card">
          <h2>Thanh toán</h2>
          <OrderPaymentRows order={order} />
          <hr className="order-divider" />
          <p className="order-success-method">
            <small className="muted">Phương thức thanh toán</small>
            <strong>{formatPaymentMethod(order.paymentMethod)}</strong>
          </p>
        </section>

        <section className="card order-card">
          <h2>Giao tới</h2>
          <p>
            <strong>{order.contact.name}</strong>
            <br />
            {order.contact.phone}
          </p>
          <p>{formatShippingAddress(order.shippingAddress)}</p>
        </section>
      </aside>
    </div>
  );
}

// Trang thông báo đặt hàng thành công: tóm tắt đơn vừa đặt và liên kết xem đơn.
export function OrderSuccessPage() {
  const { code } = useParams();
  const location = useLocation();
  const state = (location.state ?? null) as OrderSuccessState | null;
  const accessToken = state?.accessToken ?? null;

  const { data: actor, isLoading: isActorLoading } = useActor();
  const isAuthenticated = Boolean(actor && actor.kind === 'customer');

  // Chỉ gọi API khi khách có quyền xem đơn. Khách vãng lai không có token luôn nhận 404 nên không gọi.
  const canLoadOrder = Boolean(code) && (Boolean(accessToken) || isAuthenticated);
  // Cùng khóa với trang chi tiết đơn, nên bấm "Xem đơn hàng" mở ngay từ bộ nhớ đệm.
  const {
    data: order,
    isLoading: isOrderLoading,
    error: orderError,
  } = useQuery({
    queryKey: ['order', code, accessToken],
    queryFn: () => fetchOrderDetail(code!, accessToken),
    enabled: canLoadOrder,
  });

  const [copyMessage, setCopyMessage] = useState('');

  async function handleCopyCode() {
    try {
      await navigator.clipboard.writeText(code ?? '');
      setCopyMessage(COPIED_MESSAGE);
    } catch {
      // Trình duyệt chặn clipboard (trang không bảo mật, chưa cấp quyền) thì báo để khách tự chép mã.
      setCopyMessage(COPY_FAILED_MESSAGE);
    }
  }

  const linkResult = buildOrderSuccessLink(code ?? '', accessToken, isAuthenticated);
  // Chờ xác định tài khoản để không hiện nhầm thông báo dành cho khách vãng lai.
  const isLinkReady = Boolean(accessToken) || !isActorLoading;
  // Đơn đã thanh toán thì bước "Thanh toán chuyển khoản" đã nói câu này, nên khối đầu không lặp lại.
  const showPaymentConfirmedAlert =
    state?.paymentConfirmed === true && order?.paymentStatus !== 'paid';

  return (
    <main className="catalog-page checkout-page order-success-page">
      <PurchaseProgress stage="success" />
      <section className="card order-success-hero">
        <ResultIcon tone="success" />
        <div className="order-success-hero-body">
          <h1>Đặt hàng thành công</h1>
          <p className="order-success-code-line">
            Mã đơn hàng của bạn:
            {/* Mã và nút chép đi cùng nhau, để màn hẹp không đẩy nút xuống dòng riêng. */}
            <span className="order-success-code-copy">
              <strong className="order-success-code">{code}</strong>
              <button
                type="button"
                className="icon-btn"
                aria-label="Sao chép mã đơn hàng"
                onClick={handleCopyCode}
              >
                <Icon name="copy" />
              </button>
            </span>
            <span role="status" className="order-success-copied">
              {copyMessage}
            </span>
          </p>

          {order && (
            <p className="order-success-email muted">
              {`Pickory đã gửi email xác nhận kèm link xem đơn tới ${order.contact.email}.`}
            </p>
          )}
          {orderError && (
            <p className="order-success-email muted">
              Pickory đã gửi email xác nhận kèm link xem đơn tới email liên hệ của đơn.
            </p>
          )}

          {showPaymentConfirmedAlert && (
            <p className="alert alert-ok">Shop đã nhận tiền chuyển khoản.</p>
          )}

          {isLinkReady && !linkResult.href && (
            <p className="muted">
              {linkResult.message} Không thấy thư? Tra cứu lại bằng mã đơn và email đặt hàng.
            </p>
          )}

          <div className="order-success-actions">
            {isLinkReady &&
              (linkResult.href ? (
                <Link to={linkResult.href} className="btn btn-primary">
                  Xem đơn hàng
                </Link>
              ) : (
                <Link to="/order-lookup" className="btn btn-outline">
                  Tra cứu đơn hàng
                </Link>
              ))}
            <Link to="/" className="btn btn-outline">
              Tiếp tục mua sắm
            </Link>
          </div>
        </div>
      </section>

      {orderError && <ErrorAlert message={orderError.message} />}
      {canLoadOrder && isOrderLoading && <OrderSuccessSkeleton />}
      {order && <OrderSuccessDetails order={order} />}
    </main>
  );
}
