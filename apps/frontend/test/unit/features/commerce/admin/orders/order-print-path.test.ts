import { describe, expect, it } from 'vitest';
import { buildOrderPrintPath } from '../../../../../../src/features/commerce/admin/orders/order-print-path';

describe('buildOrderPrintPath', () => {
  it('trả đường dẫn gốc khi danh sách mã đơn rỗng', () => {
    expect(buildOrderPrintPath([])).toBe('/admin/orders/print');
  });

  it('dựng đúng đường dẫn với một mã đơn', () => {
    expect(buildOrderPrintPath(['PK7KQ2M9XA'])).toBe('/admin/orders/print?codes=PK7KQ2M9XA');
  });

  it('nối nhiều mã bằng dấu phẩy và giữ nguyên thứ tự truyền vào', () => {
    expect(buildOrderPrintPath(['PK-03', 'PK-01', 'PK-02'])).toBe(
      '/admin/orders/print?codes=PK-03,PK-01,PK-02',
    );
  });

  it('mã hóa ký tự đặc biệt trong từng mã nhưng giữ nguyên dấu phẩy phân cách', () => {
    expect(buildOrderPrintPath(['PK/01 A', 'PK&02=B'])).toBe(
      '/admin/orders/print?codes=PK%2F01%20A,PK%2602%3DB',
    );
  });
});
