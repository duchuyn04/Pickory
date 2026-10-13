import { describe, expect, it } from 'vitest';
import {
  initialParcelForm,
  toParcelBody,
  validateCancelReason,
  validateParcelForm,
  validateTransactionRef,
} from '../../../../../../src/features/commerce/admin/orders/order-action-forms';

describe('validateCancelReason', () => {
  it('nhận lý do hợp lệ', () => {
    expect(validateCancelReason('Khách đổi ý')).toBeNull();
  });

  it('từ chối lý do rỗng hoặc chỉ có khoảng trắng', () => {
    expect(validateCancelReason('')).toBe('Nhập lý do hủy đơn.');
    expect(validateCancelReason('   ')).toBe('Nhập lý do hủy đơn.');
  });

  it('nhận đúng 500 ký tự và từ chối 501 ký tự', () => {
    expect(validateCancelReason('a'.repeat(500))).toBeNull();
    expect(validateCancelReason('a'.repeat(501))).toBe(
      'Lý do hủy đơn không được dài quá 500 ký tự.',
    );
  });

  it('đếm ký tự sau khi bỏ khoảng trắng hai đầu', () => {
    expect(validateCancelReason(`  ${'a'.repeat(500)}  `)).toBeNull();
  });
});

describe('validateTransactionRef', () => {
  it('nhận mã hợp lệ', () => {
    expect(validateTransactionRef('FT26101200001')).toBeNull();
  });

  it('từ chối mã rỗng hoặc chỉ có khoảng trắng', () => {
    expect(validateTransactionRef('  ')).toBe('Nhập mã giao dịch hoàn tiền.');
  });

  it('nhận đúng 100 ký tự và từ chối 101 ký tự', () => {
    expect(validateTransactionRef('a'.repeat(100))).toBeNull();
    expect(validateTransactionRef('a'.repeat(101))).toBe(
      'Mã giao dịch hoàn tiền không được dài quá 100 ký tự.',
    );
  });
});

describe('initialParcelForm', () => {
  it('điền sẵn bốn ô từ kiện gợi ý', () => {
    expect(
      initialParcelForm({ weightGrams: 1200, lengthCm: 30, widthCm: 20, heightCm: 10 }),
    ).toEqual({ weightGrams: '1200', lengthCm: '30', widthCm: '20', heightCm: '10' });
  });

  it('để trống bốn ô khi không có kiện gợi ý', () => {
    expect(initialParcelForm(null)).toEqual({
      weightGrams: '',
      lengthCm: '',
      widthCm: '',
      heightCm: '',
    });
  });
});

describe('validateParcelForm', () => {
  const valid = { weightGrams: '1200', lengthCm: '30', widthCm: '20', heightCm: '10' };

  it('nhận bốn số nguyên dương', () => {
    expect(validateParcelForm(valid)).toEqual({});
  });

  it.each(['0', '-5', 'abc', '1.5', '', '  ', '1e3', '2147483648'])(
    'từ chối giá trị %j',
    (value) => {
      const errors = validateParcelForm({ ...valid, lengthCm: value });

      expect(errors.lengthCm).toBe('Nhập số nguyên dương.');
      expect(errors.weightGrams).toBeUndefined();
    },
  );

  it('báo lỗi từng ô sai', () => {
    const errors = validateParcelForm({
      weightGrams: '0',
      lengthCm: '30',
      widthCm: 'x',
      heightCm: '',
    });

    expect(Object.keys(errors).sort()).toEqual(['heightCm', 'weightGrams', 'widthCm']);
  });
});

describe('toParcelBody', () => {
  it('đổi chuỗi sang số nguyên và bọc trong trường parcel', () => {
    expect(
      toParcelBody({ weightGrams: ' 1200 ', lengthCm: '30', widthCm: '20', heightCm: '10' }),
    ).toEqual({ parcel: { weightGrams: 1200, lengthCm: 30, widthCm: 20, heightCm: 10 } });
  });
});
