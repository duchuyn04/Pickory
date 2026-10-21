import { useEffect, useId, useRef, useState } from 'react';
import { Icon } from './icons';

// Thời gian chờ sau lần gõ cuối. Mỗi phím gõ không gửi một request tới API.
const SEARCH_DELAY_MS = 300;

interface SearchInputProps {
  // Nhãn nhìn thấy, đồng thời là tên truy cập của ô.
  label: string;
  // Từ khóa đang áp dụng, thường đọc từ URL. Đổi từ bên ngoài thì ô hiện giá trị mới.
  value: string;
  onSearch: (text: string) => void;
  placeholder?: string;
}

export function SearchInput({ label, value, onSearch, placeholder }: SearchInputProps) {
  const inputId = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [text, setText] = useState(value);
  const [previousValue, setPreviousValue] = useState(value);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const pendingTextRef = useRef(value);
  const onSearchRef = useRef(onSearch);

  // Giữ hàm gọi mới nhất. Lần gọi hẹn giờ không dùng bản cũ của màn cha.
  useEffect(() => {
    onSearchRef.current = onSearch;
  }, [onSearch]);

  // Đồng bộ ô khi từ khóa đổi từ bên ngoài, ví dụ nút Back. Bản trả về từ chính ô này đã khớp nên bỏ qua.
  if (value !== previousValue) {
    setPreviousValue(value);
    if (value !== text.trim()) {
      setText(value);
    }
  }

  useEffect(() => {
    // Từ khóa bên ngoài khác chữ đang chờ gửi thì lần gọi đó đã cũ. Hủy để không ghi đè giá trị mới.
    if (timerRef.current !== null && value !== pendingTextRef.current.trim()) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  }, [value]);

  // Hủy hẹn giờ khi gỡ component. Không gọi `onSearch` sau khi người dùng đã rời màn.
  useEffect(() => {
    return () => {
      if (timerRef.current !== null) clearTimeout(timerRef.current);
    };
  }, []);

  // Hủy lần gọi đang hẹn giờ. Mọi đường gửi ngay đều gọi hàm này để API không nhận hai request cho một lần nhập.
  const cancelPendingSearch = () => {
    if (timerRef.current === null) return;
    clearTimeout(timerRef.current);
    timerRef.current = null;
  };

  // Gửi từ khóa ngay, không chờ 300 ms. Enter, Esc và nút xóa dùng chung đường này.
  const searchNow = (nextText: string) => {
    cancelPendingSearch();
    // Cập nhật chữ đang chờ trước khi gọi, để hiệu ứng đồng bộ theo `value` không coi giá trị mới là cũ.
    pendingTextRef.current = nextText;
    onSearchRef.current(nextText);
  };

  // Xóa ô và gửi từ khóa rỗng ngay. Nút xóa và phím Esc dùng chung hàm này.
  const clearText = () => {
    setText('');
    searchNow('');
    // Đưa con trỏ về ô để người dùng gõ từ khóa mới ngay, không phải bấm lại vào ô.
    inputRef.current?.focus();
  };

  const handleChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const nextText = event.target.value;
    setText(nextText);
    pendingTextRef.current = nextText;
    // Mỗi phím gõ đặt lại bộ đếm. Chỉ chữ cuối sau khi ngừng gõ được gửi đi.
    cancelPendingSearch();
    timerRef.current = setTimeout(() => {
      timerRef.current = null;
      onSearchRef.current(nextText);
    }, SEARCH_DELAY_MS);
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    // Phím Enter đang xác nhận chữ của bộ gõ chưa phải lệnh tìm. Chỉ nhận phím sau khi bộ gõ chốt chữ.
    if (event.nativeEvent.isComposing) return;

    if (event.key === 'Enter') {
      // Chặn gửi form cha nếu có. Ô tìm đã tự gửi từ khóa.
      event.preventDefault();
      searchNow(text);
      return;
    }

    // Ô rỗng không có gì để xóa. Bỏ qua để Esc vẫn đóng được hộp thoại hay lớp phủ bên ngoài.
    if (event.key === 'Escape' && text !== '') {
      // Chặn hành vi mặc định của `type="search"`. Trình duyệt sẽ tự xóa ô và phát thêm sự kiện `input`, làm hẹn giờ chạy lại.
      event.preventDefault();
      clearText();
    }
  };

  // Chữ toàn khoảng trắng không phải từ khóa, nên không cần nút xóa.
  const hasKeyword = text.trim() !== '';

  return (
    <div className="field">
      <label htmlFor={inputId}>{label}</label>
      <div className="search-input-control">
        <input
          ref={inputRef}
          id={inputId}
          className="search-input-field"
          type="search"
          value={text}
          placeholder={placeholder}
          onChange={handleChange}
          onKeyDown={handleKeyDown}
        />
        {hasKeyword && (
          <button
            type="button"
            className="search-input-clear"
            aria-label="Xóa từ khóa"
            onClick={clearText}
          >
            <Icon name="close" />
          </button>
        )}
      </div>
    </div>
  );
}
