import { ErrorAlert } from '../../../../shared/error-alert';
import { formatProductPrice } from '../../../catalog/customer/product-display';
import type { CheckoutPreview } from './checkout-api';

interface CheckoutSummaryProps {
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

// Khối tóm tắt tiền. Vùng số tiền có `aria-live` để trình đọc màn hình đọc khi tổng đổi.
export function CheckoutSummary({
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
      <h2 className="checkout-summary-title">Tóm tắt đơn hàng</h2>

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
