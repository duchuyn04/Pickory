// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPARE_STORAGE_KEY } from '../../../../../src/features/catalog/customer/compare-list';
import { createCompareListStore } from '../../../../../src/features/catalog/customer/compare-list-store';

function memoryStorage(initial: Record<string, string> = {}): Storage {
  const data = new Map(Object.entries(initial));
  return {
    get length() {
      return data.size;
    },
    clear: () => data.clear(),
    getItem: (key) => data.get(key) ?? null,
    key: (index) => [...data.keys()][index] ?? null,
    removeItem: (key) => void data.delete(key),
    setItem: (key, value) => void data.set(key, value),
  };
}

// Storage giả ném lỗi ở mọi lần đọc và ghi, như trình duyệt chặn dữ liệu trang.
const brokenStorage = {
  getItem: () => {
    throw new Error('bị chặn');
  },
  setItem: () => {
    throw new Error('bị chặn');
  },
} as unknown as Storage;

afterEach(() => vi.restoreAllMocks());

describe('createCompareListStore khi storage ném lỗi', () => {
  it('giữ đủ 3 sản phẩm thêm liên tiếp trong bộ nhớ', () => {
    const store = createCompareListStore(() => brokenStorage);
    store.add('cat-1', 'p1');
    store.add('cat-1', 'p2');
    store.add('cat-1', 'p3');
    expect(store.getSnapshot()).toEqual({ categoryId: 'cat-1', productIds: ['p1', 'p2', 'p3'] });
  });

  it('vẫn áp giới hạn 4 sản phẩm và rào danh mục', () => {
    const store = createCompareListStore(() => brokenStorage);
    for (const id of ['p1', 'p2', 'p3', 'p4', 'p5']) store.add('cat-1', id);
    store.add('cat-2', 'p9');
    expect(store.getSnapshot().productIds).toEqual(['p1', 'p2', 'p3', 'p4']);
  });

  it('gọi người nghe mỗi lần danh sách đổi', () => {
    const store = createCompareListStore(() => brokenStorage);
    const listener = vi.fn();
    store.subscribe(listener);
    store.add('cat-1', 'p1');
    store.remove('p1');
    expect(listener).toHaveBeenCalledTimes(2);
  });

  it('giữ danh sách trong bộ nhớ khi không có storage', () => {
    const store = createCompareListStore(() => null);
    store.add('cat-1', 'p1');
    expect(store.getSnapshot()).toBe(store.getSnapshot());
    expect(store.getSnapshot().productIds).toEqual(['p1']);
  });
});

describe('createCompareListStore khi storage dùng được', () => {
  it('ghi danh sách vào storage và đọc lại khi tạo kho mới', () => {
    const storage = memoryStorage();
    createCompareListStore(() => storage).add('cat-1', 'p1');
    expect(createCompareListStore(() => storage).getSnapshot().productIds).toEqual(['p1']);
  });

  it('bỏ nhiều id cùng lúc và xóa toàn bộ danh sách', () => {
    const store = createCompareListStore(() => memoryStorage());
    for (const id of ['p1', 'p2', 'p3']) store.add('cat-1', id);
    store.removeMany(['p1', 'p3']);
    expect(store.getSnapshot().productIds).toEqual(['p2']);
    store.clear();
    expect(store.getSnapshot()).toEqual({ categoryId: null, productIds: [] });
  });

  it('bắt đầu danh sách mới với một sản phẩm', () => {
    const store = createCompareListStore(() => memoryStorage());
    store.add('cat-1', 'p1');
    store.startNew('cat-2', 'p9');
    expect(store.getSnapshot()).toEqual({ categoryId: 'cat-2', productIds: ['p9'] });
  });

  it('cập nhật khi tab khác đổi storage (sự kiện storage)', () => {
    const storage = memoryStorage();
    const store = createCompareListStore(() => storage);
    const listener = vi.fn();
    const unsubscribe = store.subscribe(listener);
    storage.setItem(
      COMPARE_STORAGE_KEY,
      JSON.stringify({ categoryId: 'cat-1', productIds: ['x1', 'x2'] }),
    );
    window.dispatchEvent(new StorageEvent('storage', { key: COMPARE_STORAGE_KEY }));
    expect(store.getSnapshot().productIds).toEqual(['x1', 'x2']);
    expect(listener).toHaveBeenCalledTimes(1);
    unsubscribe();
  });

  it('đọc lại và trả danh sách rỗng khi tab khác xóa toàn bộ storage (khóa null)', () => {
    const storage = memoryStorage();
    const store = createCompareListStore(() => storage);
    store.add('cat-1', 'p1');
    store.add('cat-1', 'p2');
    const listener = vi.fn();
    const unsubscribe = store.subscribe(listener);
    storage.clear();
    window.dispatchEvent(new StorageEvent('storage', { key: null }));
    expect(store.getSnapshot()).toEqual({ categoryId: null, productIds: [] });
    expect(listener).toHaveBeenCalledTimes(1);
    unsubscribe();
  });

  it('bỏ qua sự kiện storage của khóa khác', () => {
    const storage = memoryStorage();
    const store = createCompareListStore(() => storage);
    store.add('cat-1', 'p1');
    const listener = vi.fn();
    store.subscribe(listener);
    window.dispatchEvent(new StorageEvent('storage', { key: 'khoa-khac' }));
    expect(listener).not.toHaveBeenCalled();
  });

  it('thôi nghe sự kiện storage sau khi hủy đăng ký', () => {
    const storage = memoryStorage();
    const store = createCompareListStore(() => storage);
    const listener = vi.fn();
    store.subscribe(listener)();
    storage.setItem(COMPARE_STORAGE_KEY, JSON.stringify({ categoryId: 'c', productIds: ['q'] }));
    window.dispatchEvent(new StorageEvent('storage', { key: COMPARE_STORAGE_KEY }));
    expect(listener).not.toHaveBeenCalled();
  });
});
