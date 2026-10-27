import { FieldError } from '../../../../shared/field-error';
import { Icon } from '../../../../shared/icons';
import type { PaymentMethod } from './checkout-api';

interface CheckoutPaymentMethodsProps {
  paymentMethod: PaymentMethod;
  codDisabledReason: string | null;
  errorMessage?: string | null;
  isSubmitting: boolean;
  onChange: (method: PaymentMethod) => void;
}

export function CheckoutPaymentMethods({
  paymentMethod,
  codDisabledReason,
  errorMessage,
  isSubmitting,
  onChange,
}: CheckoutPaymentMethodsProps) {
  const codDescriptionId = 'checkout-payment-cod-description';
  const sepayDescriptionId = 'checkout-payment-sepay-description';

  return (
    <section className="card checkout-section">
      <fieldset className="checkout-fieldset">
        <legend>
          <span className="checkout-section-number" data-step="3" aria-hidden="true" />
          <span>Phương thức thanh toán</span>
        </legend>
        <div className="checkout-payment-options">
          <label
            className={
              codDisabledReason
                ? 'checkout-radio checkout-payment-option checkout-radio-disabled'
                : 'checkout-radio checkout-payment-option'
            }
          >
            <input
              id="checkout-payment-cod"
              type="radio"
              name="paymentMethod"
              value="cod"
              checked={paymentMethod === 'cod'}
              disabled={isSubmitting || codDisabledReason !== null}
              aria-label="Thanh toán khi nhận hàng (COD)"
              aria-describedby={
                codDisabledReason ? `${codDescriptionId} checkout-cod-reason` : codDescriptionId
              }
              onChange={() => onChange('cod')}
            />
            <span className="checkout-payment-icon">
              <Icon name="truck" />
            </span>
            <span className="checkout-payment-copy">
              <strong>Thanh toán khi nhận hàng (COD)</strong>
              <small id={codDescriptionId}>Thanh toán bằng tiền mặt khi nhận được hàng.</small>
            </span>
          </label>
          {codDisabledReason && (
            <p id="checkout-cod-reason" className="muted small checkout-radio-reason">
              {codDisabledReason}
            </p>
          )}

          <label className="checkout-radio checkout-payment-option">
            <input
              id="checkout-payment-sepay"
              type="radio"
              name="paymentMethod"
              value="sepay"
              checked={paymentMethod === 'sepay'}
              disabled={isSubmitting}
              aria-label="Chuyển khoản (VietQR)"
              aria-describedby={sepayDescriptionId}
              onChange={() => onChange('sepay')}
            />
            <span className="checkout-payment-icon">
              <Icon name="smartphone" />
            </span>
            <span className="checkout-payment-copy">
              <strong>Chuyển khoản (VietQR)</strong>
              <small id={sepayDescriptionId}>Quét mã QR bằng ứng dụng ngân hàng sau khi đặt.</small>
            </span>
          </label>
        </div>
        <FieldError inputId="checkout-payment" message={errorMessage} />
      </fieldset>
    </section>
  );
}
