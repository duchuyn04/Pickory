import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { ApiError } from '../../../shared/api';
import { ErrorAlert } from '../../../shared/error-alert';
import { FieldError, hasFieldErrors } from '../../../shared/field-error';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { useCan } from '../../identity/hooks/use-actor';
import {
  commerceSettingsQueryKey,
  fetchCommerceSettings,
  saveCommerceSettings,
  type CommerceSettings,
} from '../commerce-settings-api';
import {
  MAX_CART_LINES_UPPER_BOUND,
  MAX_QUANTITY_PER_SKU_UPPER_BOUND,
  MONEY_MAX_AMOUNT,
  formatCount,
  formatVnd,
  mapServerErrors,
  parseBoundedInteger,
  validateCommerceForm,
  type CommerceFieldErrors,
  type CommerceFieldName,
  type CommerceFormValues,
} from '../commerce-settings-validation';
import './commerce-settings.css';

const COMMERCE_SETTINGS_PERMISSION = 'commerce-settings:manage';

const SAVED_MESSAGE = 'Đã lưu cấu hình bán hàng.';

// Id ổn định cho từng ô, để nhãn, gợi ý, lỗi và E2E nối với đúng ô.
const FIELD_IDS: Record<CommerceFieldName, string> = {
  freeShippingThreshold: 'commerce-free-shipping-threshold',
  codMaxAmount: 'commerce-cod-max-amount',
  maxQuantityPerSku: 'commerce-max-quantity-per-sku',
  maxCartLines: 'commerce-max-cart-lines',
  sepayAccountNumber: 'commerce-sepay-account-number',
  sepayBankCode: 'commerce-sepay-bank-code',
};

// Màn cấu hình ngưỡng miễn phí giao hàng, hạn mức COD, giới hạn giỏ hàng và tài khoản nhận chuyển khoản.
export function CommerceSettingsPage() {
  const canManage = useCan(COMMERCE_SETTINGS_PERMISSION);

  // Thiếu quyền thì không gọi API, vì backend chắc chắn trả 403.
  // Tắt tải lại khi focus cửa sổ để dữ liệu đang sửa dở không bị ghi đè.
  const settingsQuery = useQuery({
    queryKey: commerceSettingsQueryKey,
    queryFn: fetchCommerceSettings,
    enabled: canManage,
    refetchOnWindowFocus: false,
  });

  // Hiện thông báo thiếu quyền khi mở thẳng URL, vì menu chỉ ẩn mục chứ không chặn đường dẫn.
  if (!canManage) {
    return (
      <ForbiddenNotice permission={COMMERCE_SETTINGS_PERMISSION} subject="cấu hình bán hàng" />
    );
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Cấu hình bán hàng</h1>
          <p>
            Ngưỡng miễn phí giao hàng, hạn mức COD, giới hạn giỏ hàng và tài khoản nhận chuyển
            khoản.
          </p>
        </div>
      </div>

      {settingsQuery.isError && (
        <ErrorAlert
          message={
            settingsQuery.error instanceof Error && settingsQuery.error.message
              ? settingsQuery.error.message
              : 'Không tải được cấu hình bán hàng.'
          }
        />
      )}

      {settingsQuery.isLoading && (
        <p role="status" className="empty-state">
          Đang tải cấu hình...
        </p>
      )}

      {settingsQuery.data && <CommerceSettingsForm initialSettings={settingsQuery.data} />}
    </div>
  );
}

// Số `null` ở backend là chuỗi rỗng ở ô nhập, vì ô nhập chỉ giữ chuỗi.
function toFormValues(settings: CommerceSettings): CommerceFormValues {
  return {
    freeShippingThreshold: String(settings.freeShippingThreshold),
    codMaxAmount: String(settings.codMaxAmount),
    maxQuantityPerSku: String(settings.maxQuantityPerSku),
    maxCartLines: String(settings.maxCartLines),
    sepayAccountNumber: settings.sepayAccountNumber ?? '',
    sepayBankCode: settings.sepayBankCode ?? '',
  };
}

// Chuỗi trống hoặc chỉ có khoảng trắng là chưa nhập, tức `null` ở backend.
function emptyToNull(text: string): string | null {
  const trimmed = text.trim();
  return trimmed === '' ? null : trimmed;
}

interface CommerceSettingsFormProps {
  initialSettings: CommerceSettings;
}

function CommerceSettingsForm({ initialSettings }: CommerceSettingsFormProps) {
  const queryClient = useQueryClient();
  const [values, setValues] = useState<CommerceFormValues>(toFormValues(initialSettings));
  const [fieldErrors, setFieldErrors] = useState<CommerceFieldErrors>({});
  // Lỗi không gắn được vào ô nào, hiện bằng `ErrorAlert` đầu form.
  const [formErrors, setFormErrors] = useState<string[]>([]);
  const [isSaved, setIsSaved] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Sửa ô nào thì lỗi của ô đó hết hiệu lực, vì lỗi cũ nói về giá trị cũ.
  // Hai ô SePay đi cùng nhau nên sửa một ô cũng xóa lỗi cặp ở ô kia.
  function changeField(field: CommerceFieldName, text: string) {
    setValues((current) => ({ ...current, [field]: text }));
    setFieldErrors((current) => {
      const next = { ...current };
      delete next[field];
      if (field === 'sepayAccountNumber') delete next.sepayBankCode;
      if (field === 'sepayBankCode') delete next.sepayAccountNumber;
      return next;
    });
    setIsSaved(false);
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    // Nút đã tắt khi đang gửi, nhưng phím Enter trong ô nhập vẫn gửi form. Chặn tại đây để không gửi hai lần.
    if (isSubmitting) return;

    setIsSaved(false);
    setFormErrors([]);

    // Kiểm phía client trước. Còn lỗi thì không gửi request, để lỗi hiện ngay dưới đúng ô.
    const clientErrors = validateCommerceForm(values);
    setFieldErrors(clientErrors);
    if (hasFieldErrors(clientErrors)) return;

    setIsSubmitting(true);
    try {
      // Chỉ gửi 6 trường backend nhận. Khóa webhook SePay nằm ở biến môi trường, không đi qua màn này.
      const saved = await saveCommerceSettings({
        freeShippingThreshold: Number(values.freeShippingThreshold.trim()),
        codMaxAmount: Number(values.codMaxAmount.trim()),
        maxQuantityPerSku: Number(values.maxQuantityPerSku.trim()),
        maxCartLines: Number(values.maxCartLines.trim()),
        sepayAccountNumber: emptyToNull(values.sepayAccountNumber),
        sepayBankCode: emptyToNull(values.sepayBankCode),
      });

      // Cập nhật cache để lần mở lại màn trong phiên này hiện dữ liệu vừa lưu.
      // Form nhận lại dữ liệu server trả về, ví dụ mã ngân hàng đã đổi sang chữ hoa.
      queryClient.setQueryData(commerceSettingsQueryKey, saved);
      setValues(toFormValues(saved));
      setFieldErrors({});
      setIsSaved(true);
    } catch (error) {
      if (error instanceof ApiError && error.status === 400) {
        // Lỗi 400 gán vào đúng ô. Phần không gắn được vào ô nào hiện đầu form.
        const mapped = mapServerErrors(error);
        setFieldErrors(mapped.fieldErrors);
        setFormErrors(mapped.unmatched);
      } else {
        // Lỗi khác (403, 503, mạng) hiện đầu form. Dữ liệu đã nhập giữ nguyên để thử lại.
        setFormErrors([error instanceof Error ? error.message : 'Không lưu được cấu hình.']);
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  // Gợi ý dưới ô tiền: khoảng hợp lệ và số đang nhập theo định dạng VND.
  function moneyHint(field: 'freeShippingThreshold' | 'codMaxAmount'): string {
    const range = `Từ 0 đến ${formatCount(MONEY_MAX_AMOUNT)} đ.`;
    const amount = parseBoundedInteger(values[field], 0, MONEY_MAX_AMOUNT);
    return amount === null ? range : `${range} Đang nhập: ${formatVnd(amount)}.`;
  }

  return (
    <>
      {formErrors.length > 0 && <ErrorAlert message={formErrors[0]} items={formErrors.slice(1)} />}

      {isSaved && (
        <p role="status" className="alert alert-ok">
          {SAVED_MESSAGE}
        </p>
      )}

      <form noValidate className="card" onSubmit={handleSubmit}>
        <div className="card-head">
          <h2>Giỏ hàng và giao hàng</h2>
        </div>
        <div className="form-grid form-grid-2">
          <SettingsField
            field="freeShippingThreshold"
            label="Ngưỡng miễn phí giao hàng (đ)"
            hint={moneyHint('freeShippingThreshold')}
            values={values}
            errors={fieldErrors}
            disabled={isSubmitting}
            onChange={changeField}
          />
          <SettingsField
            field="codMaxAmount"
            label="Hạn mức thanh toán COD (đ)"
            hint={moneyHint('codMaxAmount')}
            values={values}
            errors={fieldErrors}
            disabled={isSubmitting}
            onChange={changeField}
          />
          <SettingsField
            field="maxQuantityPerSku"
            label="Số lượng tối đa mỗi sản phẩm"
            hint={`Từ 1 đến ${MAX_QUANTITY_PER_SKU_UPPER_BOUND}.`}
            values={values}
            errors={fieldErrors}
            disabled={isSubmitting}
            onChange={changeField}
          />
          <SettingsField
            field="maxCartLines"
            label="Số dòng tối đa mỗi giỏ"
            hint={`Từ 1 đến ${MAX_CART_LINES_UPPER_BOUND}.`}
            values={values}
            errors={fieldErrors}
            disabled={isSubmitting}
            onChange={changeField}
          />
        </div>

        <div className="card-head commerce-settings-section">
          <h2>Tài khoản nhận chuyển khoản</h2>
        </div>
        <p className="muted commerce-settings-note">
          Điền cả số tài khoản và mã ngân hàng, hoặc để trống cả hai.
        </p>
        <div className="form-grid form-grid-2">
          <SettingsField
            field="sepayAccountNumber"
            label="Số tài khoản"
            hint="Từ 6 đến 20 chữ số."
            values={values}
            errors={fieldErrors}
            disabled={isSubmitting}
            onChange={changeField}
          />
          <SettingsField
            field="sepayBankCode"
            label="Mã ngân hàng"
            hint="Từ 2 đến 20 chữ cái hoặc chữ số. Hệ thống lưu dạng chữ hoa."
            values={values}
            errors={fieldErrors}
            disabled={isSubmitting}
            onChange={changeField}
          />
        </div>

        <div className="head-actions">
          <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
            {isSubmitting ? 'Đang lưu...' : 'Lưu'}
          </button>
        </div>
      </form>
    </>
  );
}

interface SettingsFieldProps {
  field: CommerceFieldName;
  label: string;
  hint: string;
  values: CommerceFormValues;
  errors: CommerceFieldErrors;
  disabled: boolean;
  onChange: (field: CommerceFieldName, text: string) => void;
}

// Một ô nhập có nhãn, gợi ý giới hạn và câu lỗi ngay dưới ô.
function SettingsField({
  field,
  label,
  hint,
  values,
  errors,
  disabled,
  onChange,
}: SettingsFieldProps) {
  const inputId = FIELD_IDS[field];
  const hintId = `${inputId}-hint`;
  const error = errors[field];

  return (
    <div className="field">
      <label htmlFor={inputId}>{label}</label>
      <input
        id={inputId}
        type="text"
        // Ô số mở bàn phím số trên điện thoại. Kiểu ô giữ là chữ để người dùng nhập số âm và thấy lỗi dưới ô.
        inputMode={field === 'sepayBankCode' ? 'text' : 'numeric'}
        autoComplete="off"
        disabled={disabled}
        value={values[field]}
        onChange={(event) => onChange(field, event.target.value)}
        aria-invalid={error ? true : undefined}
        // Có lỗi thì trình đọc màn hình đọc câu lỗi. Không có lỗi thì đọc gợi ý giới hạn.
        aria-describedby={error ? `${inputId}-error` : hintId}
      />
      <FieldError inputId={inputId} message={error} />
      <span id={hintId} className="commerce-settings-hint">
        {hint}
      </span>
    </div>
  );
}
