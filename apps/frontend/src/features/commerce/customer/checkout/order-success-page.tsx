import { Link, useLocation, useParams } from 'react-router-dom';
import './checkout-page.css';

interface OrderSuccessState {
  accessToken?: string | null;
}

// Trang thành công tối thiểu. COM-UI3 hoàn thiện trang này với chi tiết đơn và hướng dẫn thanh toán.
export function OrderSuccessPage() {
  const { code } = useParams();
  const location = useLocation();
  const state = (location.state ?? null) as OrderSuccessState | null;
  const accessToken = state?.accessToken ?? null;

  return (
    <main className="catalog-page checkout-page">
      <div className="card order-success">
        <h1>Đặt hàng thành công</h1>
        <p>
          Mã đơn hàng của bạn: <strong className="order-success-code">{code}</strong>
        </p>
        {accessToken && code && (
          <Link
            to={`/orders/${encodeURIComponent(code)}?t=${encodeURIComponent(accessToken)}`}
            className="btn btn-primary"
          >
            Xem đơn hàng
          </Link>
        )}
        <Link to="/" className="btn btn-outline">
          Tiếp tục mua sắm
        </Link>
      </div>
    </main>
  );
}
