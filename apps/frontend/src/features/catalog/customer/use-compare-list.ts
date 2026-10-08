import { useSyncExternalStore } from 'react';
import type { CompareList } from './compare-list';
import { createCompareListStore } from './compare-list-store';

// Truy cập `window.localStorage` có thể ném lỗi (trình duyệt chặn dữ liệu trang), nên bọc để coi là không có.
function getBrowserStorage(): Storage | null {
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

// Một kho cho cả phiên. Trang sản phẩm và trang so sánh nhờ đó thấy cùng một danh sách.
export const compareListStore = createCompareListStore(getBrowserStorage);

export interface CompareListControls {
  list: CompareList;
  add: (categoryId: string, productId: string) => void;
  remove: (productId: string) => void;
  removeMany: (productIds: string[]) => void;
  startNew: (categoryId: string, productId: string) => void;
  clear: () => void;
}

export function useCompareList(): CompareListControls {
  const list = useSyncExternalStore(compareListStore.subscribe, compareListStore.getSnapshot);
  return {
    list,
    add: compareListStore.add,
    remove: compareListStore.remove,
    removeMany: compareListStore.removeMany,
    startNew: compareListStore.startNew,
    clear: compareListStore.clear,
  };
}
