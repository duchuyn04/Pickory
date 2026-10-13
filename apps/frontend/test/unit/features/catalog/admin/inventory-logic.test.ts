import { describe, expect, it } from 'vitest';
import {
  MAX_REASON_LENGTH,
  formatDelta,
  describeOptionValues,
  movementTypeLabel,
  stockAfterAdjustment,
  stockBadge,
  toAdjustmentBody,
  validateAdjustment,
} from '../../../../../src/features/catalog/admin/inventory-logic';

describe('validateAdjustment', () => {
  it('nhận điều chỉnh hợp lệ', () => {
    expect(validateAdjustment({ type: 'manual', delta: '-3', reason: 'Kiểm kê lệch' })).toEqual({});
    expect(validateAdjustment({ type: 'return', delta: '1', reason: 'Khách trả' })).toEqual({});
  });

  it('từ chối số lượng rỗng, không phải số nguyên hoặc bằng 0', () => {
    for (const delta of ['', '  ', 'abc', '1.5', '1e3x']) {
      expect(validateAdjustment({ type: 'manual', delta, reason: 'Lý do' }).delta).toBe(
        'Số lượng thay đổi phải là số nguyên.',
      );
    }
    expect(validateAdjustment({ type: 'manual', delta: '0', reason: 'Lý do' }).delta).toBe(
      'Số lượng thay đổi phải khác 0.',
    );
  });

  it('bắt buộc lý do sau khi bỏ khoảng trắng', () => {
    for (const reason of ['', '   ', '\n\t']) {
      expect(validateAdjustment({ type: 'manual', delta: '1', reason }).reason).toBe(
        'Lý do là bắt buộc.',
      );
    }
  });

  it('giới hạn lý do 500 ký tự tính sau khi cắt khoảng trắng', () => {
    const atLimit = 'a'.repeat(MAX_REASON_LENGTH);
    expect(
      validateAdjustment({ type: 'manual', delta: '1', reason: atLimit }).reason,
    ).toBeUndefined();
    expect(
      validateAdjustment({ type: 'manual', delta: '1', reason: ` ${atLimit} ` }).reason,
    ).toBeUndefined();
    expect(validateAdjustment({ type: 'manual', delta: '1', reason: `${atLimit}a` }).reason).toBe(
      'Lý do không được dài quá 500 ký tự.',
    );
  });

  it('khách trả hàng cần số lượng dương', () => {
    for (const delta of ['-1', '0']) {
      expect(validateAdjustment({ type: 'return', delta, reason: 'Lý do' }).delta).toBe(
        delta === '0'
          ? 'Số lượng thay đổi phải khác 0.'
          : 'Khách trả hàng phải có số lượng lớn hơn 0.',
      );
    }
  });

  it('báo cả hai lỗi cùng lúc', () => {
    expect(validateAdjustment({ type: 'manual', delta: '0', reason: '' })).toEqual({
      delta: 'Số lượng thay đổi phải khác 0.',
      reason: 'Lý do là bắt buộc.',
    });
  });
});

describe('toAdjustmentBody', () => {
  it('đổi form thành body API với lý do đã cắt khoảng trắng', () => {
    expect(toAdjustmentBody({ type: 'return', delta: ' 4 ', reason: '  Khách trả  ' })).toEqual({
      delta: 4,
      reason: 'Khách trả',
      type: 'return',
    });
  });
});

describe('movementTypeLabel', () => {
  it('có nhãn tiếng Việt cho cả 4 loại', () => {
    expect(movementTypeLabel('manual')).toBe('Điều chỉnh');
    expect(movementTypeLabel('return')).toBe('Khách trả hàng');
    expect(movementTypeLabel('order_reserve')).toBe('Giữ hàng cho đơn');
    expect(movementTypeLabel('order_release')).toBe('Trả hàng về kho');
  });

  it('giữ nguyên mã loại lạ để không mất thông tin', () => {
    expect(movementTypeLabel('moi_la')).toBe('moi_la');
  });
});

describe('formatDelta', () => {
  it('có dấu cộng hoặc dấu trừ thường, không dùng dấu gạch dài', () => {
    expect(formatDelta(7)).toBe('+7');
    expect(formatDelta(-3)).toBe('-3');
    expect(formatDelta(0)).toBe('0');
    // Mã 45 là dấu gạch nối ASCII. Các dấu gạch dài bị cấm trong repo.
    expect(formatDelta(-3).charCodeAt(0)).toBe(45);
  });
});

describe('stockAfterAdjustment', () => {
  it('cộng số lượng thay đổi vào tồn hiện tại', () => {
    expect(stockAfterAdjustment(5, '7')).toBe(12);
    expect(stockAfterAdjustment(5, '-5')).toBe(0);
    expect(stockAfterAdjustment(5, '-6')).toBe(-1);
  });

  it('trả null khi số lượng chưa hợp lệ', () => {
    for (const delta of ['', '-', 'abc', '1.5']) {
      expect(stockAfterAdjustment(5, delta)).toBeNull();
    }
  });
});

describe('stockBadge', () => {
  it('chia tình trạng theo ngưỡng 5 của bộ lọc sắp hết hàng', () => {
    expect(stockBadge(0).label).toBe('Hết hàng');
    expect(stockBadge(-1).label).toBe('Hết hàng');
    expect(stockBadge(1).label).toBe('Sắp hết');
    expect(stockBadge(5).label).toBe('Sắp hết');
    expect(stockBadge(6).label).toBe('Còn hàng');
  });
});

describe('describeOptionValues', () => {
  it('nối giá trị tùy chọn, SKU không có tùy chọn gọi là Mặc định', () => {
    expect(describeOptionValues({ Màu: 'Đỏ', 'Bộ nhớ': '128GB' })).toBe('Đỏ / 128GB');
    expect(describeOptionValues({})).toBe('Mặc định');
  });
});
