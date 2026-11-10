import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { ApiError } from '../../../shared/api';
import { ErrorAlert } from '../../../shared/error-alert';
import { getErrorMessage } from '../../../shared/error-message';
import {
  FieldError,
  fieldErrorProps,
  fieldPropsWithHint,
  hasFieldErrors,
} from '../../../shared/field-error';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { Icon } from '../../../shared/icons';
import { formatVietnamDateTime } from '../../../shared/vietnam-time';
import { useCan } from '../../identity/hooks/use-actor';
import { CannedResponsePanel } from './canned-response-panel';
import { sortHolidays } from './holiday-dates';
import { HOLIDAY_INPUT_ID, HolidayPicker } from './holiday-picker';
import {
  fetchSupportSettings,
  saveSupportSettings,
  SUPPORT_SETTINGS_QUERY_KEY,
} from './support-desk-api';
import type { SupportSettings } from './support-desk-schemas';
import {
  mapSupportSettingsServerErrors,
  SUPPORT_SETTINGS_FIELD_ORDER,
  validateSupportSettings,
  type SupportSettingsFieldErrors,
  type SupportSettingsFieldName,
  type SupportSettingsFormValues,
} from './support-settings-validation';
import './support-settings.css';

const SUPPORT_SETTINGS_PERMISSION = 'support-settings:manage';
const REPLY_PERMISSION = 'conversations:reply';

// Id ổn định cho từng ô, để nhãn, lỗi và E2E nối với đúng ô.
const FIELD_IDS: Record<SupportSettingsFieldName, string> = {
  opensAt: 'support-settings-opens-at',
  closesAt: 'support-settings-closes-at',
  waitNudgeMinutes: 'support-settings-wait-nudge',
  autoCloseMinutes: 'support-settings-auto-close',
};
const WAIT_NUDGE_HINT_ID = 'support-settings-wait-nudge-hint';
const AUTO_CLOSE_HINT_ID = 'support-settings-auto-close-hint';
const SAVE_BUTTON_ID = 'support-settings-save';

const SAVED_MESSAGE = 'Đã lưu cấu hình.';
const PENDING_DAY_ERROR = 'Bấm Thêm ngày nghỉ để thêm ngày này, hoặc xóa ô ngày.';
const DEFAULT_SAVE_ERROR = 'Chưa lưu được cấu hình. Hãy thử lại.';
const LOAD_ERROR = 'Không tải được cấu hình chăm sóc khách hàng.';

// Màn cấu hình chăm sóc khách hàng: giờ làm việc, ngày nghỉ, mốc thời gian của phiên và mẫu dùng chung.
export function SupportSettingsPage() {
  const canManage = useCan(SUPPORT_SETTINGS_PERMISSION);

  // Thiếu quyền thì không gọi API, vì backend chắc chắn trả 403.
  // Tắt tải lại khi focus cửa sổ để dữ liệu đang sửa dở không bị ghi đè.
  const settingsQuery = useQuery({
    queryKey: SUPPORT_SETTINGS_QUERY_KEY,
    queryFn: fetchSupportSettings,
    enabled: canManage,
    refetchOnWindowFocus: false,
  });
  // Route mẫu trả lời cần quyền `conversations:reply`, độc lập với quyền cấu hình.
  const canUseTemplates = useCan(REPLY_PERMISSION);

  // Hiện thông báo thiếu quyền khi mở thẳng URL, vì menu chỉ ẩn mục chứ không chặn đường dẫn.
  if (!canManage) {
    return (
      <ForbiddenNotice
        permission={SUPPORT_SETTINGS_PERMISSION}
        subject="cấu hình chăm sóc khách hàng"
      />
    );
  }

  return (
    <div className="page support-settings-page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Cấu hình CSKH</h1>
          <p>Giờ làm việc, ngày nghỉ, mốc thời gian của phiên chat và mẫu trả lời dùng chung.</p>
        </div>
      </div>

      {settingsQuery.isError && (
        <div className="support-settings-load-error">
          <ErrorAlert message={getErrorMessage(settingsQuery.error, LOAD_ERROR)} />
          <button
            type="button"
            className="btn btn-outline"
            disabled={settingsQuery.isFetching}
            onClick={() => void settingsQuery.refetch()}
          >
            Thử lại
          </button>
        </div>
      )}

      {settingsQuery.isLoading && (
        <p role="status" className="empty-state">
          Đang tải cấu hình...
        </p>
      )}

      {settingsQuery.data && <SupportSettingsForm initialSettings={settingsQuery.data} />}

      <section className="card" aria-labelledby="support-shared-canned-title">
        <div className="card-head">
          <h2 id="support-shared-canned-title">Mẫu trả lời dùng chung</h2>
        </div>
        <CannedResponsePanel scope="shared" canUse={canUseTemplates} />
      </section>
    </div>
  );
}

function toFormValues(settings: SupportSettings): SupportSettingsFormValues {
  return {
    opensAt: settings.opensAt,
    closesAt: settings.closesAt,
    waitNudgeMinutes: String(settings.waitNudgeMinutes),
    autoCloseMinutes: String(settings.autoCloseMinutes),
  };
}

interface SupportSettingsFormProps {
  initialSettings: SupportSettings;
}

function SupportSettingsForm({ initialSettings }: SupportSettingsFormProps) {
  const queryClient = useQueryClient();
  const [values, setValues] = useState<SupportSettingsFormValues>(toFormValues(initialSettings));
  const [holidays, setHolidays] = useState<string[]>(sortHolidays(initialSettings.holidays));
  const [fieldErrors, setFieldErrors] = useState<SupportSettingsFieldErrors>({});
  // Lỗi không gắn được vào ô nào, hiện bằng `ErrorAlert` đầu form.
  const [formErrors, setFormErrors] = useState<string[]>([]);
  // Thời điểm máy chủ ghi cấu hình lần cuối. `null` nghĩa là chưa có lần lưu nào đang hiện.
  const [savedAt, setSavedAt] = useState<string | null>(null);
  // Ngày đang chọn trong ô ngày nhưng chưa thêm vào danh sách, và lỗi dưới ô ngày.
  const [pickedDay, setPickedDay] = useState('');
  const [dayError, setDayError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  // Cờ đồng bộ chặn bấm đôi, vì `isSubmitting` chỉ đổi sau lần vẽ lại kế tiếp.
  const isSubmittingRef = useRef(false);
  // Ô sẽ nhận focus khi form hết khóa. Ô đang khóa không nhận focus được.
  const pendingFocusIdRef = useRef<string | null>(null);

  useEffect(() => {
    if (isSubmitting || pendingFocusIdRef.current === null) return;
    document.getElementById(pendingFocusIdRef.current)?.focus();
    pendingFocusIdRef.current = null;
  }, [isSubmitting]);

  function updateValue(field: SupportSettingsFieldName, value: string) {
    setValues((current) => ({ ...current, [field]: value }));
    // Lỗi cũ nói về giá trị cũ, nên bỏ lỗi của ô vừa sửa.
    setFieldErrors((current) => ({ ...current, [field]: null }));
    // Giá trị đã đổi nên thông báo "đã lưu" không còn đúng.
    setSavedAt(null);
  }

  function updatePickedDay(next: string) {
    setPickedDay(next);
    setSavedAt(null);
  }

  function updateHolidays(next: string[]) {
    setHolidays(next);
    setSavedAt(null);
  }

  function focusFirstInvalidField(errors: SupportSettingsFieldErrors) {
    const firstInvalid = SUPPORT_SETTINGS_FIELD_ORDER.find((field) => errors[field]);
    if (firstInvalid) document.getElementById(FIELD_IDS[firstInvalid])?.focus();
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    // Phím Enter trong ô nhập vẫn gửi form khi nút đã khóa. Chặn tại đây để không gửi hai lần.
    if (isSubmittingRef.current) return;

    setSavedAt(null);
    setFormErrors([]);

    // Kiểm phía client trước. Còn lỗi thì không gửi request, để lỗi hiện ngay dưới đúng ô.
    const clientErrors = validateSupportSettings(values);
    setFieldErrors(clientErrors);
    // Ngày đã chọn mà chưa bấm "Thêm ngày nghỉ" sẽ không vào body. Chặn lưu để người dùng không tưởng ngày đó đã lưu.
    const hasPendingDay = pickedDay !== '';
    if (hasPendingDay) setDayError(PENDING_DAY_ERROR);
    // Ô số sai được focus trước. Hết lỗi ô số thì nhánh sau đưa focus về ô ngày chưa thêm.
    if (hasFieldErrors(clientErrors)) {
      focusFirstInvalidField(clientErrors);
      return;
    }
    if (hasPendingDay) {
      document.getElementById(HOLIDAY_INPUT_ID)?.focus();
      return;
    }

    isSubmittingRef.current = true;
    setIsSubmitting(true);
    try {
      // Gửi cả cấu hình bằng một `PUT`. Máy chủ tự đặt `updatedAt`, nên body không có trường này.
      const saved = await saveSupportSettings({
        opensAt: values.opensAt,
        closesAt: values.closesAt,
        holidays,
        waitNudgeMinutes: Number(values.waitNudgeMinutes),
        autoCloseMinutes: Number(values.autoCloseMinutes),
      });

      // Cập nhật cache để lần mở lại màn trong phiên này hiện dữ liệu vừa lưu.
      queryClient.setQueryData(SUPPORT_SETTINGS_QUERY_KEY, saved);
      // Form nhận lại dữ liệu từ response để hiện đúng giá trị máy chủ đã lưu.
      setValues(toFormValues(saved));
      setHolidays(sortHolidays(saved.holidays));
      setFieldErrors({});
      setSavedAt(saved.updatedAt);
      // Nút lưu bị khóa lúc gửi nên mất focus. Trả focus về nút để bàn phím không rơi về đầu trang.
      pendingFocusIdRef.current = SAVE_BUTTON_ID;
    } catch (error) {
      if (error instanceof ApiError && error.status === 400) {
        // Lỗi 400 gắn vào đúng ô. Phần không gắn được vào ô nào hiện đầu form.
        const mapped = mapSupportSettingsServerErrors(error);
        setFieldErrors(mapped.fieldErrors);
        setFormErrors(mapped.unmatched);
        const firstInvalid = SUPPORT_SETTINGS_FIELD_ORDER.find(
          (field) => mapped.fieldErrors[field],
        );
        pendingFocusIdRef.current = firstInvalid ? FIELD_IDS[firstInvalid] : SAVE_BUTTON_ID;
      } else {
        // Lỗi khác (403, 503, mạng) hiện đầu form. Dữ liệu đã nhập giữ nguyên để thử lại.
        setFormErrors([getErrorMessage(error, DEFAULT_SAVE_ERROR)]);
        pendingFocusIdRef.current = SAVE_BUTTON_ID;
      }
    } finally {
      isSubmittingRef.current = false;
      setIsSubmitting(false);
    }
  }

  return (
    <form noValidate className="support-settings-form" onSubmit={handleSubmit}>
      {formErrors.length > 0 && <ErrorAlert message={formErrors[0]} items={formErrors.slice(1)} />}

      <section className="card" aria-labelledby="support-hours-title">
        <div className="card-head">
          <h2 id="support-hours-title">Giờ làm việc</h2>
        </div>
        <div className="form-grid form-grid-2">
          <div className="field">
            <label htmlFor={FIELD_IDS.opensAt}>Giờ mở cửa</label>
            <input
              id={FIELD_IDS.opensAt}
              type="time"
              disabled={isSubmitting}
              value={values.opensAt}
              onChange={(event) => updateValue('opensAt', event.target.value)}
              {...fieldErrorProps(FIELD_IDS.opensAt, fieldErrors.opensAt)}
            />
            <FieldError inputId={FIELD_IDS.opensAt} message={fieldErrors.opensAt} />
          </div>
          <div className="field">
            <label htmlFor={FIELD_IDS.closesAt}>Giờ đóng cửa</label>
            <input
              id={FIELD_IDS.closesAt}
              type="time"
              disabled={isSubmitting}
              value={values.closesAt}
              onChange={(event) => updateValue('closesAt', event.target.value)}
              {...fieldErrorProps(FIELD_IDS.closesAt, fieldErrors.closesAt)}
            />
            <FieldError inputId={FIELD_IDS.closesAt} message={fieldErrors.closesAt} />
          </div>
        </div>
        <p className="support-settings-note">Giờ theo múi giờ Việt Nam.</p>
      </section>

      <section className="card" aria-labelledby="support-holidays-title">
        <div className="card-head">
          <h2 id="support-holidays-title">Ngày nghỉ</h2>
        </div>
        <HolidayPicker
          holidays={holidays}
          pickedDay={pickedDay}
          dayError={dayError}
          disabled={isSubmitting}
          onPickedDayChange={updatePickedDay}
          onDayErrorChange={setDayError}
          onChange={updateHolidays}
        />
      </section>

      <section className="card" aria-labelledby="support-thresholds-title">
        <div className="card-head">
          <h2 id="support-thresholds-title">Mốc thời gian của phiên</h2>
        </div>
        <div className="form-grid form-grid-2">
          <div className="field">
            <label htmlFor={FIELD_IDS.waitNudgeMinutes}>Nhắc lại phiên chờ sau (phút)</label>
            <input
              id={FIELD_IDS.waitNudgeMinutes}
              type="number"
              inputMode="numeric"
              min={1}
              max={240}
              step={1}
              disabled={isSubmitting}
              value={values.waitNudgeMinutes}
              onChange={(event) => updateValue('waitNudgeMinutes', event.target.value)}
              {...fieldPropsWithHint(
                FIELD_IDS.waitNudgeMinutes,
                WAIT_NUDGE_HINT_ID,
                fieldErrors.waitNudgeMinutes,
              )}
            />
            <FieldError
              inputId={FIELD_IDS.waitNudgeMinutes}
              message={fieldErrors.waitNudgeMinutes}
            />
            <p id={WAIT_NUDGE_HINT_ID} className="support-settings-note">
              Phiên chờ quá số phút này thì hệ thống nhắc nhân viên nhận phiên. Giá trị từ 1 đến
              240.
            </p>
          </div>
          <div className="field">
            <label htmlFor={FIELD_IDS.autoCloseMinutes}>Tự đóng phiên im lặng sau (phút)</label>
            <input
              id={FIELD_IDS.autoCloseMinutes}
              type="number"
              inputMode="numeric"
              min={1}
              max={240}
              step={1}
              disabled={isSubmitting}
              value={values.autoCloseMinutes}
              onChange={(event) => updateValue('autoCloseMinutes', event.target.value)}
              {...fieldPropsWithHint(
                FIELD_IDS.autoCloseMinutes,
                AUTO_CLOSE_HINT_ID,
                fieldErrors.autoCloseMinutes,
              )}
            />
            <FieldError
              inputId={FIELD_IDS.autoCloseMinutes}
              message={fieldErrors.autoCloseMinutes}
            />
            <p id={AUTO_CLOSE_HINT_ID} className="support-settings-note">
              Phiên không có tin mới quá số phút này thì hệ thống đóng phiên. Giá trị từ 1 đến 240.
            </p>
          </div>
        </div>
      </section>

      <div className="support-settings-save">
        <button
          id={SAVE_BUTTON_ID}
          type="submit"
          className="btn btn-primary"
          disabled={isSubmitting}
        >
          {isSubmitting ? 'Đang lưu...' : 'Lưu cấu hình'}
        </button>
        {/* Vùng thông báo luôn có trong DOM để trình đọc màn hình đọc được nội dung mới. */}
        <div role="status" className="support-settings-status">
          {savedAt !== null && (
            <>
              <span className="support-settings-saved">
                <Icon name="check" className="icon" />
                {SAVED_MESSAGE}
              </span>
              <span className="support-settings-saved-at">
                Cập nhật lần cuối {formatVietnamDateTime(savedAt)}
              </span>
            </>
          )}
        </div>
      </div>
    </form>
  );
}
