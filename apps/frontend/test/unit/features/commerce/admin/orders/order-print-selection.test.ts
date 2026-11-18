import { describe, expect, it } from 'vitest';
import {
  buildOrderListSelectionKey,
  getSelectAllState,
  getVisibleSelectedCodes,
  MAX_ORDERS_PER_PRINT,
  toggleOrderSelection,
  toggleSelectAllOrders,
} from '../../../../../../src/features/commerce/admin/orders/order-print-selection';

function makeCodes(count: number): string[] {
  return Array.from({ length: count }, (_, index) => `PK-${String(index + 1).padStart(2, '0')}`);
}

describe('order-print-selection', () => {
  it('giới hạn tối đa mỗi lần in là 50 đơn', () => {
    expect(MAX_ORDERS_PER_PRINT).toBe(50);
  });

  it('trả về mã đã chọn theo thứ tự hiển thị dù người dùng bấm theo thứ tự ngược', () => {
    const visible = ['PK-1', 'PK-2', 'PK-3', 'PK-4'];
    let selected = new Set<string>();
    selected = toggleOrderSelection(selected, 'PK-3', visible);
    selected = toggleOrderSelection(selected, 'PK-1', visible);
    selected = toggleOrderSelection(selected, 'PK-2', visible);

    expect(getVisibleSelectedCodes(selected, visible)).toEqual(['PK-1', 'PK-2', 'PK-3']);
  });

  it('bỏ chọn một đơn đang chọn và giữ các đơn còn lại', () => {
    const visible = ['PK-1', 'PK-2', 'PK-3'];
    const initial = new Set(['PK-1', 'PK-2', 'PK-3']);
    const next = toggleOrderSelection(initial, 'PK-2', visible);

    expect(getVisibleSelectedCodes(next, visible)).toEqual(['PK-1', 'PK-3']);
  });

  it('loại bỏ mã không còn trong trang hiện tại khi tính danh sách đã chọn', () => {
    const visible = ['PK-2', 'PK-4'];
    const selected = new Set(['PK-1', 'PK-2', 'PK-3']);

    expect(getVisibleSelectedCodes(selected, visible)).toEqual(['PK-2']);
  });

  it('chặn chọn thêm khi đã đạt giới hạn 50 đơn nhưng vẫn cho bỏ chọn', () => {
    const visible = makeCodes(60);
    const initial = new Set(visible.slice(0, 50));

    // Đơn thứ 51 không được thêm vào lựa chọn khi đã chạm ngưỡng tối đa.
    const afterAdd51 = toggleOrderSelection(initial, visible[50]!, visible);
    expect(getVisibleSelectedCodes(afterAdd51, visible)).toHaveLength(50);
    expect(afterAdd51.has(visible[50]!)).toBe(false);

    // Bỏ chọn một đơn đang chọn vẫn hoạt động bình thường để giảm xuống 49 đơn.
    const afterRemove = toggleOrderSelection(initial, visible[0]!, visible);
    expect(getVisibleSelectedCodes(afterRemove, visible)).toHaveLength(49);
    expect(afterRemove.has(visible[0]!)).toBe(false);
  });

  it('chọn tất cả với trang 3 dòng chọn đủ 3 rồi bấm lần nữa bỏ chọn hết', () => {
    const visible = ['PK-1', 'PK-2', 'PK-3'];
    const allSelected = toggleSelectAllOrders(new Set(), visible);
    expect(getVisibleSelectedCodes(allSelected, visible)).toEqual(['PK-1', 'PK-2', 'PK-3']);
    expect(getSelectAllState(3, 3)).toBe('all');

    const cleared = toggleSelectAllOrders(allSelected, visible);
    expect(getVisibleSelectedCodes(cleared, visible)).toEqual([]);
    expect(getSelectAllState(0, 3)).toBe('none');
  });

  it('chọn tất cả khi đang chọn một phần thì chọn đủ trang', () => {
    const visible = ['PK-1', 'PK-2', 'PK-3'];
    expect(getSelectAllState(1, 3)).toBe('some');

    const next = toggleSelectAllOrders(new Set(['PK-2']), visible);
    expect(getVisibleSelectedCodes(next, visible)).toEqual(['PK-1', 'PK-2', 'PK-3']);
  });

  it('chọn tất cả trên trang 60 dòng chỉ lấy đúng 50 dòng đầu và bấm lại bỏ chọn hết', () => {
    const visible = makeCodes(60);
    const first50 = toggleSelectAllOrders(new Set(), visible);
    const ordered = getVisibleSelectedCodes(first50, visible);

    expect(ordered).toHaveLength(50);
    expect(ordered[0]).toBe('PK-01');
    expect(ordered[49]).toBe('PK-50');
    expect(getSelectAllState(ordered.length, visible.length)).toBe('all');

    const cleared = toggleSelectAllOrders(first50, visible);
    expect(getVisibleSelectedCodes(cleared, visible)).toEqual([]);
  });

  it('cắt đúng theo limit khi tập đã chọn vượt quá giới hạn truyền vào', () => {
    const visible = ['PK-1', 'PK-2', 'PK-3', 'PK-4'];
    const selected = new Set(visible);

    expect(getVisibleSelectedCodes(selected, visible, 2)).toEqual(['PK-1', 'PK-2']);
  });

  it('bỏ qua mã không nằm trong danh sách hiển thị khi bấm chọn một đơn', () => {
    const visible = ['PK-1', 'PK-2'];
    const next = toggleOrderSelection(new Set(['PK-1']), 'PK-OUTSIDE', visible);

    expect([...next]).toEqual(['PK-1']);
  });

  it('cắt các mã cũ không còn trên trang trước khi kiểm tra giới hạn lúc chọn thêm', () => {
    const visible = ['PK-1', 'PK-2', 'PK-3'];
    // Hai mã PK-OLD-1 và PK-OLD-2 không còn trên trang nên không chiếm chỗ của giới hạn 2 đơn.
    const selectedWithStale = new Set(['PK-OLD-1', 'PK-OLD-2', 'PK-1']);
    const next = toggleOrderSelection(selectedWithStale, 'PK-2', visible, 2);

    expect([...next]).toEqual(['PK-1', 'PK-2']);
  });

  it('khóa danh sách đổi khi số trang hoặc bất kỳ bộ lọc nào thay đổi', () => {
    const baseValues = {
      search: '',
      status: '',
      paymentStatus: '',
      paymentMethod: '',
      flag: '',
      from: '',
      to: '',
    };
    const baseKey = buildOrderListSelectionKey(baseValues, 1);

    expect(buildOrderListSelectionKey(baseValues, 2)).not.toBe(baseKey);
    for (const key of Object.keys(baseValues)) {
      const nextKey = buildOrderListSelectionKey({ ...baseValues, [key]: 'changed' }, 1);
      expect(nextKey).not.toBe(baseKey);
    }
  });
});
