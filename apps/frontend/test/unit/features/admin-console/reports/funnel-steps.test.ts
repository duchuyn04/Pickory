import { describe, expect, it } from 'vitest';
import { funnelRows } from '../../../../../src/features/admin-console/reports/funnel-steps';

describe('dòng của phễu chuyển đổi', () => {
  it('đặt tên tiếng Việt cho ba bước theo đúng thứ tự API', () => {
    const rows = funnelRows([
      { metric: 'product_view', count: 120, conversionRate: null },
      { metric: 'add_to_cart', count: 30, conversionRate: 0.25 },
      { metric: 'order_placed', count: 6, conversionRate: 0.2 },
    ]);

    // Thứ tự bước theo đường mua hàng, không sắp lại theo số lượt.
    expect(rows.map((row) => row.label)).toEqual(['Xem sản phẩm', 'Thêm giỏ', 'Đặt đơn']);
    expect(rows.map((row) => row.count)).toEqual([120, 30, 6]);
  });

  it('hiện tỉ lệ chuyển so với bước trước dạng phần trăm', () => {
    const rows = funnelRows([
      { metric: 'product_view', count: 120, conversionRate: null },
      { metric: 'add_to_cart', count: 30, conversionRate: 0.25 },
      { metric: 'order_placed', count: 6, conversionRate: 0.2 },
    ]);

    expect(rows[1]?.rateLabel).toBe('25%');
    expect(rows[2]?.rateLabel).toBe('20%');
  });

  it('bước đầu không có bước trước nên ghi rõ là bước đầu', () => {
    const rows = funnelRows([
      { metric: 'product_view', count: 120, conversionRate: null },
      { metric: 'add_to_cart', count: 30, conversionRate: 0.25 },
      { metric: 'order_placed', count: 6, conversionRate: 0.2 },
    ]);

    expect(rows[0]?.rateLabel).toBe('Bước đầu');
  });

  it('bước trước không có lượt thì ghi chưa có dữ liệu thay vì chia cho 0', () => {
    const rows = funnelRows([
      { metric: 'product_view', count: 0, conversionRate: null },
      { metric: 'add_to_cart', count: 0, conversionRate: null },
      { metric: 'order_placed', count: 0, conversionRate: null },
    ]);

    expect(rows[1]?.rateLabel).toBe('Chưa có dữ liệu');
    expect(rows[2]?.rateLabel).toBe('Chưa có dữ liệu');
  });
});
