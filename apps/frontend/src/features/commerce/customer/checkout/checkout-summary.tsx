import { Link } from 'react-router-dom';
import { ErrorAlert } from '../../../../shared/error-alert';
import { formatProductPrice } from '../../../catalog/customer/product-display';
import type { CartLine } from '../cart-api';
import { ProductThumbnail } from '../product-thumbnail';
import type { CheckoutPreview } from './checkout-api';

interface CheckoutSummaryProps {
  items: CartLine[];
  // Tạm tính của giỏ. Hiện khi chưa có preview.
  cartSubtotal: number;
  preview: CheckoutPreview | undefined;
  isAddressIncomplete: boolean;
  isLoading: boolean;
  // Lỗi preview của shipping hoặc lỗi chung. Khi có lỗi, summary không hiện phí.
  errorMessage: string | null;
  onRetry: (() => void) | null;
  canSubmit: boolean;
  isSubmitting: boolean;
}

function SummaryRow({
  label,
  value,
  strong = false,
}: {
  label: string;
  value: string;
  strong?: boolean;
}) {
  return (
    <p className={strong ? 'checkout-summary-row checkout-summary-total' : 'checkout-summary-row'}>
      <span>{label}</span>
      <strong>{value}</strong>
    </p>
  );
}

function CheckoutItem({ line }: { line: CartLine }) {
  const options = Object.entries(line.optionValues ?? {});
  return (
    <li className="checkout-summary-item">
      <ProductThumbnail
        imageUrl={line.image?.url}
        size={56}
        className="checkout-summary-item-image"
      />
      <div className="checkout-summary-item-main">
        <p className="checkout-summary-item-name">{line.productName ?? 'Sản phẩm đã gỡ'}</p>
        {options.length > 0 && (
          <p className="checkout-summary-item-meta">
            {options.map(([name, value]) => `${name}: ${value}`).join(' · ')}
          </p>
        )}
        <p className="checkout-summary-item-meta">Số lượng: {line.quantity}</p>
      </div>
      {line.lineTotal !== null && (
        <strong className="checkout-summary-item-total">
          {formatProductPrice(line.lineTotal)}
        </strong>
      )}
    </li>
  );
}

// Khối tóm tắt tiền. Vùng số tiền có `aria-live` để trình đọc màn hình đọc khi tổng đổi.
export function CheckoutSummary({
  items,
  cartSubtotal,
  preview,
  isAddressIncomplete,
  isLoading,
  errorMessage,
  onRetry,
  canSubmit,
  isSubmitting,
}: CheckoutSummaryProps) {
  const hasError = errorMessage !== null;

  return (
    <aside className="card checkout-summary" aria-label="Tóm tắt đơn hàng">
      <div className="checkout-summary-head">
        <h2 className="checkout-summary-title">Tóm tắt đơn hàng</h2>
        <Link to="/cart" className="checkout-summary-edit">
          Sửa giỏ hàng
        </Link>
      </div>

      <ul className="checkout-summary-items">
        {items.map((line) => (
          <CheckoutItem key={line.skuId} line={line} />
        ))}
      </ul>

      <div aria-live="polite" aria-atomic="true" className="checkout-summary-figures">
        {preview && !hasError ? (
          <>
            <SummaryRow label="Tạm tính" value={formatProductPrice(preview.subtotal)} />
            <SummaryRow label="Phí vận chuyển" value={formatProductPrice(preview.shippingFee)} />
            {preview.shippingDiscount > 0 && (
              <SummaryRow
                label="Miễn phí vận chuyển"
                value={`-${formatProductPrice(preview.shippingDiscount)}`}
              />
            )}
            <SummaryRow label="Tổng tiền" value={formatProductPrice(preview.total)} strong />
          </>
        ) : (
          <>
            <SummaryRow label="Tạm tính" value={formatProductPrice(cartSubtotal)} />
            {isAddressIncomplete && !hasError && (
              <p className="muted small">Chọn địa chỉ để tính phí vận chuyển</p>
            )}
            {isLoading && !hasError && <p className="muted small">Đang tính phí vận chuyển...</p>}
          </>
        )}
      </div>

      {hasError && (
        <div className="checkout-summary-error">
          <ErrorAlert message={errorMessage} />
          {onRetry && (
            <button type="button" className="btn btn-outline btn-sm" onClick={onRetry}>
              Thử lại
            </button>
          )}
        </div>
      )}

      <button type="submit" className="btn btn-primary btn-block" disabled={!canSubmit}>
        {isSubmitting ? 'Đang đặt hàng...' : 'Đặt hàng'}
      </button>
    </aside>
  );
}
