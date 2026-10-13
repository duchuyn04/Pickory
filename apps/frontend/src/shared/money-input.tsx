import { useId, useLayoutEffect, useRef, type ChangeEvent, type InputHTMLAttributes } from 'react';
import { formatVndInput, reformatVndInput } from './money-input-format';
import './money-input.css';

// Chỉ nhận các thuộc tính form mà các màn tiền đang dùng. Giá trị và sự kiện đổi do component này quản lý.
type MoneyInputAttributes = Pick<
  InputHTMLAttributes<HTMLInputElement>,
  | 'id'
  | 'name'
  | 'disabled'
  | 'required'
  | 'placeholder'
  | 'aria-label'
  | 'aria-invalid'
  | 'aria-describedby'
>;

interface MoneyInputProps extends MoneyInputAttributes {
  // Số đồng nguyên. `null` là ô trống.
  value: number | null;
  onChange: (value: number | null) => void;
  // `start` là căn trái như ô nhập khác. `end` căn phải để các số tiền trong cột bảng thẳng hàng.
  align?: 'start' | 'end';
}

// Ô nhập số tiền VND: hiện dấu chấm nghìn và đơn vị `đ`, nhưng form cha chỉ thấy số nguyên.
export function MoneyInput({
  value,
  onChange,
  align = 'start',
  'aria-describedby': describedBy,
  ...inputAttributes
}: MoneyInputProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  // Vị trí con trỏ cần đặt sau lần vẽ lại. `null` nghĩa là không có việc chờ.
  const pendingCaretRef = useRef<number | null>(null);
  const unitId = useId();

  const displayText = formatVndInput(value);

  function applyPendingCaret() {
    const input = inputRef.current;
    const caret = pendingCaretRef.current;
    pendingCaretRef.current = null;
    if (input === null || caret === null) return;
    // Chỉ đặt con trỏ khi ô đang được focus. Nếu không, một số trình duyệt sẽ kéo focus về ô này.
    if (document.activeElement !== input) return;
    input.setSelectionRange(caret, caret);
  }

  // React ghi lại chuỗi đã định dạng vào ô và đẩy con trỏ về cuối. Đặt lại con trỏ ngay sau lần vẽ đó.
  useLayoutEffect(applyPendingCaret);

  function handleChange(event: ChangeEvent<HTMLInputElement>) {
    const rawText = event.target.value;
    const rawCaret = event.target.selectionStart ?? rawText.length;
    const result = reformatVndInput(rawText, rawCaret);

    if (result === null) {
      // Số quá dài bị từ chối và ô giữ giá trị cũ. Con trỏ lùi theo số ký tự vừa chèn để không nhảy về cuối.
      const insertedLength = rawText.length - displayText.length;
      pendingCaretRef.current = Math.min(
        Math.max(rawCaret - insertedLength, 0),
        displayText.length,
      );
    } else {
      pendingCaretRef.current = result.caret;
      // Form cha chỉ nhận số. Chuỗi đã định dạng chỉ tồn tại trong ô.
      onChange(result.value);
    }

    // Nếu không có lần vẽ lại (giá trị không đổi), React vẫn trả ô về chuỗi cũ. Đặt con trỏ sau đó bằng microtask.
    queueMicrotask(applyPendingCaret);
  }

  // Đơn vị nằm sau câu lỗi trong `aria-describedby`, nên trình đọc màn hình đọc lỗi trước rồi mới đọc đơn vị.
  const describedByIds = [describedBy, unitId].filter(Boolean).join(' ');

  return (
    // Khung dùng thẻ `div`: luật `.field > span` của design system sẽ đổi cỡ chữ và độ đậm nếu khung là `span`.
    <div className={align === 'end' ? 'money-input money-input-end' : 'money-input'}>
      <input
        {...inputAttributes}
        ref={inputRef}
        className="money-input-field"
        type="text"
        // Bàn phím số trên điện thoại. Kiểu `text` cho phép dán chuỗi có dấu chấm và đơn vị.
        inputMode="numeric"
        autoComplete="off"
        value={displayText}
        onChange={handleChange}
        aria-describedby={describedByIds}
      />
      {/* Hậu tố chỉ để nhìn. Trình đọc màn hình đọc đơn vị từ phần tử ẩn bên dưới. */}
      <span className="money-input-suffix" aria-hidden="true">
        đ
      </span>
      <span id={unitId} className="sr">
        đồng
      </span>
    </div>
  );
}
