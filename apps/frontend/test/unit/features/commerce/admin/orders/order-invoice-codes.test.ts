import { describe, expect, it } from 'vitest';
import {
  buildOrderInvoicesApiPath,
  parseInvoiceOrderCodes,
} from '../../../../../../src/features/commerce/admin/orders/order-invoice-codes';

describe('tách danh sách mã đơn in hóa đơn từ URL', () => {
  it('đọc một mã đơn từ tham số codes', () => {
    expect(parseInvoiceOrderCodes('?codes=PK7KQ2M9XA')).toEqual(['PK7KQ2M9XA']);
  });

  it('đọc nhiều mã đơn theo đúng thứ tự', () => {
    expect(parseInvoiceOrderCodes('?codes=PK7KQ2M9XA,PK3HN8RT2B,PK9AA111BB')).toEqual([
      'PK7KQ2M9XA',
      'PK3HN8RT2B',
      'PK9AA111BB',
    ]);
  });

  it('cắt khoảng trắng hai đầu từng mã và bỏ phần tử rỗng', () => {
    expect(parseInvoiceOrderCodes('?codes=%20PK7KQ2M9XA%20,%20,PK3HN8RT2B,')).toEqual([
      'PK7KQ2M9XA',
      'PK3HN8RT2B',
    ]);
  });

  it('bỏ mã trùng và giữ lần xuất hiện đầu tiên', () => {
    expect(
      parseInvoiceOrderCodes('?codes=PK3HN8RT2B,PK7KQ2M9XA,PK3HN8RT2B,PK9AA111BB,PK7KQ2M9XA'),
    ).toEqual(['PK3HN8RT2B', 'PK7KQ2M9XA', 'PK9AA111BB']);
  });

  it('trả mảng rỗng khi codes chỉ có khoảng trắng hoặc dấu phẩy', () => {
    expect(parseInvoiceOrderCodes('?codes=')).toEqual([]);
    expect(parseInvoiceOrderCodes('?codes=%20,%20,')).toEqual([]);
  });

  it('trả mảng rỗng khi thiếu tham số codes', () => {
    expect(parseInvoiceOrderCodes('')).toEqual([]);
    expect(parseInvoiceOrderCodes('?other=1')).toEqual([]);
  });
});

describe('dựng đường dẫn API dữ liệu in hóa đơn', () => {
  it('nối các mã bằng dấu phẩy và mã hóa từng mã', () => {
    expect(buildOrderInvoicesApiPath(['PK7KQ2M9XA'])).toBe(
      '/api/admin/order-invoices?codes=PK7KQ2M9XA',
    );
    expect(buildOrderInvoicesApiPath(['PK7KQ2M9XA', 'PK3HN8RT2B'])).toBe(
      '/api/admin/order-invoices?codes=PK7KQ2M9XA,PK3HN8RT2B',
    );
    expect(buildOrderInvoicesApiPath(['PK 1', 'PK&2'])).toBe(
      '/api/admin/order-invoices?codes=PK%201,PK%262',
    );
  });
});
