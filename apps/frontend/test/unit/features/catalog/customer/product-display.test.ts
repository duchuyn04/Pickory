import { describe, expect, it } from 'vitest';
import type { ProductListItem } from '../../../../../src/features/catalog/customer/catalog-api';
import {
  formatDiscountPercent,
  formatProductPrice,
  getProductBadges,
} from '../../../../../src/features/catalog/customer/product-display';

function item(change: Partial<ProductListItem>): ProductListItem {
  return {
    id: 'p1',
    slug: 'laptop-1',
    name: 'Laptop 1',
    brand: { id: 'b1', name: 'ASUS', slug: 'asus' },
    minPrice: 21490000,
    inStock: true,
    status: 'active',
    image: null,
    ...change,
  };
}

describe('giá sản phẩm', () => {
  it('format vi-VN có đơn vị đồng', () => {
    expect(formatProductPrice(21490000)).toBe('21.490.000 đ');
  });

  it('giá null hiện "Liên hệ"', () => {
    expect(formatProductPrice(null)).toBe('Liên hệ');
  });
});

describe('nhãn trạng thái của thẻ sản phẩm', () => {
  it('sản phẩm đang bán và còn hàng không có nhãn', () => {
    expect(getProductBadges(item({}))).toEqual([]);
  });

  it('hết hàng hiện "Hết hàng"', () => {
    expect(getProductBadges(item({ inStock: false })).map((badge) => badge.label)).toEqual([
      'Hết hàng',
    ]);
  });

  it('ngừng kinh doanh hiện "Ngừng kinh doanh"', () => {
    expect(getProductBadges(item({ status: 'discontinued' })).map((badge) => badge.label)).toEqual([
      'Ngừng kinh doanh',
    ]);
  });

  it('vừa ngừng kinh doanh vừa hết hàng hiện cả hai nhãn', () => {
    expect(
      getProductBadges(item({ status: 'discontinued', inStock: false })).map(
        (badge) => badge.label,
      ),
    ).toEqual(['Ngừng kinh doanh', 'Hết hàng']);
  });
});

describe('formatDiscountPercent', () => {
  it('tính phần trăm giảm làm tròn từ giá niêm yết', () => {
    expect(formatDiscountPercent(21_000_000, 25_000_000)).toBe(16);
  });

  it('trả null khi không có giá niêm yết', () => {
    expect(formatDiscountPercent(21_000_000, null)).toBeNull();
  });

  it('trả null khi giá niêm yết bằng hoặc thấp hơn giá bán', () => {
    expect(formatDiscountPercent(21_000_000, 21_000_000)).toBeNull();
    expect(formatDiscountPercent(21_000_000, 20_000_000)).toBeNull();
  });
});
