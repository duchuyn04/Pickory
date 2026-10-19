import { describe, expect, it } from 'vitest';
import { ApiError } from '../../../../src/shared/api';
import {
  FALLBACK_FEE_FIELDS,
  type FallbackFee,
  type FallbackFeeFieldName,
} from '../../../../src/features/shipping/fallback-fee-api';
import {
  FEE_MESSAGE,
  MISSING_MESSAGE,
  WEIGHT_MESSAGE,
  mapServerErrors,
  toFormValues,
  toPayload,
  validateFallbackFeeForm,
  type FallbackFeeFormValues,
} from '../../../../src/features/shipping/fallback-fee-validation';

function filledValues(overrides: Partial<FallbackFeeFormValues> = {}): FallbackFeeFormValues {
  const values = Object.fromEntries(
    FALLBACK_FEE_FIELDS.map((field) => [
      field,
      field.endsWith('BaseWeightGrams') ? '1000' : '20000',
    ]),
  ) as FallbackFeeFormValues;
  return { ...values, ...overrides };
}

function emptyValues(): FallbackFeeFormValues {
  return Object.fromEntries(
    FALLBACK_FEE_FIELDS.map((field) => [field, '']),
  ) as FallbackFeeFormValues;
}

describe('validateFallbackFeeForm', () => {
  it('đủ chín ô hợp lệ thì không có lỗi, dù bật hay tắt', () => {
    expect(validateFallbackFeeForm(filledValues(), true)).toEqual({});
    expect(validateFallbackFeeForm(filledValues(), false)).toEqual({});
  });

  it('bật mà để trống thì mỗi ô trống có lỗi thiếu giá trị', () => {
    const errors = validateFallbackFeeForm(emptyValues(), true);

    expect(Object.keys(errors).sort()).toEqual([...FALLBACK_FEE_FIELDS].sort());
    for (const field of FALLBACK_FEE_FIELDS) {
      expect(errors[field]).toBe(MISSING_MESSAGE);
    }
  });

  it('bật mà chỉ thiếu một ô thì chỉ ô đó có lỗi', () => {
    const errors = validateFallbackFeeForm(filledValues({ crossRegionStepFee: '  ' }), true);

    expect(errors).toEqual({ crossRegionStepFee: MISSING_MESSAGE });
  });

  it('tắt thì ô trống được phép', () => {
    expect(validateFallbackFeeForm(emptyValues(), false)).toEqual({});
    expect(validateFallbackFeeForm(filledValues({ sameRegionBaseFee: '' }), false)).toEqual({});
  });

  it('tắt nhưng ô đã nhập sai khoảng vẫn bị báo lỗi, giống backend', () => {
    const errors = validateFallbackFeeForm(filledValues({ sameRegionBaseFee: '5000001' }), false);

    expect(errors).toEqual({ sameRegionBaseFee: FEE_MESSAGE });
  });

  it.each([
    ['sameProvinceBaseFee', '0', true],
    ['sameProvinceBaseFee', '5000000', true],
    ['sameProvinceBaseFee', '5000001', false],
    ['sameProvinceStepFee', '0', true],
    ['sameProvinceStepFee', '5000001', false],
    ['sameRegionBaseWeightGrams', '1', true],
    ['sameRegionBaseWeightGrams', '20000', true],
    ['sameRegionBaseWeightGrams', '0', false],
    ['sameRegionBaseWeightGrams', '20001', false],
  ] as [FallbackFeeFieldName, string, boolean][])(
    'ô %s nhận giá trị %s: hợp lệ là %s',
    (field, text, isValid) => {
      const errors = validateFallbackFeeForm(filledValues({ [field]: text }), true);

      expect(errors[field] === undefined).toBe(isValid);
    },
  );

  it.each(['-1', '1.5', '1,5', '12a', '+5', '1e3', '0x10', '１２'])(
    'từ chối %s vì không phải số nguyên không âm',
    (text) => {
      const errors = validateFallbackFeeForm(filledValues({ crossRegionBaseFee: text }), true);

      expect(errors.crossRegionBaseFee).toBe(FEE_MESSAGE);
    },
  );

  it('lỗi ô khối lượng nêu khoảng gram, lỗi ô giá nêu khoảng đồng', () => {
    const errors = validateFallbackFeeForm(
      filledValues({ crossRegionBaseWeightGrams: '0', crossRegionBaseFee: '-1' }),
      true,
    );

    expect(errors.crossRegionBaseWeightGrams).toBe(WEIGHT_MESSAGE);
    expect(errors.crossRegionBaseFee).toBe(FEE_MESSAGE);
    expect(WEIGHT_MESSAGE).toContain('gram');
    expect(FEE_MESSAGE).toContain('đồng');
  });
});

describe('toFormValues và toPayload', () => {
  const stored: FallbackFee = {
    enabled: true,
    sameProvinceBaseFee: 20_000,
    sameProvinceBaseWeightGrams: 1000,
    sameProvinceStepFee: 5_000,
    sameRegionBaseFee: 30_000,
    sameRegionBaseWeightGrams: 1000,
    sameRegionStepFee: 6_000,
    crossRegionBaseFee: 40_000,
    crossRegionBaseWeightGrams: 2000,
    crossRegionStepFee: 7_000,
    updatedAt: '2026-10-19T08:00:00.000Z',
  };

  it('số `null` ở backend là chuỗi rỗng ở ô nhập', () => {
    const unsaved: FallbackFee = { ...stored, crossRegionBaseFee: null };

    const values = toFormValues(unsaved);

    expect(values.crossRegionBaseFee).toBe('');
    expect(values.sameProvinceBaseFee).toBe('20000');
  });

  it('payload gửi enabled và chín số, ô trống thành null, và không có updatedAt', () => {
    const payload = toPayload(filledValues({ sameRegionStepFee: ' ' }), true);

    expect(payload.enabled).toBe(true);
    expect(payload.sameRegionStepFee).toBeNull();
    expect(payload.sameProvinceBaseWeightGrams).toBe(1000);
    expect(payload.crossRegionBaseFee).toBe(20_000);
    expect(Object.keys(payload)).toHaveLength(10);
    expect('updatedAt' in payload).toBe(false);
  });

  it('đi vòng từ bản lưu qua ô nhập về payload giữ nguyên chín giá', () => {
    const payload = toPayload(toFormValues(stored), stored.enabled);

    for (const field of FALLBACK_FEE_FIELDS) {
      expect(payload[field]).toBe(stored[field]);
    }
  });
});

describe('mapServerErrors', () => {
  it('lỗi 400 gắn vào đúng ô theo khóa trong details', () => {
    const error = new ApiError('VALIDATION_ERROR', 400, 'Dữ liệu không hợp lệ.', [
      {
        key: 'sameRegionBaseWeightGrams',
        message: 'Khối lượng gốc phải là số nguyên từ 1 đến 20.000 gram.',
      },
      { key: 'crossRegionStepFee', message: 'Giá phải là số nguyên từ 0 đến 5.000.000 đồng.' },
    ]);

    const mapped = mapServerErrors(error);

    expect(mapped.fieldErrors).toEqual({
      sameRegionBaseWeightGrams: 'Khối lượng gốc phải là số nguyên từ 1 đến 20.000 gram.',
      crossRegionStepFee: 'Giá phải là số nguyên từ 0 đến 5.000.000 đồng.',
    });
    expect(mapped.unmatched).toEqual([]);
  });

  it('lỗi 422 thiếu giá gắn vào từng ô thiếu và không đụng ô khác', () => {
    const error = new ApiError(
      'FALLBACK_FEE_INCOMPLETE',
      422,
      'Bật phí dự phòng cần nhập đủ giá của cả 3 tuyến.',
      [
        { key: 'sameProvinceBaseFee', message: MISSING_MESSAGE },
        { key: 'crossRegionBaseWeightGrams', message: MISSING_MESSAGE },
      ],
    );

    const mapped = mapServerErrors(error);

    expect(Object.keys(mapped.fieldErrors).sort()).toEqual([
      'crossRegionBaseWeightGrams',
      'sameProvinceBaseFee',
    ]);
    expect(mapped.unmatched).toEqual([]);
  });

  it('khóa lạ, khóa enabled và lỗi cả body vào danh sách chưa gắn được', () => {
    const error = new ApiError('VALIDATION_ERROR', 400, 'Dữ liệu không hợp lệ.', [
      { key: 'enabled', message: 'enabled là bắt buộc và phải là true hoặc false.' },
      { key: 'truongLa', message: 'Trường này không được hỗ trợ.' },
      { key: '', message: 'Dữ liệu gửi lên phải là một đối tượng JSON.' },
    ]);

    const mapped = mapServerErrors(error);

    expect(mapped.fieldErrors).toEqual({});
    expect(mapped.unmatched).toEqual([
      'enabled là bắt buộc và phải là true hoặc false.',
      'Trường này không được hỗ trợ.',
      'Dữ liệu gửi lên phải là một đối tượng JSON.',
    ]);
  });

  it('lỗi không có details hiện nguyên câu message ở đầu form', () => {
    const mapped = mapServerErrors(new ApiError('NETWORK_ERROR', 0, 'Không kết nối được máy chủ.'));

    expect(mapped.fieldErrors).toEqual({});
    expect(mapped.unmatched).toEqual(['Không kết nối được máy chủ.']);
  });

  it('hai lỗi cùng một ô thì ô giữ lỗi đầu và lỗi sau vào danh sách chưa gắn được', () => {
    const error = new ApiError('VALIDATION_ERROR', 400, 'Dữ liệu không hợp lệ.', [
      { key: 'sameRegionBaseFee', message: 'Lỗi một.' },
      { key: 'sameRegionBaseFee', message: 'Lỗi hai.' },
    ]);

    const mapped = mapServerErrors(error);

    expect(mapped.fieldErrors).toEqual({ sameRegionBaseFee: 'Lỗi một.' });
    expect(mapped.unmatched).toEqual(['Lỗi hai.']);
  });
});
