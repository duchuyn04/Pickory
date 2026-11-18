import { describe, expect, it } from 'vitest';
import {
  buildTransactionsExportPath,
  buildTransactionsPath,
} from '../../../../src/features/commerce/admin/payments/admin-payments-api';
import {
  buildAdminOrdersExportPath,
  buildAdminOrdersPath,
} from '../../../../src/features/commerce/admin/orders/admin-orders-api';
import {
  buildAdminProductsExportPath,
  buildAdminProductsPath,
} from '../../../../src/features/catalog/admin/products-path';
import {
  buildAdminCustomersExportPath,
  buildAdminCustomersPath,
} from '../../../../src/features/identity/admin/customers-path';

// Tách đường dẫn thành phần đường và bảng tham số, để so sánh không phụ thuộc thứ tự tham số.
function parsePath(path: string) {
  const url = new URL(path, 'http://localhost');
  return { pathname: url.pathname, query: Object.fromEntries(url.searchParams.entries()) };
}

const emptyOrderValues = {
  search: '',
  status: '',
  paymentStatus: '',
  paymentMethod: '',
  flag: '',
  from: '',
  to: '',
};

const fullOrderValues = {
  search: 'PK-1',
  status: 'delivered',
  paymentStatus: 'unpaid',
  paymentMethod: 'cod',
  flag: 'underpaid',
  from: '2026-10-01',
  to: '2026-10-31',
};

describe('đường dẫn danh sách và xuất của đơn hàng', () => {
  it('không có bộ lọc thì đường dẫn xuất không có query', () => {
    expect(buildAdminOrdersExportPath(emptyOrderValues)).toBe('/api/admin/exports/orders');
  });

  it('đủ bảy bộ lọc thì đường dẫn xuất mang đủ bảy tham số', () => {
    expect(parsePath(buildAdminOrdersExportPath(fullOrderValues))).toEqual({
      pathname: '/api/admin/exports/orders',
      query: fullOrderValues,
    });
  });

  it('từ khóa có & và # vẫn là một giá trị duy nhất của search', () => {
    const path = buildAdminOrdersExportPath({ ...emptyOrderValues, search: 'a&b#c d' });
    expect(parsePath(path).query).toEqual({ search: 'a&b#c d' });
  });

  it('đường dẫn xuất không có page, còn danh sách vẫn có page từ trang 2', () => {
    expect(buildAdminOrdersExportPath(fullOrderValues)).not.toMatch(/page=|limit=/);
    expect(parsePath(buildAdminOrdersPath(fullOrderValues, 2)).query).toEqual({
      ...fullOrderValues,
      page: '2',
    });
    expect(buildAdminOrdersPath(emptyOrderValues, 1)).toBe('/api/admin/orders');
  });
});

const emptyProductFilters = { search: '', status: '', categoryId: '', lowStock: false };

describe('đường dẫn danh sách và xuất của sản phẩm', () => {
  it('không có bộ lọc thì đường dẫn xuất không có query', () => {
    expect(buildAdminProductsExportPath(emptyProductFilters)).toBe('/api/admin/exports/products');
  });

  it('đủ bốn bộ lọc thì lowStock gửi chuỗi true', () => {
    const path = buildAdminProductsExportPath({
      search: 'ao',
      status: 'active',
      categoryId: 'cat-1',
      lowStock: true,
    });
    expect(parsePath(path)).toEqual({
      pathname: '/api/admin/exports/products',
      query: { search: 'ao', status: 'active', categoryId: 'cat-1', lowStock: 'true' },
    });
  });

  it('từ khóa có & và # vẫn là một giá trị duy nhất của search', () => {
    const path = buildAdminProductsExportPath({ ...emptyProductFilters, search: 'a&b#c' });
    expect(parsePath(path).query).toEqual({ search: 'a&b#c' });
  });

  it('từ khóa có khoảng trắng hai đầu bị cắt ở cả danh sách và đường dẫn xuất', () => {
    const filters = { ...emptyProductFilters, search: '  ao  ' };
    expect(parsePath(buildAdminProductsExportPath(filters)).query).toEqual({ search: 'ao' });
    expect(parsePath(buildAdminProductsPath(filters, 1)).query).toEqual({
      page: '1',
      limit: '20',
      search: 'ao',
    });
  });

  it('từ khóa toàn khoảng trắng thì không gửi search ở cả danh sách và đường dẫn xuất', () => {
    const filters = { ...emptyProductFilters, search: '   ' };
    expect(buildAdminProductsExportPath(filters)).toBe('/api/admin/exports/products');
    expect(parsePath(buildAdminProductsPath(filters, 1)).query).toEqual({
      page: '1',
      limit: '20',
    });
  });

  it('đường dẫn xuất không có page và limit, còn danh sách luôn có cả hai', () => {
    const filters = { ...emptyProductFilters, lowStock: true };
    expect(buildAdminProductsExportPath(filters)).not.toMatch(/page=|limit=/);
    expect(parsePath(buildAdminProductsPath(filters, 3)).query).toEqual({
      page: '3',
      limit: '20',
      lowStock: 'true',
    });
  });
});

const emptyCustomerValues = { search: '', status: '', emailVerified: '' };

describe('đường dẫn danh sách và xuất của khách hàng', () => {
  it('không có bộ lọc thì đường dẫn xuất không có query', () => {
    expect(buildAdminCustomersExportPath(emptyCustomerValues)).toBe('/api/admin/exports/customers');
  });

  it('đủ ba bộ lọc thì đường dẫn xuất mang đủ ba tham số', () => {
    const values = { search: 'an', status: 'locked', emailVerified: 'false' };
    expect(parsePath(buildAdminCustomersExportPath(values))).toEqual({
      pathname: '/api/admin/exports/customers',
      query: values,
    });
  });

  it('từ khóa có & và # vẫn là một giá trị duy nhất của search', () => {
    const path = buildAdminCustomersExportPath({ ...emptyCustomerValues, search: 'a&b#c' });
    expect(parsePath(path).query).toEqual({ search: 'a&b#c' });
  });

  it('đường dẫn xuất không có page, còn danh sách có page từ trang 2', () => {
    const values = { ...emptyCustomerValues, status: 'active' };
    expect(buildAdminCustomersExportPath(values)).not.toMatch(/page=|limit=/);
    expect(parsePath(buildAdminCustomersPath(values, 2)).query).toEqual({
      status: 'active',
      page: '2',
    });
    expect(buildAdminCustomersPath(emptyCustomerValues, 1)).toBe('/api/admin/customers');
  });
});

describe('đường dẫn danh sách và xuất của giao dịch thanh toán', () => {
  it('trạng thái khớp cụ thể và orderFlag đều được gửi', () => {
    const path = buildTransactionsExportPath({ matchStatus: 'unmatched', orderFlag: 'overpaid' });
    expect(parsePath(path)).toEqual({
      pathname: '/api/admin/exports/payment-transactions',
      query: { matchStatus: 'unmatched', orderFlag: 'overpaid' },
    });
  });

  it('chọn Tất cả (all) thì không gửi matchStatus', () => {
    const path = buildTransactionsExportPath({ matchStatus: 'all', orderFlag: '' });
    expect(path).toBe('/api/admin/exports/payment-transactions');
  });

  it('đường dẫn xuất không có page, còn danh sách có page từ trang 2', () => {
    const filters = { matchStatus: 'matched', orderFlag: '' };
    expect(buildTransactionsExportPath(filters)).not.toMatch(/page=|limit=/);
    expect(parsePath(buildTransactionsPath(filters, 2)).query).toEqual({
      matchStatus: 'matched',
      page: '2',
    });
  });
});
