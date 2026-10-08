import { describe, expect, it } from 'vitest';
import {
  COMPARE_LIST_LIMIT,
  addToCompareList,
  describeAddState,
  emptyCompareList,
  readCompareList,
  removeFromCompareList,
  startCompareList,
  writeCompareList,
} from '../../../../../src/features/catalog/customer/compare-list';

function memoryStorage(initial: Record<string, string> = {}): Storage {
  const store = new Map(Object.entries(initial));
  return {
    get length() {
      return store.size;
    },
    clear: () => store.clear(),
    getItem: (key) => store.get(key) ?? null,
    key: (index) => [...store.keys()][index] ?? null,
    removeItem: (key) => void store.delete(key),
    setItem: (key, value) => void store.set(key, value),
  };
}

describe('addToCompareList', () => {
  it('thêm sản phẩm vào danh sách rỗng và ghi nhớ danh mục', () => {
    const list = addToCompareList(emptyCompareList, 'cat-1', 'p1');
    expect(list).toEqual({ categoryId: 'cat-1', productIds: ['p1'] });
  });

  it('không nhận sản phẩm thứ 5', () => {
    let list = emptyCompareList;
    for (const id of ['p1', 'p2', 'p3', 'p4', 'p5']) list = addToCompareList(list, 'cat-1', id);
    expect(list.productIds).toEqual(['p1', 'p2', 'p3', 'p4']);
    expect(list.productIds).toHaveLength(COMPARE_LIST_LIMIT);
  });

  it('không nhận sản phẩm khác danh mục', () => {
    const list = addToCompareList({ categoryId: 'cat-1', productIds: ['p1'] }, 'cat-2', 'p9');
    expect(list.productIds).toEqual(['p1']);
  });

  it('không thêm trùng một sản phẩm', () => {
    const list = addToCompareList({ categoryId: 'cat-1', productIds: ['p1'] }, 'cat-1', 'p1');
    expect(list.productIds).toEqual(['p1']);
  });
});

describe('removeFromCompareList', () => {
  it('bỏ sản phẩm khỏi danh sách', () => {
    const list = removeFromCompareList({ categoryId: 'cat-1', productIds: ['p1', 'p2'] }, 'p1');
    expect(list).toEqual({ categoryId: 'cat-1', productIds: ['p2'] });
  });

  it('xóa danh mục khi bỏ sản phẩm cuối, để danh sách rỗng nhận danh mục khác', () => {
    const list = removeFromCompareList({ categoryId: 'cat-1', productIds: ['p1'] }, 'p1');
    expect(list).toEqual(emptyCompareList);
  });
});

describe('startCompareList', () => {
  it('bỏ danh sách cũ và bắt đầu danh sách mới với một sản phẩm', () => {
    expect(startCompareList('cat-2', 'p9')).toEqual({ categoryId: 'cat-2', productIds: ['p9'] });
  });
});

describe('describeAddState', () => {
  const full = { categoryId: 'cat-1', productIds: ['p1', 'p2', 'p3', 'p4'] };

  it('báo đã có trong danh sách trước cả khi danh sách đầy', () => {
    expect(describeAddState(full, 'cat-1', 'p2')).toBe('in-list');
  });

  it('báo đầy khi đủ 4 sản phẩm và sản phẩm chưa có trong danh sách', () => {
    expect(describeAddState(full, 'cat-1', 'p9')).toBe('full');
  });

  it('báo khác danh mục khi danh sách có sản phẩm của danh mục khác', () => {
    expect(describeAddState({ categoryId: 'cat-1', productIds: ['p1'] }, 'cat-2', 'p9')).toBe(
      'category-mismatch',
    );
  });

  it('cho thêm khi danh sách rỗng', () => {
    expect(describeAddState(emptyCompareList, 'cat-1', 'p1')).toBe('can-add');
  });
});

describe('readCompareList và writeCompareList', () => {
  it('đọc lại đúng danh sách đã ghi', () => {
    const storage = memoryStorage();
    writeCompareList(storage, { categoryId: 'cat-1', productIds: ['p1', 'p2'] });
    expect(readCompareList(storage)).toEqual({ categoryId: 'cat-1', productIds: ['p1', 'p2'] });
  });

  it('coi JSON hỏng là danh sách rỗng', () => {
    const storage = memoryStorage({ 'pickory.compare': '{hỏng' });
    expect(readCompareList(storage)).toEqual(emptyCompareList);
  });

  it('coi dữ liệu sai dạng là danh sách rỗng', () => {
    const storage = memoryStorage({
      'pickory.compare': JSON.stringify({ categoryId: 5, productIds: 'p1' }),
    });
    expect(readCompareList(storage)).toEqual(emptyCompareList);
  });

  it('cắt dữ liệu quá 4 sản phẩm và bỏ id trùng', () => {
    const storage = memoryStorage({
      'pickory.compare': JSON.stringify({
        categoryId: 'cat-1',
        productIds: ['p1', 'p1', 'p2', 'p3', 'p4', 'p5'],
      }),
    });
    expect(readCompareList(storage).productIds).toEqual(['p1', 'p2', 'p3', 'p4']);
  });

  it('coi storage ném lỗi là danh sách rỗng và không ném lỗi khi ghi', () => {
    const broken = {
      getItem: () => {
        throw new Error('bị chặn');
      },
      setItem: () => {
        throw new Error('bị chặn');
      },
    } as unknown as Storage;
    expect(readCompareList(broken)).toEqual(emptyCompareList);
    expect(() => writeCompareList(broken, emptyCompareList)).not.toThrow();
  });

  it('coi storage không có là danh sách rỗng', () => {
    expect(readCompareList(null)).toEqual(emptyCompareList);
  });
});
