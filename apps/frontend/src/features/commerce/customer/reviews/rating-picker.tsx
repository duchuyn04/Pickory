import { useEffect, useRef } from 'react';
import { FieldError } from '../../../../shared/field-error';
import { StarGlyph } from './star-rating';

const RATING_VALUES = [1, 2, 3, 4, 5];
const RADIO_GROUP_NAME = 'review-rating';
const ERROR_ID_BASE = 'review-rating';

interface RatingPickerProps {
  value: number | null;
  onChange: (rating: number) => void;
  // Câu lỗi của nhóm sao. Có lỗi thì nhóm gắn `aria-describedby` và đưa focus về radio đầu.
  error: string | null | undefined;
  // Tăng sau mỗi lần gửi bị chặn vì thiếu sao, để focus về radio đầu cả khi lỗi cũ chưa đổi.
  focusSignal: number;
  isDisabled: boolean;
}

// Chọn số sao bằng năm radio thật. Phím mũi tên đổi lựa chọn nhờ hành vi gốc của radio.
export function RatingPicker({
  value,
  onChange,
  error,
  focusSignal,
  isDisabled,
}: RatingPickerProps) {
  const firstRadioRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    // Tín hiệu 0 là lúc dựng form, chưa có lần gửi nào để báo lỗi.
    if (focusSignal === 0) return;
    // Đưa focus về radio đầu để người dùng bàn phím biết chỗ cần sửa.
    firstRadioRef.current?.focus();
  }, [focusSignal]);

  return (
    <fieldset
      className="rating-picker"
      disabled={isDisabled}
      aria-describedby={error ? `${ERROR_ID_BASE}-error` : undefined}
    >
      <legend>Số sao</legend>
      <div className="rating-picker-options">
        {RATING_VALUES.map((rating) => (
          <label key={rating} className="rating-option">
            <input
              ref={rating === 1 ? firstRadioRef : undefined}
              type="radio"
              name={RADIO_GROUP_NAME}
              value={rating}
              checked={value === rating}
              aria-label={`${rating} sao`}
              onChange={() => onChange(rating)}
            />
            {/* Sao sáng từ 1 đến mức đang chọn, như cách các cửa hàng khác hiện điểm. */}
            <StarGlyph isFilled={value !== null && rating <= value} />
          </label>
        ))}
      </div>
      <FieldError inputId={ERROR_ID_BASE} message={error} />
    </fieldset>
  );
}
