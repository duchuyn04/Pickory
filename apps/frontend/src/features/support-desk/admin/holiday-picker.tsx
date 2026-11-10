import { useRef, type KeyboardEvent } from 'react';
import { FieldError, fieldErrorProps } from '../../../shared/field-error';
import { Icon } from '../../../shared/icons';
import { addHoliday, formatIsoDay, removeHoliday } from './holiday-dates';

export const HOLIDAY_INPUT_ID = 'support-settings-holiday-input';

interface HolidayPickerProps {
  // Danh sách ngày nghỉ `yyyy-MM-dd`, đã xếp tăng dần.
  holidays: readonly string[];
  // Ngày đang chọn trong ô ngày nhưng chưa thêm. Form cha giữ giá trị này để chặn lưu khi ngày chưa được thêm.
  pickedDay: string;
  dayError: string | null;
  disabled: boolean;
  onPickedDayChange: (next: string) => void;
  onDayErrorChange: (next: string | null) => void;
  onChange: (next: string[]) => void;
}

// Chọn ngày nghỉ bằng ô ngày và xóa từng ngày. Danh sách, ô ngày và lỗi đều thuộc về form cha.
export function HolidayPicker({
  holidays,
  pickedDay,
  dayError,
  disabled,
  onPickedDayChange,
  onDayErrorChange,
  onChange,
}: HolidayPickerProps) {
  const dayInputRef = useRef<HTMLInputElement>(null);

  function handleAdd() {
    const result = addHoliday(holidays, pickedDay);
    // Ngày trống, sai hoặc trùng thì giữ nguyên danh sách và hiện lỗi dưới ô ngày.
    if (!result.ok) {
      onDayErrorChange(result.error);
      return;
    }
    onDayErrorChange(null);
    // Xóa ô ngày để lần thêm kế tiếp bắt đầu từ ô trống, tránh thêm lại cùng ngày do bấm nhầm.
    onPickedDayChange('');
    onChange(result.holidays);
  }

  function handleRemove(isoDay: string) {
    onChange(removeHoliday(holidays, isoDay));
    // Nút vừa bấm biến mất khỏi DOM, nên đưa focus về ô ngày để bàn phím không rơi về đầu trang.
    dayInputRef.current?.focus();
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key !== 'Enter') return;
    // Enter trong ô ngày thêm ngày nghỉ. Mặc định của form là gửi cả cấu hình, ngoài ý người dùng.
    event.preventDefault();
    handleAdd();
  }

  return (
    <>
      <div className="field">
        <label htmlFor={HOLIDAY_INPUT_ID}>Thêm ngày nghỉ</label>
        <div className="holiday-add">
          <input
            ref={dayInputRef}
            id={HOLIDAY_INPUT_ID}
            type="date"
            // Chặn năm có hơn bốn chữ số, vì hợp đồng chỉ nhận `yyyy-MM-dd`.
            max="9999-12-31"
            disabled={disabled}
            value={pickedDay}
            onChange={(event) => {
              onPickedDayChange(event.target.value);
              onDayErrorChange(null);
            }}
            onKeyDown={handleKeyDown}
            {...fieldErrorProps(HOLIDAY_INPUT_ID, dayError)}
          />
          <button type="button" className="btn btn-outline" disabled={disabled} onClick={handleAdd}>
            Thêm ngày nghỉ
          </button>
        </div>
        <FieldError inputId={HOLIDAY_INPUT_ID} message={dayError} />
      </div>

      {holidays.length === 0 ? (
        <p className="holiday-empty">Chưa có ngày nghỉ nào.</p>
      ) : (
        <ul className="holiday-list" aria-label="Ngày nghỉ đã chọn">
          {holidays.map((isoDay) => {
            const label = formatIsoDay(isoDay);
            return (
              <li key={isoDay} className="holiday-item">
                <span className="holiday-date">{label}</span>
                <button
                  type="button"
                  className="icon-btn holiday-remove"
                  aria-label={`Xóa ngày nghỉ ${label}`}
                  disabled={disabled}
                  onClick={() => handleRemove(isoDay)}
                >
                  <Icon name="close" className="icon" />
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </>
  );
}
