import { formatProductPrice } from '../../../catalog/customer/product-display';
import { ProductThumbnail } from '../product-thumbnail';
import type { OrderDetail, OrderDetailItem, OrderShippingAddress } from './orders-api';
import './orders.css';

// Danh sách dòng hàng của đơn. Trang chi tiết đơn và trang đặt hàng thành công dùng chung để hai trang luôn khớp nhau.
export function OrderItemsList({ items }: { items: OrderDetailItem[] }) {
  return (
    <ul className="order-items-list">
      {items.map((item, index) => (
        <li key={`${item.name}-${index}`} className="order-item-row">
          <ProductThumbnail imageUrl={item.imageUrl} size={64} className="order-line-image" />
          <div className="order-item-info">
            <p className="order-item-name">{item.name}</p>
            {item.variantName && <p className="order-item-meta">{item.variantName}</p>}
            <p className="order-item-meta">Số lượng: {item.quantity}</p>
          </div>
          <div className="order-item-price">
            <div className="order-item-unit-price">{formatProductPrice(item.unitPrice)}</div>
            <div className="order-item-line-total">{formatProductPrice(item.lineTotal)}</div>
          </div>
        </li>
      ))}
    </ul>
  );
}

// Các dòng tiền của đơn: tạm tính, phí vận chuyển, giảm phí vận chuyển và tổng cộng.
export function OrderPaymentRows({ order }: { order: OrderDetail }) {
  return (
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
  );
}

// Địa chỉ giao hàng trên một dòng. Phường và tỉnh có thể thiếu ở đơn cũ nên chỉ nối phần có dữ liệu.
export function formatShippingAddress(address: OrderShippingAddress): string {
  return [address.line, address.wardName, address.provinceName].filter(Boolean).join(', ');
}
