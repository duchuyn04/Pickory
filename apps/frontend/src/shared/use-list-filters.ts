import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

const PAGE_PARAM = 'page';

export interface ListFilters<Name extends string = string> {
  // Giá trị hiện tại của từng bộ lọc trong danh sách tên. Chuỗi rỗng khi URL không có tham số.
  values: Record<Name, string>;
  page: number;
  setFilter: (name: Name, value: string) => void;
  setPage: (page: number) => void;
}

// Đọc số trang từ URL. Giá trị không phải số nguyên từ 1 trở lên được coi là trang 1.
function parsePage(raw: string | null): number {
  if (raw === null || !/^\d+$/.test(raw)) return 1;
  const page = Number(raw);
  return page >= 1 ? page : 1;
}

// Giữ bộ lọc và số trang của màn danh sách trên URL. Mở lại link cho đúng danh sách đó.
export function useListFilters<const Name extends string>(
  filterNames: readonly Name[],
): ListFilters<Name> {
  const [searchParams, setSearchParams] = useSearchParams();

  // `BrowserRouter` cập nhật địa chỉ trong một chuyển cảnh, nên `searchParams` đổi chậm hơn thao tác.
  // Giá trị chờ cho ô điều khiển, như ô tích hoặc ô chọn, đổi ngay mà không quay về giá trị cũ.
  const [pendingParams, setPendingParams] = useState<URLSearchParams | null>(null);
  const [seenSearchParams, setSeenSearchParams] = useState(searchParams);
  if (seenSearchParams !== searchParams) {
    // Router đã cập nhật địa chỉ, kể cả do nút Back. Bỏ giá trị chờ để địa chỉ là nguồn đúng duy nhất.
    setSeenSearchParams(searchParams);
    setPendingParams(null);
  }
  const currentParams = pendingParams ?? searchParams;

  // Tham số vừa ghi gần nhất. Hai lần ghi liền nhau, chưa kịp vẽ lại, không ghi đè nhau.
  const lastWrittenParams = useRef<URLSearchParams | null>(null);
  useEffect(() => {
    // Địa chỉ đã cập nhật thì lấy địa chỉ làm gốc cho các lần ghi sau.
    lastWrittenParams.current = null;
  }, [searchParams]);

  const writeParams = (next: URLSearchParams, replace: boolean) => {
    lastWrittenParams.current = next;
    setPendingParams(next);
    setSearchParams(next, { replace });
  };

  // Chỉ đọc các tên màn đã khai báo. Tham số lạ trên URL không lọt vào giá trị bộ lọc.
  const values = {} as Record<Name, string>;
  for (const name of filterNames) {
    values[name] = currentParams.get(name) ?? '';
  }

  const setFilter = (name: string, value: string) => {
    // Sao chép tham số mới nhất. Tham số ngoài danh sách phải còn nguyên sau khi ghi.
    const next = new URLSearchParams(lastWrittenParams.current ?? searchParams);
    const trimmed = value.trim();
    // Giá trị rỗng xóa tham số. URL không giữ `?search=` vô nghĩa.
    if (trimmed === '') {
      next.delete(name);
    } else {
      next.set(name, trimmed);
    }
    // Bộ lọc đổi thì kết quả đổi. Về trang 1 để không đứng ở trang không còn tồn tại.
    next.delete(PAGE_PARAM);
    // Thay mục lịch sử hiện tại. Mỗi lần gõ không thêm một mục cho nút Back.
    writeParams(next, true);
  };

  const setPage = (page: number) => {
    const next = new URLSearchParams(lastWrittenParams.current ?? searchParams);
    // Trang 1 là mặc định nên không ghi. Link trang đầu giữ ngắn gọn.
    if (page <= 1) {
      next.delete(PAGE_PARAM);
    } else {
      next.set(PAGE_PARAM, String(page));
    }
    // Thêm mục lịch sử. Nút Back của trình duyệt quay về trang trước.
    writeParams(next, false);
  };

  return { values, page: parsePage(currentParams.get(PAGE_PARAM)), setFilter, setPage };
}
