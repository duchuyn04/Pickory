import { describe, expect, it } from 'vitest';
import {
  computeDisplayPrice,
  validatePrice,
  type SkuPricingInput,
} from '../../../../../src/modules/catalog/domain/pricing';

describe('pricing domain', () => {
  describe('computeDisplayPrice', () => {
    it('lấy giá thấp nhất trong các biến thể đang bật và còn hàng', () => {
      const skus: SkuPricingInput[] = [
        { price: 20000000, stock: 5, enabled: true },
        { price: 18000000, stock: 3, enabled: true },
        { price: 15000000, stock: 0, enabled: true },
        { price: 12000000, stock: 10, enabled: false },
      ];
      const result = computeDisplayPrice(skus);
      expect(result.minPrice).toBe(18000000);
      expect(result.inStock).toBe(true);
    });

    it('lấy giá thấp nhất trong các biến thể đang bật khi toàn bộ hết hàng', () => {
      const skus: SkuPricingInput[] = [
        { price: 25000000, stock: 0, enabled: true },
        { price: 22000000, stock: 0, enabled: true },
        { price: 19000000, stock: 10, enabled: false },
      ];
      const result = computeDisplayPrice(skus);
      expect(result.minPrice).toBe(22000000);
      expect(result.inStock).toBe(false);
    });

    it('trả về null và inStock false khi không có biến thể nào đang bật', () => {
      const skus: SkuPricingInput[] = [
        { price: 30000000, stock: 10, enabled: false },
        { price: 28000000, stock: 5, enabled: false },
      ];
      const result = computeDisplayPrice(skus);
      expect(result.minPrice).toBeNull();
      expect(result.inStock).toBe(false);
    });

    it('trả về null và inStock false khi mảng biến thể rỗng', () => {
      const result = computeDisplayPrice([]);
      expect(result.minPrice).toBeNull();
      expect(result.inStock).toBe(false);
    });
  });

  describe('validatePrice', () => {
    it('chấp nhận giá bán là số nguyên dương và không có giá niêm yết', () => {
      const result = validatePrice(15000000);
      expect(result.valid).toBe(true);
    });

    it('chấp nhận giá bán và giá niêm yết khi giá niêm yết lớn hơn giá bán', () => {
      const result = validatePrice(15000000, 18000000);
      expect(result.valid).toBe(true);
    });

    it('từ chối giá bán bằng 0 hoặc số âm', () => {
      expect(validatePrice(0).valid).toBe(false);
      expect(validatePrice(-500000).valid).toBe(false);
    });

    it('từ chối giá bán là số thập phân', () => {
      expect(validatePrice(15000000.5).valid).toBe(false);
    });

    it('từ chối giá niêm yết nhỏ hơn hoặc bằng giá bán', () => {
      expect(validatePrice(15000000, 15000000).valid).toBe(false);
      expect(validatePrice(15000000, 12000000).valid).toBe(false);
    });

    it('từ chối giá niêm yết là số thập phân', () => {
      expect(validatePrice(15000000, 18000000.5).valid).toBe(false);
    });
  });
});
