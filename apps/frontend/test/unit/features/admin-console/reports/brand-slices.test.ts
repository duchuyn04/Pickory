import { describe, expect, it } from 'vitest';
import {
  buildBrandSlices,
  OTHER_BRAND_KEY,
  totalBrandRevenue,
} from '../../../../../src/features/admin-console/reports/brand-slices';
import type { SalesReportBrand } from '../../../../../src/features/admin-console/reports/reports-api';

function brand(
  brandId: string | null,
  brandName: string | null,
  revenue: number,
): SalesReportBrand {
  return { brandId, brandName, revenue };
}

describe('gộp thương hiệu thành lát biểu đồ tròn', () => {
  it('sắp lát theo doanh thu giảm dần', () => {
    const slices = buildBrandSlices([
      brand('b1', 'Hãng Một', 100),
      brand('b2', 'Hãng Hai', 300),
      brand('b3', 'Hãng Ba', 200),
    ]);

    expect(slices.map((slice) => slice.label)).toEqual(['Hãng Hai', 'Hãng Ba', 'Hãng Một']);
    expect(totalBrandRevenue(slices)).toBe(600);
  });

  it('quá sáu thương hiệu thì phần còn lại vào lát Khác', () => {
    const brands = Array.from({ length: 8 }, (_, index) =>
      brand(`b${index}`, `Hãng ${index}`, (8 - index) * 100),
    );

    const slices = buildBrandSlices(brands);

    expect(slices).toHaveLength(7);
    expect(slices.slice(0, 6).map((slice) => slice.label)).toEqual([
      'Hãng 0',
      'Hãng 1',
      'Hãng 2',
      'Hãng 3',
      'Hãng 4',
      'Hãng 5',
    ]);
    const other = slices[6];
    expect(other?.key).toBe(OTHER_BRAND_KEY);
    expect(other?.label).toBe('Khác');
    // Hai thương hiệu cuối có doanh thu 200 và 100.
    expect(other?.revenue).toBe(300);
  });

  it('sản phẩm không có thương hiệu vào cùng lát Khác', () => {
    const slices = buildBrandSlices([brand('b1', 'Hãng Một', 700), brand(null, null, 300)]);

    expect(slices.map((slice) => slice.label)).toEqual(['Hãng Một', 'Khác']);
    expect(slices[1]?.key).toBe(OTHER_BRAND_KEY);
    expect(slices[1]?.revenue).toBe(300);
  });

  it('bỏ thương hiệu không có doanh thu để không vẽ lát rỗng', () => {
    const slices = buildBrandSlices([brand('b1', 'Hãng Một', 0), brand('b2', 'Hãng Hai', 500)]);

    expect(slices.map((slice) => slice.label)).toEqual(['Hãng Hai']);
    expect(totalBrandRevenue(slices)).toBe(500);
  });

  it('tên thương hiệu rỗng dùng nhãn thay thế', () => {
    const slices = buildBrandSlices([brand('b1', '   ', 500)]);

    expect(slices[0]?.label).toBe('Không rõ thương hiệu');
  });

  it('tháng không có lát nào trả danh sách rỗng', () => {
    expect(buildBrandSlices([])).toEqual([]);
  });
});
