import { useCallback, useState } from 'react';
import {
  addToCompareList,
  readCompareList,
  removeFromCompareList,
  startCompareList,
  writeCompareList,
  type CompareList,
} from './compare-list';

// Truy cập `window.localStorage` có thể ném lỗi (trình duyệt chặn dữ liệu trang), nên bọc để coi là không có.
function getBrowserStorage(): Storage | null {
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

export interface CompareListControls {
  list: CompareList;
  add: (categoryId: string, productId: string) => void;
  remove: (productId: string) => void;
  startNew: (categoryId: string, productId: string) => void;
}

export function useCompareList(): CompareListControls {
  // Đọc `localStorage` một lần khi mở trang, để trạng thái nút còn nguyên sau khi tải lại.
  const [list, setList] = useState<CompareList>(() => readCompareList(getBrowserStorage()));

  const replaceList = useCallback((next: CompareList) => {
    // Ghi ngay mỗi lần đổi, để tab khác và trang so sánh của UI-C3 đọc được danh sách mới nhất.
    writeCompareList(getBrowserStorage(), next);
    setList(next);
  }, []);

  const add = useCallback(
    (categoryId: string, productId: string) =>
      replaceList(addToCompareList(readCompareList(getBrowserStorage()), categoryId, productId)),
    [replaceList],
  );
  const remove = useCallback(
    (productId: string) =>
      replaceList(removeFromCompareList(readCompareList(getBrowserStorage()), productId)),
    [replaceList],
  );
  const startNew = useCallback(
    (categoryId: string, productId: string) => replaceList(startCompareList(categoryId, productId)),
    [replaceList],
  );

  return { list, add, remove, startNew };
}
