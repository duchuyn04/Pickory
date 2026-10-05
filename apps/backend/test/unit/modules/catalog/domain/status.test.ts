import { describe, expect, it } from 'vitest';
import type { AttributeDefinition } from '@pickory/shared';
import {
  canTransition,
  checkPublishable,
  type ProductPublishableInput,
} from '../../../../../src/modules/catalog/domain/status';

describe('status domain', () => {
  const definitions: AttributeDefinition[] = [
    {
      key: 'ram_gb',
      label: 'RAM',
      group: 'Bộ nhớ',
      type: 'number',
      required: true,
      filterable: true,
    },
  ];

  const validProduct: ProductPublishableInput = {
    specs: { ram_gb: 16 },
    attributeDefinitions: definitions,
    images: [{ id: 'img-1', publicId: 'catalog/prod-1' }],
    skus: [
      {
        id: 'sku-1',
        code: 'SKU-001',
        enabled: true,
        price: 20000000,
        weightGrams: 2000,
        lengthCm: 35,
        widthCm: 25,
        heightCm: 2,
      },
    ],
  };

  describe('checkPublishable', () => {
    it('chấp nhận sản phẩm có đầy đủ ảnh, thông số bắt buộc và biến thể hợp lệ', () => {
      const result = checkPublishable(validProduct);
      expect(result.publishable).toBe(true);
      expect(result.reasons).toHaveLength(0);
    });

    it('báo lỗi khi thiếu thông số bắt buộc', () => {
      const result = checkPublishable({
        ...validProduct,
        specs: {},
      });
      expect(result.publishable).toBe(false);
      expect(result.reasons.some((r) => r.includes('thông số'))).toBe(true);
    });

    it('báo lỗi khi không có ảnh nào', () => {
      const result = checkPublishable({
        ...validProduct,
        images: [],
      });
      expect(result.publishable).toBe(false);
      expect(result.reasons.some((r) => r.includes('ảnh'))).toBe(true);
    });

    it('báo lỗi khi không có biến thể nào đang bật', () => {
      const result = checkPublishable({
        ...validProduct,
        skus: validProduct.skus.map((sku) => ({ ...sku, enabled: false })),
      });
      expect(result.publishable).toBe(false);
      expect(result.reasons.some((r) => r.includes('biến thể đang bật'))).toBe(true);
    });

    it('báo lỗi khi biến thể đang bật có giá không hợp lệ', () => {
      const result = checkPublishable({
        ...validProduct,
        skus: [{ ...validProduct.skus[0]!, price: 0 }],
      });
      expect(result.publishable).toBe(false);
      expect(result.reasons.some((r) => r.includes('giá'))).toBe(true);
    });

    it('báo lỗi khi biến thể đang bật thiếu cân nặng hoặc kích thước đóng gói', () => {
      const missingWeight = checkPublishable({
        ...validProduct,
        skus: [{ ...validProduct.skus[0]!, weightGrams: null }],
      });
      expect(missingWeight.publishable).toBe(false);
      expect(missingWeight.reasons.some((r) => r.includes('cân nặng'))).toBe(true);

      const missingLength = checkPublishable({
        ...validProduct,
        skus: [{ ...validProduct.skus[0]!, lengthCm: 0 }],
      });
      expect(missingLength.publishable).toBe(false);
      expect(missingLength.reasons.some((r) => r.includes('kích thước'))).toBe(true);
    });

    it('báo đầy đủ danh sách lý do khi vi phạm nhiều điều kiện cùng lúc', () => {
      const result = checkPublishable({
        specs: {},
        attributeDefinitions: definitions,
        images: [],
        skus: [],
      });
      expect(result.publishable).toBe(false);
      expect(result.reasons.length).toBeGreaterThanOrEqual(3);
    });
  });

  describe('canTransition', () => {
    it('cho phép chuyển từ draft sang active', () => {
      expect(canTransition('draft', 'active')).toBe(true);
    });

    it('từ chối chuyển từ draft sang hidden hoặc discontinued', () => {
      expect(canTransition('draft', 'hidden')).toBe(false);
      expect(canTransition('draft', 'discontinued')).toBe(false);
    });

    it('cho phép ẩn hoặc ngừng kinh doanh sản phẩm đang active', () => {
      expect(canTransition('active', 'hidden')).toBe(true);
      expect(canTransition('active', 'discontinued')).toBe(true);
    });

    it('cho phép mở lại hoặc ngừng kinh doanh sản phẩm đang hidden', () => {
      expect(canTransition('hidden', 'active')).toBe(true);
      expect(canTransition('hidden', 'discontinued')).toBe(true);
    });

    it('cho phép mở bán lại sản phẩm discontinued', () => {
      expect(canTransition('discontinued', 'active')).toBe(true);
    });

    it('từ chối chuyển ngược về draft sau khi đã phát hành', () => {
      expect(canTransition('active', 'draft')).toBe(false);
      expect(canTransition('hidden', 'draft')).toBe(false);
      expect(canTransition('discontinued', 'draft')).toBe(false);
    });

    it('cho phép giữ nguyên trạng thái hiện tại', () => {
      expect(canTransition('active', 'active')).toBe(true);
      expect(canTransition('draft', 'draft')).toBe(true);
    });
  });
});
