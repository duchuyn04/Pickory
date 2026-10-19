import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { ApiError } from '../../../shared/api';
import { ErrorAlert } from '../../../shared/error-alert';
import { FieldError, hasFieldErrors } from '../../../shared/field-error';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { MoneyInput } from '../../../shared/money-input';
import { formatVndInput, parseVndInput } from '../../../shared/money-input-format';
import { useCan } from '../../identity/hooks/use-actor';
import {
  fallbackFeeQueryKey,
  fetchFallbackFee,
  saveFallbackFee,
  type FallbackFee,
  type FallbackFeeFieldName,
  type FallbackFeeTier,
} from '../fallback-fee-api';
import { SAMPLE_PARCEL, calculateFee, chargeableWeightGrams } from '../fallback-fee-calculator';
import {
  MAX_BASE_WEIGHT_GRAMS,
  MAX_FEE,
  MIN_BASE_WEIGHT_GRAMS,
  mapServerErrors,
  parseFieldValue,
  toFormValues,
  toPayload,
  validateFallbackFeeForm,
  type FallbackFeeFieldErrors,
  type FallbackFeeFormValues,
} from '../fallback-fee-validation';
import './fallback-fee.css';

const SHIPPING_SETTINGS_PERMISSION = 'shipping-settings:manage';

const SAVED_MESSAGE = 'Đã lưu phí ship dự phòng.';

const NOT_ENOUGH_PRICES_TEXT = 'Chưa đủ giá để tính';

const ENABLED_ID = 'fallback-fee-enabled';
const EXAMPLE_ID = 'fallback-fee-example';

interface TierView {
  tier: FallbackFeeTier;
  title: string;
  description: string;
}

// Ba tuyến theo thứ tự hiện trên màn. Câu giải thích giúp admin biết tuyến nào áp cho đơn nào.
const TIER_VIEWS: readonly TierView[] = [
  { tier: 'sameProvince', title: 'Nội tỉnh', description: 'Cùng tỉnh với địa chỉ lấy hàng.' },
  {
    tier: 'sameRegion',
    title: 'Nội vùng',
    description: 'Cùng vùng (Bắc, Trung hoặc Nam), khác tỉnh.',
  },
  { tier: 'crossRegion', title: 'Liên vùng', description: 'Khác vùng với địa chỉ lấy hàng.' },
];

// `sameProvinceBaseFee` thành `same-province-base-fee`, để id ô và id ví dụ cùng một kiểu viết với các màn khác.
function toKebabCase(name: string): string {
  return name.replace(/[A-Z]/g, (letter) => `-${letter.toLowerCase()}`);
}

// Id ổn định cho từng ô, để nhãn, gợi ý, lỗi và E2E nối với đúng ô.
function fieldId(field: FallbackFeeFieldName): string {
  return `fallback-fee-${toKebabCase(field)}`;
}

function formatGrams(grams: number): string {
  return `${formatVndInput(grams)} g`;
}

function formatMoney(amount: number): string {
  return `${formatVndInput(amount)} đ`;
}

// Màn cấu hình bảng phí dùng khi GHN không phản hồi lúc báo phí.
export function FallbackFeePage() {
  const canManage = useCan(SHIPPING_SETTINGS_PERMISSION);

  // Thiếu quyền thì không gọi API, vì backend chắc chắn trả 403.
  // Tắt tải lại khi focus cửa sổ để dữ liệu đang sửa dở không bị ghi đè.
  const feeQuery = useQuery({
    queryKey: fallbackFeeQueryKey,
    queryFn: fetchFallbackFee,
    enabled: canManage,
    refetchOnWindowFocus: false,
  });

  // Hiện thông báo thiếu quyền khi mở thẳng URL, vì menu chỉ ẩn mục chứ không chặn đường dẫn.
  if (!canManage) {
    return (
      <ForbiddenNotice permission={SHIPPING_SETTINGS_PERMISSION} subject="phí ship dự phòng" />
    );
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Phí ship dự phòng</h1>
          <p>
            Shop dùng bảng phí này khi GHN không phản hồi lúc báo phí. Khách thấy phí như bình
            thường.
          </p>
        </div>
      </div>

      {feeQuery.isError && (
        <ErrorAlert
          message={
            feeQuery.error instanceof Error && feeQuery.error.message
              ? feeQuery.error.message
              : 'Không tải được phí ship dự phòng.'
          }
        />
      )}

      {feeQuery.isLoading && (
        <p role="status" className="empty-state">
          Đang tải phí ship dự phòng...
        </p>
      )}

      {feeQuery.data && <FallbackFeeForm initialFee={feeQuery.data} />}
    </div>
  );
}

interface FallbackFeeFormProps {
  initialFee: FallbackFee;
}

function FallbackFeeForm({ initialFee }: FallbackFeeFormProps) {
  const queryClient = useQueryClient();
  const [enabled, setEnabled] = useState(initialFee.enabled);
  const [values, setValues] = useState<FallbackFeeFormValues>(toFormValues(initialFee));
  const [fieldErrors, setFieldErrors] = useState<FallbackFeeFieldErrors>({});
  // Lỗi không gắn được vào ô nào, hiện bằng `ErrorAlert` đầu form.
  const [formErrors, setFormErrors] = useState<string[]>([]);
  const [isSaved, setIsSaved] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Sửa ô nào thì lỗi của ô đó hết hiệu lực, vì lỗi cũ nói về giá trị cũ.
  function changeField(field: FallbackFeeFieldName, text: string) {
    setValues((current) => ({ ...current, [field]: text }));
    setFieldErrors((current) => {
      const next = { ...current };
      delete next[field];
      return next;
    });
    setIsSaved(false);
  }

  // Lỗi thiếu giá trị cũ hết đúng sau khi đổi công tắc. Xóa lỗi ô để lần lưu sau kiểm lại từ đầu.
  function changeEnabled(nextEnabled: boolean) {
    setEnabled(nextEnabled);
    setFieldErrors({});
    setIsSaved(false);
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    // Nút đã tắt khi đang gửi, nhưng phím Enter trong ô nhập vẫn gửi form. Chặn tại đây để không gửi hai lần.
    if (isSubmitting) return;

    setIsSaved(false);
    setFormErrors([]);

    // Kiểm phía client trước. Còn lỗi thì không gửi request, để lỗi hiện ngay dưới đúng ô.
    const clientErrors = validateFallbackFeeForm(values, enabled);
    setFieldErrors(clientErrors);
    if (hasFieldErrors(clientErrors)) return;

    setIsSubmitting(true);
    try {
      const saved = await saveFallbackFee(toPayload(values, enabled));

      // Form nhận lại dữ liệu server trả về, và cache giữ bản đó cho lần mở lại màn trong phiên này.
      queryClient.setQueryData(fallbackFeeQueryKey, saved);
      setEnabled(saved.enabled);
      setValues(toFormValues(saved));
      setFieldErrors({});
      setIsSaved(true);
    } catch (error) {
      // Lỗi 400 (sai kiểu, sai khoảng) và lỗi 422 (bật mà thiếu giá) đều có `details` theo từng ô.
      if (error instanceof ApiError && (error.status === 400 || error.status === 422)) {
        const mapped = mapServerErrors(error);
        setFieldErrors(mapped.fieldErrors);
        setFormErrors(mapped.unmatched);
      } else {
        // Lỗi khác (403, 503, mạng) hiện đầu form. Dữ liệu đã nhập giữ nguyên để thử lại.
        setFormErrors([
          error instanceof Error ? error.message : 'Không lưu được phí ship dự phòng.',
        ]);
      }
    } finally {
      setIsSubmitting(false);
    }
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
        <div className="fallback-fee-switch">
          <label className="check" htmlFor={ENABLED_ID}>
            <input
              id={ENABLED_ID}
              type="checkbox"
              checked={enabled}
              disabled={isSubmitting}
              onChange={(event) => changeEnabled(event.target.checked)}
            />
            Dùng phí dự phòng khi GHN không phản hồi
          </label>
          <p className="muted small">
            Khi tắt, các ô giá vẫn sửa được để chuẩn bị sẵn. GHN không phản hồi thì báo lỗi như
            trước.
          </p>
        </div>

        {TIER_VIEWS.map((view) => (
          <TierGroup
            key={view.tier}
            view={view}
            values={values}
            errors={fieldErrors}
            disabled={isSubmitting}
            onChange={changeField}
          />
        ))}

        <FeeExample values={values} />

        <div className="head-actions fallback-fee-actions">
          <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
            {isSubmitting ? 'Đang lưu...' : 'Lưu'}
          </button>
        </div>
      </form>
    </>
  );
}

interface TierGroupProps {
  view: TierView;
  values: FallbackFeeFormValues;
  errors: FallbackFeeFieldErrors;
  disabled: boolean;
  onChange: (field: FallbackFeeFieldName, text: string) => void;
}

// Một tuyến: khung có tiêu đề, một câu giải thích tuyến và ba ô giá.
function TierGroup({ view, values, errors, disabled, onChange }: TierGroupProps) {
  const moneyHint = `Từ 0 đến ${formatVndInput(MAX_FEE)} đ.`;
  const weightHint = `Đơn vị gram (g), từ ${MIN_BASE_WEIGHT_GRAMS} đến ${formatVndInput(MAX_BASE_WEIGHT_GRAMS)}.`;

  return (
    <fieldset className="fallback-fee-tier">
      <legend>{view.title}</legend>
      <p className="muted small fallback-fee-tier-note">{view.description}</p>
      <div className="form-grid form-grid-3">
        <FeeField
          field={`${view.tier}BaseFee`}
          label="Giá gốc"
          hint={moneyHint}
          isMoney
          values={values}
          errors={errors}
          disabled={disabled}
          onChange={onChange}
        />
        <FeeField
          field={`${view.tier}BaseWeightGrams`}
          label="Khối lượng gốc"
          hint={weightHint}
          isMoney={false}
          values={values}
          errors={errors}
          disabled={disabled}
          onChange={onChange}
        />
        <FeeField
          field={`${view.tier}StepFee`}
          label="Giá mỗi 500 g tiếp theo"
          hint={moneyHint}
          isMoney
          values={values}
          errors={errors}
          disabled={disabled}
          onChange={onChange}
        />
      </div>
    </fieldset>
  );
}

interface FeeFieldProps {
  field: FallbackFeeFieldName;
  label: string;
  hint: string;
  // Ô tiền dùng ô nhập tiền chung. Ô khối lượng là số nguyên gram.
  isMoney: boolean;
  values: FallbackFeeFormValues;
  errors: FallbackFeeFieldErrors;
  disabled: boolean;
  onChange: (field: FallbackFeeFieldName, text: string) => void;
}

// Một ô nhập có nhãn, gợi ý giới hạn và câu lỗi ngay dưới ô.
function FeeField({
  field,
  label,
  hint,
  isMoney,
  values,
  errors,
  disabled,
  onChange,
}: FeeFieldProps) {
  const inputId = fieldId(field);
  const hintId = `${inputId}-hint`;
  const unitId = `${inputId}-unit`;
  const error = errors[field];
  // Có lỗi thì trình đọc màn hình đọc câu lỗi. Không có lỗi thì đọc gợi ý giới hạn.
  const describedById = error ? `${inputId}-error` : hintId;

  return (
    <div className="field">
      <label htmlFor={inputId}>{label}</label>
      {isMoney ? (
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
        // Khung và chữ "g" dùng lại lớp của `MoneyInput`, để ô khối lượng nằm cùng kiểu với hai ô tiền bên cạnh.
        <div className="money-input">
          <input
            id={inputId}
            className="money-input-field"
            type="text"
            // Ô số mở bàn phím số trên điện thoại. Kiểu ô giữ là chữ để người dùng nhập số âm và thấy lỗi dưới ô.
            inputMode="numeric"
            autoComplete="off"
            disabled={disabled}
            value={values[field]}
            onChange={(event) => onChange(field, event.target.value)}
            aria-invalid={error ? true : undefined}
            // Đơn vị nằm sau câu lỗi hoặc gợi ý, giống cách `MoneyInput` đọc đơn vị `đồng`.
            aria-describedby={`${describedById} ${unitId}`}
          />
          {/* Chữ "g" chỉ để nhìn. Trình đọc màn hình đọc đơn vị từ phần tử ẩn bên dưới. */}
          <span className="money-input-suffix" aria-hidden="true">
            g
          </span>
          <span id={unitId} className="sr">
            gram
          </span>
        </div>
      )}
      <FieldError inputId={inputId} message={error} />
      <span id={hintId} className="fallback-fee-hint">
        {hint}
      </span>
    </div>
  );
}

// Phí của một tuyến cho gói mẫu, hoặc `null` khi một trong ba ô của tuyến chưa nhập hoặc sai khoảng.
function sampleFeeOf(tier: FallbackFeeTier, values: FallbackFeeFormValues): number | null {
  const baseFee = parseFieldValue(`${tier}BaseFee`, values[`${tier}BaseFee`]);
  const baseWeightGrams = parseFieldValue(
    `${tier}BaseWeightGrams`,
    values[`${tier}BaseWeightGrams`],
  );
  const stepFee = parseFieldValue(`${tier}StepFee`, values[`${tier}StepFee`]);
  // Thiếu một giá trị thì không tính nửa chừng, vì con số đó sẽ khiến admin tưởng bảng đã đủ.
  if (baseFee === null || baseWeightGrams === null || stepFee === null) return null;
  return calculateFee({ baseFee, baseWeightGrams, stepFee }, chargeableWeightGrams(SAMPLE_PARCEL));
}

interface FeeExampleProps {
  values: FallbackFeeFormValues;
}

// Ví dụ tính phí cho một gói mẫu cố định, cập nhật theo giá đang nhập.
function FeeExample({ values }: FeeExampleProps) {
  const { weightGrams, lengthCm, widthCm, heightCm } = SAMPLE_PARCEL;
  const chargeable = chargeableWeightGrams(SAMPLE_PARCEL);

  return (
    <section
      id={EXAMPLE_ID}
      className="fallback-fee-example"
      aria-labelledby={`${EXAMPLE_ID}-title`}
    >
      <div className="card-head">
        <h2 id={`${EXAMPLE_ID}-title`}>Ví dụ tính phí</h2>
      </div>
      <p className="muted fallback-fee-example-note">
        Gói mẫu: cân thật {formatGrams(weightGrams)}, hộp {lengthCm} × {widthCm} × {heightCm} cm.
        Khối lượng tính cước là {formatGrams(chargeable)}.
      </p>
      <div className="table-wrap">
        <table className="tbl">
          <thead>
            <tr>
              <th scope="col">Tuyến</th>
              <th scope="col" className="num">
                Phí của gói mẫu
              </th>
            </tr>
          </thead>
          <tbody>
            {TIER_VIEWS.map((view) => {
              const fee = sampleFeeOf(view.tier, values);
              return (
                <tr key={view.tier}>
                  <th scope="row">{view.title}</th>
                  <td id={`${EXAMPLE_ID}-${toKebabCase(view.tier)}`} className="num">
                    {fee === null ? NOT_ENOUGH_PRICES_TEXT : formatMoney(fee)}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </section>
  );
}
