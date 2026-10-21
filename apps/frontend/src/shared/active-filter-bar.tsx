import { Icon } from './icons';
import './active-filter-bar.css';

export interface ActiveFilter<Name extends string = string> {
  // Tên bộ lọc, đưa lại cho `onRemove`. Trùng tên tham số trên URL của màn.
  name: Name;
  // Chữ nhìn thấy trên nhãn, ví dụ `Từ khóa: iphone`.
  label: string;
}

interface ActiveFilterBarProps<Name extends string> {
  filters: ActiveFilter<Name>[];
  // Dòng số kết quả, ví dụ `12 sản phẩm khớp "iphone"`. Màn tự ghép vì mỗi màn gọi đơn vị khác nhau.
  summary?: string;
  onRemove: (name: Name) => void;
  onClearAll: () => void;
}

// Thanh nằm giữa hàng lọc và bảng, cho người dùng thấy bộ lọc nào đang bật và bỏ từng bộ lọc.
export function ActiveFilterBar<Name extends string>({
  filters,
  summary,
  onRemove,
  onClearAll,
}: ActiveFilterBarProps<Name>) {
  // Không có bộ lọc thì không có gì để hiện. Màn giữ nguyên bố cục cũ.
  if (filters.length === 0) return null;

  return (
    <div className="active-filter-bar" role="group" aria-label="Bộ lọc đang áp dụng">
      <ul className="active-filter-list">
        {filters.map((filter) => (
          <li key={filter.name} className="active-filter-chip">
            <span className="active-filter-label">{filter.label}</span>
            <button
              type="button"
              className="active-filter-remove"
              aria-label={`Bỏ lọc ${filter.label}`}
              onClick={() => onRemove(filter.name)}
            >
              <Icon name="close" />
            </button>
          </li>
        ))}
      </ul>
      <button type="button" className="btn btn-sm" onClick={onClearAll}>
        Xóa bộ lọc
      </button>
      {/* Số kết quả đổi khi bộ lọc đổi. `aria-live` cho trình đọc màn hình đọc lại số mới. */}
      {summary && (
        <p className="active-filter-summary muted small" aria-live="polite">
          {summary}
        </p>
      )}
    </div>
  );
}
