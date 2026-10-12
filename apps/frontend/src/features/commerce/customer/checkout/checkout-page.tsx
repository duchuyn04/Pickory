import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useRef, useState, type FocusEvent, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import type { ApiError } from '../../../../shared/api';
import { ErrorAlert } from '../../../../shared/error-alert';
import { FieldError, fieldErrorProps, hasFieldErrors } from '../../../../shared/field-error';
import { useActor } from '../../../identity/hooks/use-actor';
import { formatProductPrice } from '../../../catalog/customer/product-display';
import { AddressPicker, EMPTY_ADDRESS, type AddressValue } from '../../../shipping/address-picker';
import { canCheckout, checkoutHint } from '../cart-labels';
import { CART_QUERY_KEY, useCart } from '../use-cart';
import type { PaymentMethod, PlaceOrderPayload } from './checkout-api';
import {
  NOTE_MAX_LENGTH,
  validateCheckoutForm,
  type CheckoutFieldErrors,
  type CheckoutFieldName,
  type CheckoutFormValues,
} from './checkout-form-schema';
import {
  describeOrderError,
  describePreviewError,
  firstErrorField,
  type OrderFailure,
} from './checkout-rules';
import { CheckoutSummary } from './checkout-summary';
import { useCheckoutPreview } from './use-checkout-preview';
import { usePlaceOrder } from './use-place-order';
import './checkout-page.css';

// Id ổn định cho từng ô, để nhãn, lỗi, focus và E2E nối với đúng ô.
const FIELD_IDS: Record<CheckoutFieldName, string> = {
  'contact.name': 'checkout-contact-name',
  'contact.phone': 'checkout-contact-phone',
  'contact.email': 'checkout-contact-email',
  address: 'checkout-address-province',
  'address.provinceCode': 'checkout-address-province',
  'address.wardCode': 'checkout-address-ward',
  'address.line': 'checkout-address-line',
  note: 'checkout-note',
  paymentMethod: 'checkout-payment-sepay',
  invoice: 'checkout-invoice-toggle',
  'invoice.companyName': 'checkout-invoice-company-name',
  'invoice.taxCode': 'checkout-invoice-tax-code',
  'invoice.address': 'checkout-invoice-address',
  'invoice.email': 'checkout-invoice-email',
};

const ADDRESS_ID_PREFIX = 'checkout-address';
const ADDRESS_ERROR_ANCHOR_ID = 'checkout-address-group';

const EMPTY_FORM: CheckoutFormValues = {
  contact: { name: '', phone: '', email: '' },
  address: EMPTY_ADDRESS,
  note: '',
  paymentMethod: 'cod',
  wantsInvoice: false,
  invoice: { companyName: '', taxCode: '', address: '', email: '' },
};

function BlockedCheckout({ message }: { message: string }) {
  return (
    <div className="card checkout-blocked">
      <h2>Chưa đặt hàng được</h2>
      <p className="muted">{message}</p>
      <Link to="/cart" className="btn btn-primary">
        Quay lại giỏ hàng
      </Link>
    </div>
  );
}

// Trang đặt hàng một trang: liên hệ, địa chỉ, ghi chú, thanh toán, hóa đơn và tóm tắt tiền.
export function CheckoutPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const cartQuery = useCart();
  const { data: actor } = useActor();
  const placeOrderMutation = usePlaceOrder();

  const [values, setValues] = useState<CheckoutFormValues>(EMPTY_FORM);
  const [fieldErrors, setFieldErrors] = useState<CheckoutFieldErrors>({});
  const [orderFailure, setOrderFailure] = useState<OrderFailure | null>(null);
  // Tổng mới sau `ORDER_TOTAL_CHANGED`. `null` khi chưa tải lại được preview.
  const [refreshedTotal, setRefreshedTotal] = useState<number | null>(null);
  const [hasSwitchedFromCod, setHasSwitchedFromCod] = useState(false);
  const [focusRequest, setFocusRequest] = useState<CheckoutFieldName | null>(null);

  // Một khóa cho mỗi lần mở trang. Giữ trong state, không lưu `localStorage`, nên đóng trang là hết khóa.
  const [idempotencyKey] = useState(() => crypto.randomUUID());
  // Ref chặn bấm đúp ngay trong cùng tick, trước khi React kịp vẽ lại nút đã tắt.
  const isSubmittingRef = useRef(false);
  const hasPrefilledRef = useRef(false);

  const cart = cartQuery.data;
  const isCartReady = cart !== undefined && canCheckout(cart);

  const preview = useCheckoutPreview(values.address, values.paymentMethod, isCartReady);
  const previewFailure = useMemo(
    () => (preview.error ? describePreviewError(preview.error) : null),
    [preview.error],
  );

  // Điền sẵn liên hệ một lần khi khách đã đăng nhập. Ô khách đã gõ không bị ghi đè.
  useEffect(() => {
    if (hasPrefilledRef.current || !actor || actor.userId === null) return;
    hasPrefilledRef.current = true;
    setValues((current) => ({
      ...current,
      contact: {
        name: current.contact.name || actor.name || '',
        phone: current.contact.phone || actor.phone || '',
        email: current.contact.email || actor.email || '',
      },
    }));
  }, [actor]);

  // Preview báo vượt hạn mức COD thì đổi sang chuyển khoản. Đổi phương thức làm preview tự gọi lại.
  useEffect(() => {
    if (previewFailure?.kind !== 'cod-not-allowed') return;
    setValues((current) =>
      current.paymentMethod === 'cod' ? { ...current, paymentMethod: 'sepay' } : current,
    );
    setHasSwitchedFromCod(true);
  }, [previewFailure]);

  // Focus sau khi React vẽ lại, vì ô lỗi có thể vừa được vẽ trong cùng lượt cập nhật.
  useEffect(() => {
    if (focusRequest === null) return;
    document.getElementById(FIELD_IDS[focusRequest])?.focus();
    setFocusRequest(null);
  }, [focusRequest]);

  const addressErrors: CheckoutFieldErrors = {
    ...fieldErrors,
    ...(previewFailure?.kind === 'address' ? previewFailure.fieldErrors : {}),
  };

  const codDisabledReason =
    preview.data && !preview.data.codAllowed
      ? `Đơn trên ${formatProductPrice(preview.data.codMaxAmount)} chỉ thanh toán chuyển khoản`
      : null;

  function changeField(field: CheckoutFieldName) {
    // Sửa ô nào thì lỗi của ô đó hết hiệu lực, vì lỗi cũ nói về giá trị cũ.
    setFieldErrors((current) => ({ ...current, [field]: null }));
  }

  function changeContact(field: 'name' | 'phone' | 'email', text: string) {
    setValues((current) => ({ ...current, contact: { ...current.contact, [field]: text } }));
    changeField(`contact.${field}`);
  }

  function changeAddress(next: AddressValue) {
    setValues((current) => ({ ...current, address: next }));
    setFieldErrors((current) => ({
      ...current,
      address: null,
      'address.provinceCode': null,
      'address.wardCode': null,
      'address.line': null,
    }));
  }

  function changeInvoice(field: 'companyName' | 'taxCode' | 'address' | 'email', text: string) {
    setValues((current) => ({ ...current, invoice: { ...current.invoice, [field]: text } }));
    changeField(`invoice.${field}`);
  }

  function changePaymentMethod(method: PaymentMethod) {
    setValues((current) => ({ ...current, paymentMethod: method }));
    setHasSwitchedFromCod(false);
    changeField('paymentMethod');
  }

  // Kiểm một ô khi rời ô. Lỗi của các ô khác chưa chạm tới thì chưa hiện.
  function validateFieldOnBlur(field: CheckoutFieldName) {
    const errors = validateCheckoutForm(values);
    setFieldErrors((current) => ({ ...current, [field]: errors[field] ?? null }));
  }

  function handleAddressBlur(event: FocusEvent<HTMLDivElement>) {
    // Hai ô chọn tỉnh và phường chỉ nhận giá trị từ danh sách, nên chỉ ô số nhà cần kiểm khi rời ô.
    if (event.target.id === FIELD_IDS['address.line']) validateFieldOnBlur('address.line');
  }

  function showFieldErrors(errors: CheckoutFieldErrors) {
    setFieldErrors(errors);
    const firstField = firstErrorField(errors);
    if (firstField) setFocusRequest(firstField);
  }

  function buildPayload(expectedTotal: number): PlaceOrderPayload {
    const note = values.note.trim();
    return {
      contact: {
        name: values.contact.name.trim(),
        phone: values.contact.phone.trim(),
        email: values.contact.email.trim(),
      },
      address: {
        provinceCode: values.address.provinceCode,
        wardCode: values.address.wardCode,
        line: values.address.line.trim(),
      },
      note: note === '' ? null : note,
      paymentMethod: values.paymentMethod,
      invoice: values.wantsInvoice
        ? {
            companyName: values.invoice.companyName.trim(),
            taxCode: values.invoice.taxCode.trim(),
            address: values.invoice.address.trim(),
            email: values.invoice.email.trim(),
          }
        : null,
      expectedTotal,
    };
  }

  async function handleOrderFailure(error: ApiError) {
    const failure = describeOrderError(error);
    setOrderFailure(failure);

    if (failure.kind === 'total-changed') {
      // Lấy tổng mới để khách thấy số cần xác nhận. Trang không tự đặt lại đơn.
      const refreshed = await preview.refetch();
      setRefreshedTotal(refreshed.isError || !refreshed.data ? null : refreshed.data.total);
      return;
    }

    if (failure.kind === 'field-errors') {
      showFieldErrors(failure.fieldErrors);
    }
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    // Bấm đúp hoặc Enter lúc đang gửi không được tạo request thứ hai.
    if (isSubmittingRef.current) return;

    setOrderFailure(null);
    setRefreshedTotal(null);

    // Kiểm phía client trước. Còn lỗi thì không gửi request và focus vào ô lỗi đầu tiên.
    const clientErrors = validateCheckoutForm(values);
    if (hasFieldErrors(clientErrors)) {
      showFieldErrors(clientErrors);
      return;
    }
    setFieldErrors({});

    // Chỉ đặt đơn với tổng của preview đang hiện. Chưa có preview thì khách chưa thấy số tiền phải trả.
    if (!preview.data || !preview.isCurrent) return;

    isSubmittingRef.current = true;
    try {
      const order = await placeOrderMutation.mutateAsync({
        payload: buildPayload(preview.data.total),
        idempotencyKey,
      });

      // Đơn đã tạo thì giỏ đã trống ở server. Xóa cache để mini cart và trang giỏ nạp lại.
      await queryClient.resetQueries({ queryKey: CART_QUERY_KEY });

      // Token đi qua state của router, không đi vào URL, để không lọt vào lịch sử trình duyệt và log.
      navigate(`/orders/${encodeURIComponent(order.code)}/success`, {
        replace: true,
        state: { accessToken: order.accessToken, paymentMethod: order.paymentMethod },
      });
    } catch (error) {
      await handleOrderFailure(error as ApiError);
    } finally {
      isSubmittingRef.current = false;
    }
  }

  if (cartQuery.isPending) {
    return (
      <main className="catalog-page checkout-page">
        <p role="status" aria-busy="true">
          Đang tải giỏ hàng...
        </p>
      </main>
    );
  }

  if (cartQuery.isError || !cart) {
    return (
      <main className="catalog-page checkout-page">
        <h1>Đặt hàng</h1>
        <ErrorAlert message={cartQuery.error?.message ?? 'Không tải được giỏ hàng.'} />
        <Link to="/cart" className="btn btn-outline">
          Quay lại giỏ hàng
        </Link>
      </main>
    );
  }

  if (!isCartReady) {
    return (
      <main className="catalog-page checkout-page">
        <h1>Đặt hàng</h1>
        <BlockedCheckout message={checkoutHint(cart) ?? 'Giỏ hàng chưa sẵn sàng để đặt hàng.'} />
      </main>
    );
  }

  const isSubmitting = placeOrderMutation.isPending;
  const hasPreview = preview.data !== undefined;
  const canSubmit =
    hasPreview &&
    preview.isCurrent &&
    !preview.isLoading &&
    previewFailure === null &&
    !isSubmitting;

  // Mô tả lý do lỗi preview cho khối tóm tắt. Lỗi dưới ô địa chỉ không lặp lại ở đây.
  const summaryError =
    previewFailure && previewFailure.kind !== 'address' && previewFailure.kind !== 'cod-not-allowed'
      ? previewFailure.message
      : null;
  const canRetryPreview =
    previewFailure?.kind === 'shipping-unavailable' || previewFailure?.kind === 'rate-limited';

  const shortageNames =
    orderFailure?.kind === 'insufficient-stock'
      ? orderFailure.shortages.map((shortage) => {
          const line = cart.items.find((item) => item.skuId === shortage.skuId);
          return `${line?.productName ?? 'Sản phẩm'}: ${shortage.message}`;
        })
      : [];

  const isPreviewCartProblem = previewFailure?.kind === 'cart-problem';

  return (
    <main className="catalog-page checkout-page">
      <h1>Đặt hàng</h1>

      {orderFailure?.kind === 'total-changed' && (
        <ErrorAlert
          className="checkout-alert"
          message={
            refreshedTotal === null
              ? orderFailure.message
              : `Tổng tiền đã đổi thành ${formatProductPrice(refreshedTotal)}.`
          }
          items={['Hãy kiểm tra lại đơn hàng rồi bấm Đặt hàng để xác nhận.']}
        />
      )}

      {orderFailure?.kind === 'insufficient-stock' && (
        <div className="checkout-alert">
          <ErrorAlert message={orderFailure.message} items={shortageNames} />
          <Link to="/cart" className="btn btn-outline btn-sm">
            Quay lại giỏ hàng
          </Link>
        </div>
      )}

      {orderFailure?.kind === 'field-errors' && orderFailure.unmatched.length > 0 && (
        <ErrorAlert
          className="checkout-alert"
          message={orderFailure.unmatched[0]}
          items={orderFailure.unmatched.slice(1)}
        />
      )}

      {orderFailure?.kind === 'other' && (
        <ErrorAlert className="checkout-alert" message={orderFailure.message} />
      )}

      {isPreviewCartProblem && (
        <div className="checkout-alert">
          <ErrorAlert message={previewFailure.message} />
          <Link to="/cart" className="btn btn-outline btn-sm">
            Quay lại giỏ hàng
          </Link>
        </div>
      )}

      {hasSwitchedFromCod && (
        <p role="status" className="alert alert-warn checkout-alert">
          Đơn hàng vượt hạn mức thanh toán khi nhận hàng. Trang đã chuyển sang chuyển khoản.
        </p>
      )}

      <form noValidate className="checkout-layout" onSubmit={(event) => void handleSubmit(event)}>
        <div className="checkout-main">
          <section className="card checkout-section" aria-labelledby="checkout-contact-title">
            <h2 id="checkout-contact-title">Thông tin liên hệ</h2>
            <div className="form-grid form-grid-2">
              <div className="field">
                <label htmlFor={FIELD_IDS['contact.name']}>Họ và tên</label>
                <input
                  id={FIELD_IDS['contact.name']}
                  type="text"
                  autoComplete="name"
                  disabled={isSubmitting}
                  value={values.contact.name}
                  onChange={(event) => changeContact('name', event.target.value)}
                  onBlur={() => validateFieldOnBlur('contact.name')}
                  {...fieldErrorProps(FIELD_IDS['contact.name'], fieldErrors['contact.name'])}
                />
                <FieldError
                  inputId={FIELD_IDS['contact.name']}
                  message={fieldErrors['contact.name']}
                />
              </div>
              <div className="field">
                <label htmlFor={FIELD_IDS['contact.phone']}>Số điện thoại</label>
                <input
                  id={FIELD_IDS['contact.phone']}
                  type="tel"
                  autoComplete="tel"
                  inputMode="tel"
                  disabled={isSubmitting}
                  value={values.contact.phone}
                  onChange={(event) => changeContact('phone', event.target.value)}
                  onBlur={() => validateFieldOnBlur('contact.phone')}
                  {...fieldErrorProps(FIELD_IDS['contact.phone'], fieldErrors['contact.phone'])}
                />
                <FieldError
                  inputId={FIELD_IDS['contact.phone']}
                  message={fieldErrors['contact.phone']}
                />
              </div>
              <div className="field field-full">
                <label htmlFor={FIELD_IDS['contact.email']}>Email</label>
                <input
                  id={FIELD_IDS['contact.email']}
                  type="email"
                  autoComplete="email"
                  disabled={isSubmitting}
                  value={values.contact.email}
                  onChange={(event) => changeContact('email', event.target.value)}
                  onBlur={() => validateFieldOnBlur('contact.email')}
                  {...fieldErrorProps(FIELD_IDS['contact.email'], fieldErrors['contact.email'])}
                />
                <FieldError
                  inputId={FIELD_IDS['contact.email']}
                  message={fieldErrors['contact.email']}
                />
              </div>
            </div>
          </section>

          <section className="card checkout-section" aria-labelledby="checkout-address-title">
            <h2 id="checkout-address-title">Địa chỉ nhận hàng</h2>
            <div id={ADDRESS_ERROR_ANCHOR_ID} onBlur={handleAddressBlur}>
              <AddressPicker
                value={values.address}
                onChange={changeAddress}
                idPrefix={ADDRESS_ID_PREFIX}
                disabled={isSubmitting}
                errors={{
                  province: addressErrors['address.provinceCode'],
                  ward: addressErrors['address.wardCode'],
                  line: addressErrors['address.line'],
                }}
              />
            </div>
            <FieldError inputId={ADDRESS_ID_PREFIX} message={addressErrors.address} />
          </section>

          <section className="card checkout-section" aria-labelledby="checkout-note-title">
            <h2 id="checkout-note-title">Ghi chú</h2>
            <div className="field">
              <label htmlFor={FIELD_IDS.note}>Ghi chú cho đơn hàng (không bắt buộc)</label>
              <textarea
                id={FIELD_IDS.note}
                rows={3}
                disabled={isSubmitting}
                value={values.note}
                onChange={(event) => {
                  setValues((current) => ({ ...current, note: event.target.value }));
                  changeField('note');
                }}
                onBlur={() => validateFieldOnBlur('note')}
                {...fieldErrorProps(FIELD_IDS.note, fieldErrors.note)}
              />
              <FieldError inputId={FIELD_IDS.note} message={fieldErrors.note} />
              <span className="muted small">Tối đa {NOTE_MAX_LENGTH} ký tự.</span>
            </div>
          </section>

          <section className="card checkout-section">
            <fieldset className="checkout-fieldset">
              <legend>Phương thức thanh toán</legend>
              <label
                className={
                  codDisabledReason ? 'checkout-radio checkout-radio-disabled' : 'checkout-radio'
                }
              >
                <input
                  id="checkout-payment-cod"
                  type="radio"
                  name="paymentMethod"
                  value="cod"
                  checked={values.paymentMethod === 'cod'}
                  disabled={isSubmitting || codDisabledReason !== null}
                  aria-describedby={codDisabledReason ? 'checkout-cod-reason' : undefined}
                  onChange={() => changePaymentMethod('cod')}
                />
                <span>Thanh toán khi nhận hàng (COD)</span>
              </label>
              {codDisabledReason && (
                <p id="checkout-cod-reason" className="muted small checkout-radio-reason">
                  {codDisabledReason}
                </p>
              )}
              <label className="checkout-radio">
                <input
                  id="checkout-payment-sepay"
                  type="radio"
                  name="paymentMethod"
                  value="sepay"
                  checked={values.paymentMethod === 'sepay'}
                  disabled={isSubmitting}
                  onChange={() => changePaymentMethod('sepay')}
                />
                <span>Chuyển khoản (VietQR)</span>
              </label>
              <FieldError inputId="checkout-payment" message={fieldErrors.paymentMethod} />
            </fieldset>
          </section>

          <section className="card checkout-section" aria-labelledby="checkout-invoice-title">
            <h2 id="checkout-invoice-title">Hóa đơn VAT</h2>
            <label className="checkout-radio">
              <input
                id={FIELD_IDS.invoice}
                type="checkbox"
                checked={values.wantsInvoice}
                disabled={isSubmitting}
                onChange={(event) => {
                  setValues((current) => ({ ...current, wantsInvoice: event.target.checked }));
                  changeField('invoice');
                }}
              />
              <span>Xuất hóa đơn công ty</span>
            </label>
            <FieldError inputId={FIELD_IDS.invoice} message={fieldErrors.invoice} />

            {values.wantsInvoice && (
              <div className="form-grid form-grid-2 checkout-invoice-fields">
                <div className="field field-full">
                  <label htmlFor={FIELD_IDS['invoice.companyName']}>Tên công ty</label>
                  <input
                    id={FIELD_IDS['invoice.companyName']}
                    type="text"
                    autoComplete="organization"
                    disabled={isSubmitting}
                    value={values.invoice.companyName}
                    onChange={(event) => changeInvoice('companyName', event.target.value)}
                    onBlur={() => validateFieldOnBlur('invoice.companyName')}
                    {...fieldErrorProps(
                      FIELD_IDS['invoice.companyName'],
                      fieldErrors['invoice.companyName'],
                    )}
                  />
                  <FieldError
                    inputId={FIELD_IDS['invoice.companyName']}
                    message={fieldErrors['invoice.companyName']}
                  />
                </div>
                <div className="field">
                  <label htmlFor={FIELD_IDS['invoice.taxCode']}>Mã số thuế</label>
                  <input
                    id={FIELD_IDS['invoice.taxCode']}
                    type="text"
                    inputMode="numeric"
                    autoComplete="off"
                    disabled={isSubmitting}
                    value={values.invoice.taxCode}
                    onChange={(event) => changeInvoice('taxCode', event.target.value)}
                    onBlur={() => validateFieldOnBlur('invoice.taxCode')}
                    {...fieldErrorProps(
                      FIELD_IDS['invoice.taxCode'],
                      fieldErrors['invoice.taxCode'],
                    )}
                  />
                  <FieldError
                    inputId={FIELD_IDS['invoice.taxCode']}
                    message={fieldErrors['invoice.taxCode']}
                  />
                </div>
                <div className="field">
                  <label htmlFor={FIELD_IDS['invoice.email']}>Email nhận hóa đơn</label>
                  <input
                    id={FIELD_IDS['invoice.email']}
                    type="email"
                    autoComplete="off"
                    disabled={isSubmitting}
                    value={values.invoice.email}
                    onChange={(event) => changeInvoice('email', event.target.value)}
                    onBlur={() => validateFieldOnBlur('invoice.email')}
                    {...fieldErrorProps(FIELD_IDS['invoice.email'], fieldErrors['invoice.email'])}
                  />
                  <FieldError
                    inputId={FIELD_IDS['invoice.email']}
                    message={fieldErrors['invoice.email']}
                  />
                </div>
                <div className="field field-full">
                  <label htmlFor={FIELD_IDS['invoice.address']}>Địa chỉ công ty</label>
                  <input
                    id={FIELD_IDS['invoice.address']}
                    type="text"
                    autoComplete="off"
                    disabled={isSubmitting}
                    value={values.invoice.address}
                    onChange={(event) => changeInvoice('address', event.target.value)}
                    onBlur={() => validateFieldOnBlur('invoice.address')}
                    {...fieldErrorProps(
                      FIELD_IDS['invoice.address'],
                      fieldErrors['invoice.address'],
                    )}
                  />
                  <FieldError
                    inputId={FIELD_IDS['invoice.address']}
                    message={fieldErrors['invoice.address']}
                  />
                </div>
              </div>
            )}
          </section>
        </div>

        <CheckoutSummary
          cartSubtotal={cart.subtotal}
          preview={preview.data}
          isAddressIncomplete={preview.isAddressIncomplete}
          isLoading={preview.isLoading}
          errorMessage={summaryError}
          onRetry={canRetryPreview ? () => void preview.refetch() : null}
          canSubmit={canSubmit}
          isSubmitting={isSubmitting}
        />
      </form>
    </main>
  );
}
