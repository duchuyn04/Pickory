import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent, type InputHTMLAttributes } from 'react';
import { ApiError } from '../../../shared/api';
import { ErrorAlert } from '../../../shared/error-alert';
import { FieldError, hasFieldErrors } from '../../../shared/field-error';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { MoneyInput } from '../../../shared/money-input';
import { parseVndInput } from '../../../shared/money-input-format';
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
  SHOP_ADDRESS_MAX_LENGTH,
  SHOP_NAME_MAX_LENGTH,
  SHOP_PHONE_MAX_LENGTH,
  SHOP_TAX_CODE_MAX_LENGTH,
  formatCount,
  mapServerErrors,
  validateCommerceForm,
  type CommerceFieldErrors,
  type CommerceFieldName,
  type CommerceFormValues,
} from '../commerce-settings-validation';
import { getErrorMessage } from '../../../shared/error-message';
import './commerce-settings.css';

const COMMERCE_SETTINGS_PERMISSION = 'commerce-settings:manage';

const SAVED_MESSAGE = 'Đã lưu cấu hình bán hàng.';

// Hai ô này là số tiền VND và dùng ô nhập tiền chung. Các ô còn lại là số đếm hoặc chuỗi.
const MONEY_FIELDS: readonly CommerceFieldName[] = ['freeShippingThreshold', 'codMaxAmount'];

// Id ổn định cho từng ô, để nhãn, gợi ý, lỗi và E2E nối với đúng ô.
const FIELD_IDS: Record<CommerceFieldName, string> = {
  freeShippingThreshold: 'commerce-free-shipping-threshold',
  codMaxAmount: 'commerce-cod-max-amount',
  maxQuantityPerSku: 'commerce-max-quantity-per-sku',
  maxCartLines: 'commerce-max-cart-lines',
  sepayAccountNumber: 'commerce-sepay-account-number',
  sepayBankCode: 'commerce-sepay-bank-code',
  shopName: 'commerce-shop-name',
  shopAddress: 'commerce-shop-address',
  shopPhone: 'commerce-shop-phone',
  shopTaxCode: 'commerce-shop-tax-code',
};

// Chọn bàn phím số hoặc điện thoại cho các ô cần thiết. Mã số thuế dùng mặc định `text` để iOS nhập được dấu `-`.
const FIELD_INPUT_MODES: Partial<
  Record<CommerceFieldName, InputHTMLAttributes<HTMLInputElement>['inputMode']>
> = {
  maxQuantityPerSku: 'numeric',
  maxCartLines: 'numeric',
  sepayAccountNumber: 'numeric',
  shopPhone: 'tel',
};

// Giới hạn độ dài ở ô khớp contract backend. Ô mã số thuế dùng 20 ký tự để chứa đủ 13 chữ số kèm dấu gạch ngang.
const FIELD_MAX_LENGTHS: Partial<Record<CommerceFieldName, number>> = {
  shopName: SHOP_NAME_MAX_LENGTH,
  shopAddress: SHOP_ADDRESS_MAX_LENGTH,
  shopPhone: SHOP_PHONE_MAX_LENGTH,
  shopTaxCode: SHOP_TAX_CODE_MAX_LENGTH,
};

// Màn cấu hình giao hàng, hạn mức COD, giỏ hàng, tài khoản nhận tiền và thông tin in hóa đơn.
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
            Ngưỡng miễn phí giao hàng, hạn mức COD, giới hạn giỏ hàng, tài khoản nhận chuyển khoản
            và thông tin in trên hóa đơn.
          </p>
        </div>
      </div>

      {settingsQuery.isError && (
        <ErrorAlert
          message={getErrorMessage(settingsQuery.error, 'Không tải được cấu hình bán hàng.')}
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

// Giá trị `null` ở backend thành chuỗi rỗng trong ô nhập, vì thẻ input chỉ giữ chuỗi.
function toFormValues(settings: CommerceSettings): CommerceFormValues {
  return {
    freeShippingThreshold: String(settings.freeShippingThreshold),
    codMaxAmount: String(settings.codMaxAmount),
    maxQuantityPerSku: String(settings.maxQuantityPerSku),
    maxCartLines: String(settings.maxCartLines),
    sepayAccountNumber: settings.sepayAccountNumber ?? '',
    sepayBankCode: settings.sepayBankCode ?? '',
    shopName: settings.shop.name ?? '',
    shopAddress: settings.shop.address ?? '',
    shopPhone: settings.shop.phone ?? '',
    shopTaxCode: settings.shop.taxCode ?? '',
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
      // Chỉ gửi bảy khóa backend nhận. Khóa webhook SePay nằm ở biến môi trường, không đi qua màn này.
      const saved = await saveCommerceSettings({
        freeShippingThreshold: Number(values.freeShippingThreshold.trim()),
        codMaxAmount: Number(values.codMaxAmount.trim()),
        maxQuantityPerSku: Number(values.maxQuantityPerSku.trim()),
        maxCartLines: Number(values.maxCartLines.trim()),
        sepayAccountNumber: emptyToNull(values.sepayAccountNumber),
        sepayBankCode: emptyToNull(values.sepayBankCode),
        shop: {
          name: emptyToNull(values.shopName),
          address: emptyToNull(values.shopAddress),
          phone: emptyToNull(values.shopPhone),
          taxCode: emptyToNull(values.shopTaxCode),
        },
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
        setFormErrors([getErrorMessage(error, 'Không lưu được cấu hình.')]);
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  // Gợi ý dưới ô tiền chỉ nêu khoảng hợp lệ. Số đang nhập đã hiện trong ô tiền theo định dạng VND.
  const moneyHint = `Từ 0 đến ${formatCount(MONEY_MAX_AMOUNT)} đ.`;

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
            label="Ngưỡng miễn phí giao hàng"
            hint={moneyHint}
            values={values}
            errors={fieldErrors}
            disabled={isSubmitting}
            onChange={changeField}
          />
          <SettingsField
            field="codMaxAmount"
            label="Hạn mức thanh toán COD"
            hint={moneyHint}
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

        <div className="card-head commerce-settings-section">
          <h2>Thông tin in trên hóa đơn</h2>
        </div>
        <p className="muted commerce-settings-note">
          Thông tin này in ở đầu hóa đơn. Ô để trống thì dòng đó không in, tên shop để trống thì hóa
          đơn in Pickory.
        </p>
        <div className="form-grid form-grid-2">
          <SettingsField
            field="shopName"
            label="Tên shop"
            hint={`Tối đa ${SHOP_NAME_MAX_LENGTH} ký tự. Để trống thì hóa đơn in Pickory.`}
            values={values}
            errors={fieldErrors}
            disabled={isSubmitting}
            onChange={changeField}
          />
          <SettingsField
            field="shopAddress"
            label="Địa chỉ"
            hint={`Tối đa ${SHOP_ADDRESS_MAX_LENGTH} ký tự.`}
            values={values}
            errors={fieldErrors}
            disabled={isSubmitting}
            onChange={changeField}
          />
          <SettingsField
            field="shopPhone"
            label="Số điện thoại"
            hint={`Tối đa ${SHOP_PHONE_MAX_LENGTH} ký tự. Gồm chữ số, dấu cách, +, ., -.`}
            values={values}
            errors={fieldErrors}
            disabled={isSubmitting}
            onChange={changeField}
          />
          <SettingsField
            field="shopTaxCode"
            label="Mã số thuế"
            hint="10 hoặc 13 chữ số. Có thể dùng dấu - để ngăn nhóm."
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
  // Có lỗi thì trình đọc màn hình đọc câu lỗi. Không có lỗi thì đọc gợi ý giới hạn.
  const describedById = error ? `${inputId}-error` : hintId;

  return (
    <div className="field">
      <label htmlFor={inputId}>{label}</label>
      {MONEY_FIELDS.includes(field) ? (
        <MoneyInput
          id={inputId}
          disabled={disabled}
          // Ô trống là chuỗi rỗng để luật kiểm báo thiếu số, thay vì lưu thành số 0.
          value={parseVndInput(values[field])}
          onChange={(amount) => onChange(field, amount === null ? '' : String(amount))}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedById}
        />
      ) : (
        <input
          id={inputId}
          type="text"
          // Dùng bàn phím chữ mặc định khi ô không khai báo kiểu riêng, để gõ được chữ cái và dấu gạch ngang.
          inputMode={FIELD_INPUT_MODES[field] ?? 'text'}
          // Chặn nhập vượt giới hạn ngay trên ô theo hợp đồng của từng trường.
          maxLength={FIELD_MAX_LENGTHS[field]}
          autoComplete="off"
          disabled={disabled}
          value={values[field]}
          onChange={(event) => onChange(field, event.target.value)}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedById}
        />
      )}
      <FieldError inputId={inputId} message={error} />
      <span id={hintId} className="commerce-settings-hint">
        {hint}
      </span>
    </div>
  );
}
