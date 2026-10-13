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

  // Chỉ đọc các tên màn đã khai báo. Tham số lạ trên URL không lọt vào giá trị bộ lọc.
  const values = {} as Record<Name, string>;
  for (const name of filterNames) {
    values[name] = searchParams.get(name) ?? '';
  }

  const setFilter = (name: string, value: string) => {
    // Sao chép tham số hiện có. Tham số ngoài danh sách phải còn nguyên sau khi ghi.
    const next = new URLSearchParams(searchParams);
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
    setSearchParams(next, { replace: true });
  };

  const setPage = (page: number) => {
    const next = new URLSearchParams(searchParams);
    // Trang 1 là mặc định nên không ghi. Link trang đầu giữ ngắn gọn.
    if (page <= 1) {
      next.delete(PAGE_PARAM);
    } else {
      next.set(PAGE_PARAM, String(page));
    }
    // Thêm mục lịch sử. Nút Back của trình duyệt quay về trang trước.
    setSearchParams(next);
  };

  return { values, page: parsePage(searchParams.get(PAGE_PARAM)), setFilter, setPage };
}
