import { describe, expect, it } from 'vitest';
import {
  buildCancellationSlices,
  buildPaymentMethodSlices,
  buildProductSlices,
  CANCELLATION_REASON_GROUPS,
  countCancelledOrders,
  countPaymentMethodOrders,
  MAX_PRODUCT_SLICES,
  OTHER_PRODUCT_KEY,
  OTHER_PRODUCT_LABEL,
  PAYMENT_METHOD_GROUPS,
  splitTopProducts,
  totalCancelledOrders,
} from '../../../../../src/features/admin-console/reports/report-groups';
import {
  REPORT_OTHER_COLOR,
  REPORT_SERIES_COLORS,
} from '../../../../../src/features/admin-console/reports/report-colors';
import type { SalesReportTopProduct } from '../../../../../src/features/admin-console/reports/reports-api';

describe('nhóm phương thức thanh toán', () => {
  it('giữ một màu cố định cho mỗi phương thức', () => {
    expect(PAYMENT_METHOD_GROUPS.map((group) => [group.key, group.label, group.color])).toEqual([
      ['cod', 'COD', REPORT_SERIES_COLORS[0]],
      ['sepay', 'SePay', REPORT_SERIES_COLORS[1]],
    ]);
  });

  it('dựng lát theo số đơn đã thanh toán của từng phương thức', () => {
    const slices = buildPaymentMethodSlices({ cod: 5, sepay: 1 });

    expect(slices).toEqual([
      { key: 'cod', label: 'COD', value: 5, fill: REPORT_SERIES_COLORS[0] },
      { key: 'sepay', label: 'SePay', value: 1, fill: REPORT_SERIES_COLORS[1] },
    ]);
  });

  it('bỏ phương thức không có đơn vì lát rỗng không vẽ được', () => {
    const slices = buildPaymentMethodSlices({ cod: 0, sepay: 4 });

    expect(slices.map((slice) => slice.key)).toEqual(['sepay']);
  });

  it('đếm số đơn của một phương thức trong một ngày', () => {
    const counts = { cod: 3, sepay: 1 };

    expect(countPaymentMethodOrders('cod', counts)).toBe(3);
    expect(countPaymentMethodOrders('sepay', counts)).toBe(1);
  });
});

describe('nhóm lý do hủy', () => {
  it('giữ thứ tự khách hủy, shop hủy, quá hạn thanh toán', () => {
    expect(CANCELLATION_REASON_GROUPS.map((group) => [group.key, group.label])).toEqual([
      ['customer', 'Khách hủy'],
      ['shop', 'Shop hủy'],
      ['payment_expired', 'Quá hạn thanh toán'],
    ]);
  });

  it('dựng ba lát theo số đơn hủy của từng lý do', () => {
    const slices = buildCancellationSlices({ byCustomer: 3, byShop: 1, paymentExpired: 2 });

    expect(slices).toEqual([
      { key: 'customer', label: 'Khách hủy', value: 3, fill: REPORT_SERIES_COLORS[0] },
      { key: 'shop', label: 'Shop hủy', value: 1, fill: REPORT_SERIES_COLORS[1] },
      {
        key: 'payment_expired',
        label: 'Quá hạn thanh toán',
        value: 2,
        fill: REPORT_SERIES_COLORS[2],
      },
    ]);
  });

  it('bỏ lý do không có đơn hủy', () => {
    const slices = buildCancellationSlices({ byCustomer: 0, byShop: 0, paymentExpired: 2 });

    expect(slices.map((slice) => slice.key)).toEqual(['payment_expired']);
  });

  it('tổng số đơn hủy của một kỳ bằng tổng ba lý do', () => {
    expect(totalCancelledOrders({ byCustomer: 1, byShop: 2, paymentExpired: 3 })).toBe(6);
  });

  it('đếm số đơn hủy của một lý do trong một ngày', () => {
    const counts = { byCustomer: 1, byShop: 2, paymentExpired: 3 };

    expect(countCancelledOrders('customer', counts)).toBe(1);
    expect(countCancelledOrders('shop', counts)).toBe(2);
    expect(countCancelledOrders('payment_expired', counts)).toBe(3);
  });
});

function product(
  productId: string,
  name: string,
  quantity: number,
  revenue: number,
): SalesReportTopProduct {
  return { productId, name, brandName: 'Hãng Một', revenue, quantity };
}

describe('nhóm sản phẩm bán ra', () => {
  const products = [
    product('p1', 'Máy A', 70, 7_000_000),
    product('p2', 'Máy B', 60, 6_000_000),
    product('p3', 'Máy C', 50, 5_000_000),
    product('p4', 'Máy D', 40, 4_000_000),
    product('p5', 'Máy E', 30, 3_000_000),
    product('p6', 'Máy F', 20, 2_000_000),
    product('p7', 'Máy G', 10, 1_000_000),
  ];

  it('lấy năm sản phẩm bán nhiều nhất và gộp phần còn lại vào nhóm Khác', () => {
    const split = splitTopProducts(products);

    expect(MAX_PRODUCT_SLICES).toBe(5);
    expect(split.top.map((item) => item.productId)).toEqual(['p1', 'p2', 'p3', 'p4', 'p5']);
    expect(split.rest.map((item) => item.productId)).toEqual(['p6', 'p7']);
    expect(buildProductSlices(split)).toEqual([
      { key: 'p1', label: 'Máy A', value: 70, fill: REPORT_SERIES_COLORS[0] },
      { key: 'p2', label: 'Máy B', value: 60, fill: REPORT_SERIES_COLORS[1] },
      { key: 'p3', label: 'Máy C', value: 50, fill: REPORT_SERIES_COLORS[2] },
      { key: 'p4', label: 'Máy D', value: 40, fill: REPORT_SERIES_COLORS[3] },
      { key: 'p5', label: 'Máy E', value: 30, fill: REPORT_SERIES_COLORS[4] },
      { key: OTHER_PRODUCT_KEY, label: OTHER_PRODUCT_LABEL, value: 30, fill: REPORT_OTHER_COLOR },
    ]);
  });

  it('sản phẩm bằng số lượng thì xếp theo doanh thu rồi theo tên', () => {
    const tied = [
      product('b', 'Máy Bê', 60, 5_000_000),
      product('a', 'Máy A', 60, 6_000_000),
      product('c', 'Máy Xê', 60, 5_000_000),
    ];

    const split = splitTopProducts(tied);

    // Doanh thu cao đứng trước, cùng doanh thu thì tên theo thứ tự chữ cái.
    expect(split.top.map((item) => item.name)).toEqual(['Máy A', 'Máy Bê', 'Máy Xê']);
    expect(split.rest).toEqual([]);
  });

  it('không quá năm sản phẩm thì không có nhóm Khác', () => {
    const few = products.slice(0, 3);

    expect(buildProductSlices(splitTopProducts(few)).map((slice) => slice.key)).toEqual([
      'p1',
      'p2',
      'p3',
    ]);
  });

  it('sản phẩm không có tên dùng nhãn thay thế để lát vẫn đọc được', () => {
    const slices = buildProductSlices(splitTopProducts([product('p9', '', 5, 500_000)]));

    expect(slices[0]?.label).toBe('Không rõ tên sản phẩm');
  });
});
