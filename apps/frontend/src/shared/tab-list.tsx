import { useRef, type KeyboardEvent } from 'react';
import './tab-list.css';

export interface TabItem {
  // Mã tab. Chuỗi rỗng là tab "tất cả" không lọc.
  code: string;
  label: string;
}

interface TabListProps {
  // Tên truy cập của dải tab.
  label: string;
  // Tiền tố id của tab, để vùng nội dung nối `aria-labelledby` tới đúng tab.
  idPrefix: string;
  // Id của vùng nội dung mà các tab điều khiển.
  panelId: string;
  tabs: TabItem[];
  selected: string;
  onSelect: (code: string) => void;
}

// Id của một tab theo quy ước của `TabList`. Vùng nội dung dùng hàm này cho `aria-labelledby`.
export function tabId(idPrefix: string, code: string): string {
  return `${idPrefix}-tab-${code || 'all'}`;
}

// Dải tab theo mẫu `tablist`: chỉ tab đang chọn nằm trong thứ tự Tab, các tab khác đi bằng mũi tên.
export function TabList({ label, idPrefix, panelId, tabs, selected, onSelect }: TabListProps) {
  const tabRefs = useRef<(HTMLButtonElement | null)[]>([]);

  // Mũi tên di chuyển focus giữa các tab. Enter hoặc Space của nút mới chọn tab.
  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const currentIndex = tabRefs.current.findIndex((element) => element === event.target);
    if (currentIndex === -1) return;
    let nextIndex: number | null = null;
    if (event.key === 'ArrowRight') nextIndex = (currentIndex + 1) % tabs.length;
    if (event.key === 'ArrowLeft') nextIndex = (currentIndex - 1 + tabs.length) % tabs.length;
    if (event.key === 'Home') nextIndex = 0;
    if (event.key === 'End') nextIndex = tabs.length - 1;
    if (nextIndex === null) return;
    // Chặn cuộn trang của phím mũi tên khi focus đang ở dải tab.
    event.preventDefault();
    tabRefs.current[nextIndex]?.focus();
  }

  return (
    <div className="tab-list" role="tablist" aria-label={label} onKeyDown={handleKeyDown}>
      {tabs.map((tab, index) => {
        const isSelected = selected === tab.code;
        return (
          <button
            key={tab.code || 'all'}
            ref={(element) => {
              tabRefs.current[index] = element;
            }}
            id={tabId(idPrefix, tab.code)}
            type="button"
            role="tab"
            className="tab-list-item"
            aria-selected={isSelected}
            aria-controls={panelId}
            tabIndex={isSelected ? 0 : -1}
            onClick={() => onSelect(tab.code)}
          >
            {tab.label}
          </button>
        );
      })}
    </div>
  );
}
