import { Link } from 'react-router-dom';
import './delivery-notice.css';

// Dải thông báo phí giao hàng nằm ngay dưới khối đầu trang ở cả ba cỡ màn hình.
export function DeliveryNotice() {
  return (
    <section className="delivery-notice" aria-labelledby="home-delivery-notice-heading">
      <div className="delivery-notice-copy">
        <h2 id="home-delivery-notice-heading" className="delivery-notice-title">
          Mua online, nhận hàng tận nơi
        </h2>
        <p className="delivery-notice-description">
          Phí giao hàng và thời hạn bảo hành được ghi rõ trước khi đặt.
        </p>
      </div>
      <Link to="/policy/giao-hang" className="delivery-notice-cta">
        Xem phí giao hàng
      </Link>
    </section>
  );
}
