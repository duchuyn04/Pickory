import { Link, useLocation, useParams } from 'react-router-dom';
import { useActor } from '../../../identity/hooks/use-actor';
import { buildOrderSuccessLink } from '../orders/order-success-link';
import './checkout-page.css';

interface OrderSuccessState {
  accessToken?: string | null;
  paymentMethod?: string | null;
}

// Trang thông báo đặt hàng thành công và cung cấp liên kết xem đơn.
export function OrderSuccessPage() {
  const { code } = useParams();
  const location = useLocation();
  const state = (location.state ?? null) as OrderSuccessState | null;
  const accessToken = state?.accessToken ?? null;
  const paymentMethod = state?.paymentMethod ?? null;

  const { data: actor, isLoading: isActorLoading } = useActor();
  const isAuthenticated = Boolean(actor && actor.kind === 'customer');

  const linkResult = buildOrderSuccessLink(code ?? '', accessToken, isAuthenticated);

  return (
    <main className="catalog-page checkout-page">
      <div className="card order-success">
        <h1>Đặt hàng thành công</h1>
        <p>
          Mã đơn hàng của bạn: <strong className="order-success-code">{code}</strong>
        </p>

        {paymentMethod === 'sepay' && (
          <p className="alert alert-warn">Đơn hàng đang chờ thanh toán chuyển khoản ngân hàng.</p>
        )}

        {/* Chờ xác định tài khoản để không hiện nhầm thông báo dành cho khách vãng lai. */}
        {(accessToken || !isActorLoading) &&
          (linkResult.href ? (
            <Link to={linkResult.href} className="btn btn-primary">
              Xem đơn hàng
            </Link>
          ) : (
            <p className="muted">{linkResult.message}</p>
          ))}

        <Link to="/" className="btn btn-outline">
          Tiếp tục mua sắm
        </Link>
      </div>
    </main>
  );
}
