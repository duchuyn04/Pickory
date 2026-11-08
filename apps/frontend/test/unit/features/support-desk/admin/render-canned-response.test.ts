import { describe, expect, it } from 'vitest';
import { renderCannedResponse } from '../../../../../src/features/support-desk/admin/render-canned-response';

describe('renderCannedResponse', () => {
  it('thay cả hai biến bằng giá trị truyền vào', () => {
    const rendered = renderCannedResponse('Cảm ơn {ten_khach} đã liên hệ về đơn {ma_don}.', {
      tenKhach: 'Lan',
      maDon: 'PK123',
    });

    expect(rendered).toBe('Cảm ơn Lan đã liên hệ về đơn PK123.');
  });

  it('giữ nguyên biến null và vẫn thay biến còn lại', () => {
    const rendered = renderCannedResponse('Cảm ơn {ten_khach}. Mình kiểm đơn {ma_don} nhé.', {
      tenKhach: null,
      maDon: 'PK123',
    });

    // Backend cũng giữ biến thiếu giá trị, nên hai phía thấy cùng một chuỗi.
    expect(rendered).toBe('Cảm ơn {ten_khach}. Mình kiểm đơn PK123 nhé.');
  });

  it('thay mọi lần lặp của cùng một biến', () => {
    const rendered = renderCannedResponse('{ten_khach} ơi, đơn của {ten_khach} đã xác nhận.', {
      tenKhach: 'Minh',
      maDon: null,
    });

    expect(rendered).toBe('Minh ơi, đơn của Minh đã xác nhận.');
  });

  it('không đổi mẫu không có biến', () => {
    const body = 'Sản phẩm được bảo hành 12 tháng.';

    expect(renderCannedResponse(body, { tenKhach: null, maDon: null })).toBe(body);
  });
});
