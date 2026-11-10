import { describe, expect, it } from 'vitest';
import { ApiError } from '../../../../../src/shared/api';
import {
  mapCannedResponseServerErrors,
  stripLeadingSlashes,
  validateCannedResponse,
} from '../../../../../src/features/support-desk/admin/canned-response-validation';

describe('validateCannedResponse', () => {
  it('phím tắt và nội dung hợp lệ không có lỗi', () => {
    expect(validateCannedResponse({ shortcut: 'bao-hanh_12', body: 'Nội dung.' })).toEqual({});
  });

  it('chấp nhận độ dài 2 và 30 ký tự, loại 1 và 31', () => {
    expect(validateCannedResponse({ shortcut: 'ab', body: 'x' })).toEqual({});
    expect(validateCannedResponse({ shortcut: 'a'.repeat(30), body: 'x' })).toEqual({});
    expect(validateCannedResponse({ shortcut: 'a', body: 'x' }).shortcut).toBe(
      'Phím tắt dài từ 2 đến 30 ký tự.',
    );
    expect(validateCannedResponse({ shortcut: 'a'.repeat(31), body: 'x' }).shortcut).toBe(
      'Phím tắt dài từ 2 đến 30 ký tự.',
    );
  });

  it('không cho chữ hoa, dấu tiếng Việt, khoảng trắng và ký tự đặc biệt', () => {
    for (const shortcut of ['Camon', 'cảmơn', 'cam on', 'cam.on', 'cam/on']) {
      expect(validateCannedResponse({ shortcut, body: 'x' }).shortcut, shortcut).toBe(
        'Phím tắt chỉ gồm chữ thường không dấu, số, dấu - và dấu _.',
      );
    }
  });

  it('phím tắt trống báo lỗi nhập phím tắt', () => {
    expect(validateCannedResponse({ shortcut: '', body: 'x' }).shortcut).toBe('Hãy nhập phím tắt.');
  });

  it('dấu / ở đầu được bỏ trước khi kiểm', () => {
    expect(validateCannedResponse({ shortcut: '/camon', body: 'x' })).toEqual({});
  });

  it('nội dung chỉ có khoảng trắng là rỗng', () => {
    expect(validateCannedResponse({ shortcut: 'camon', body: '   \n ' }).body).toBe(
      'Hãy nhập nội dung mẫu.',
    );
  });

  it('nội dung 2.000 ký tự hợp lệ, 2.001 ký tự là lỗi', () => {
    expect(validateCannedResponse({ shortcut: 'camon', body: 'x'.repeat(2000) })).toEqual({});
    expect(validateCannedResponse({ shortcut: 'camon', body: 'x'.repeat(2001) }).body).toBe(
      'Nội dung tối đa 2.000 ký tự.',
    );
  });

  it('lỗi hai ô hiện cùng lúc', () => {
    expect(Object.keys(validateCannedResponse({ shortcut: '', body: '' })).sort()).toEqual([
      'body',
      'shortcut',
    ]);
  });
});

describe('stripLeadingSlashes', () => {
  it('chỉ bỏ dấu / ở đầu, giữ dấu / ở giữa để kiểm ký tự bắt lỗi', () => {
    expect(stripLeadingSlashes('//camon')).toBe('camon');
    expect(stripLeadingSlashes('ca/mon')).toBe('ca/mon');
  });
});

describe('mapCannedResponseServerErrors', () => {
  it('gắn lỗi theo khóa shortcut và body', () => {
    const error = new ApiError('VALIDATION_ERROR', 400, 'Dữ liệu không hợp lệ.', [
      { key: 'shortcut', message: 'Phím tắt sai.' },
      { key: 'body', message: 'Nội dung rỗng.' },
    ]);

    expect(mapCannedResponseServerErrors(error)).toEqual({
      fieldErrors: { shortcut: 'Phím tắt sai.', body: 'Nội dung rỗng.' },
      unmatched: [],
    });
  });

  it('khóa lạ vào danh sách lỗi chung', () => {
    const error = new ApiError('VALIDATION_ERROR', 400, 'Lỗi.', [
      { key: 'shared', message: 'Không đổi được loại mẫu.' },
    ]);

    expect(mapCannedResponseServerErrors(error)).toEqual({
      fieldErrors: {},
      unmatched: ['Không đổi được loại mẫu.'],
    });
  });

  it('lỗi không có details hiện nguyên câu của API', () => {
    const error = new ApiError('INTERNAL', 500, 'Máy chủ đang bận.');

    expect(mapCannedResponseServerErrors(error)).toEqual({
      fieldErrors: {},
      unmatched: ['Máy chủ đang bận.'],
    });
  });
});
