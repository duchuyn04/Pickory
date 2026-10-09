import {
  COMPARE_STORAGE_KEY,
  addToCompareList,
  emptyCompareList,
  readCompareList,
  removeFromCompareList,
  startCompareList,
  writeCompareList,
  type CompareList,
} from './compare-list';

export interface CompareListStore {
  getSnapshot: () => CompareList;
  subscribe: (listener: () => void) => () => void;
  add: (categoryId: string, productId: string) => void;
  remove: (productId: string) => void;
  removeMany: (productIds: string[]) => void;
  startNew: (categoryId: string, productId: string) => void;
  clear: () => void;
  // Đọc lại storage và bỏ danh sách trong bộ nhớ, như khi mở trang lần đầu.
  reload: () => void;
}

// Kho giữ danh sách trong bộ nhớ. Storage chỉ là nơi lưu thêm, nên danh sách vẫn đúng khi storage hỏng.
export function createCompareListStore(getStorage: () => Storage | null): CompareListStore {
  // Null nghĩa là chưa đọc storage. Đọc một lần khi cần để mở trang là có sẵn danh sách đã lưu.
  let currentList: CompareList | null = null;
  const listeners = new Set<() => void>();

  function getSnapshot(): CompareList {
    if (currentList === null) currentList = readCompareList(getStorage());
    return currentList;
  }

  function notifyListeners() {
    listeners.forEach((listener) => listener());
  }

  function replaceList(next: CompareList) {
    // Cập nhật bộ nhớ trước. Ghi storage lỗi thì danh sách trong phiên vẫn đúng.
    currentList = next;
    writeCompareList(getStorage(), next);
    notifyListeners();
  }

  function reload() {
    currentList = readCompareList(getStorage());
    notifyListeners();
  }

  // Tab khác ghi storage thì đọc lại, để trang đang mở cập nhật theo.
  function handleStorageEvent(event: StorageEvent) {
    // Khóa null nghĩa là storage bị xóa toàn bộ, nên cũng phải đọc lại.
    if (event.key !== null && event.key !== COMPARE_STORAGE_KEY) return;
    reload();
  }

  function subscribe(listener: () => void): () => void {
    // Chỉ nghe sự kiện `storage` khi có người đăng ký, để không giữ listener thừa trên `window`.
    if (listeners.size === 0) window.addEventListener('storage', handleStorageEvent);
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
      if (listeners.size === 0) window.removeEventListener('storage', handleStorageEvent);
    };
  }

  return {
    getSnapshot,
    subscribe,
    add: (categoryId, productId) =>
      replaceList(addToCompareList(getSnapshot(), categoryId, productId)),
    remove: (productId) => replaceList(removeFromCompareList(getSnapshot(), productId)),
    removeMany: (productIds) => {
      // Bỏ lần lượt từng id trên cùng một danh sách để chỉ ghi và báo một lần.
      const next = productIds.reduce(removeFromCompareList, getSnapshot());
      replaceList(next);
    },
    startNew: (categoryId, productId) => replaceList(startCompareList(categoryId, productId)),
    clear: () => replaceList(emptyCompareList),
    reload,
  };
}
