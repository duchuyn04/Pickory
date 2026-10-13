import { useEffect, useId, useRef, useState } from 'react';

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

  const handleChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const nextText = event.target.value;
    setText(nextText);
    pendingTextRef.current = nextText;
    // Mỗi phím gõ đặt lại bộ đếm. Chỉ chữ cuối sau khi ngừng gõ được gửi đi.
    if (timerRef.current !== null) clearTimeout(timerRef.current);
    timerRef.current = setTimeout(() => {
      timerRef.current = null;
      onSearchRef.current(nextText);
    }, SEARCH_DELAY_MS);
  };

  return (
    <div className="field">
      <label htmlFor={inputId}>{label}</label>
      <input
        id={inputId}
        type="search"
        value={text}
        placeholder={placeholder}
        onChange={handleChange}
      />
    </div>
  );
}
