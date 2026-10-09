import { describe, expect, it } from 'vitest';
import { ApiError } from '../../../../src/shared/api';
import {
  PRODUCT_STATUSES,
  getAllowedTargetStatuses,
  getStatusActionLabel,
  readNotPublishableReasons,
  type ProductStatus,
} from '../../../../src/features/catalog/admin/product-status';

describe('getAllowedTargetStatuses', () => {
  // Bảng này phải khớp `AllowedTransitions` trong `ProductPublishing.cs` của backend.
  const expectedTargets: Record<ProductStatus, ProductStatus[]> = {
    draft: ['active'],
    active: ['hidden', 'discontinued'],
    hidden: ['active', 'discontinued'],
    discontinued: ['active', 'hidden'],
  };

  it('liệt kê đủ bốn trạng thái nguồn', () => {
    expect([...PRODUCT_STATUSES].sort()).toEqual(Object.keys(expectedTargets).sort());
  });

  it.each(PRODUCT_STATUSES)('từ %s chỉ cho chuyển tới đúng các trạng thái hợp lệ', (from) => {
    expect(getAllowedTargetStatuses(from)).toEqual(expectedTargets[from]);
  });

  it('không cho chuyển sang chính trạng thái hiện tại', () => {
    for (const status of PRODUCT_STATUSES) {
      expect(getAllowedTargetStatuses(status)).not.toContain(status);
    }
  });

  it('sản phẩm đã phát hành không quay về bản nháp', () => {
    for (const status of ['active', 'hidden', 'discontinued'] as const) {
      expect(getAllowedTargetStatuses(status)).not.toContain('draft');
    }
  });

  it('trạng thái lạ từ API không có nút nào', () => {
    expect(getAllowedTargetStatuses('archived')).toEqual([]);
  });
});

describe('getStatusActionLabel', () => {
  it('đăng bán từ bản nháp', () => {
    expect(getStatusActionLabel('draft', 'active')).toBe('Đăng bán');
  });

  it.each(['hidden', 'discontinued'] as const)('bán lại từ %s', (from) => {
    expect(getStatusActionLabel(from, 'active')).toBe('Bán lại');
  });

  it.each(['active', 'discontinued'] as const)('ẩn sản phẩm từ %s', (from) => {
    expect(getStatusActionLabel(from, 'hidden')).toBe('Ẩn sản phẩm');
  });

  it.each(['active', 'hidden'] as const)('ngừng kinh doanh từ %s', (from) => {
    expect(getStatusActionLabel(from, 'discontinued')).toBe('Ngừng kinh doanh');
  });
});

describe('readNotPublishableReasons', () => {
  // Dạng thật của `details` lấy từ `ProductStatusTests.cs`: mảng chuỗi, không phải mảng đối tượng.
  const reasons = [
    'Sản phẩm phải có ít nhất 1 ảnh để đăng bán.',
    'SKU PHONE-01: thiếu cân nặng hoặc kích thước đóng gói.',
  ];

  it('trả danh sách lý do của lỗi 422 PRODUCT_NOT_PUBLISHABLE', () => {
    const error = new ApiError(
      'PRODUCT_NOT_PUBLISHABLE',
      422,
      'Sản phẩm chưa đủ điều kiện để đăng bán.',
      reasons as unknown as ApiError['details'],
    );
    expect(readNotPublishableReasons(error)).toEqual(reasons);
  });

  it('bỏ phần tử không phải chuỗi', () => {
    const error = new ApiError('PRODUCT_NOT_PUBLISHABLE', 422, 'Lỗi.', [
      'Lý do hợp lệ.',
      { key: 'x', message: 'Không phải chuỗi' },
    ] as unknown as ApiError['details']);
    expect(readNotPublishableReasons(error)).toEqual(['Lý do hợp lệ.']);
  });

  it('lỗi không có details trả danh sách rỗng', () => {
    expect(readNotPublishableReasons(new ApiError('PRODUCT_NOT_PUBLISHABLE', 422, 'Lỗi.'))).toEqual(
      [],
    );
  });

  it('lỗi mã khác không đọc details', () => {
    const error = new ApiError(
      'SPECS_INVALID',
      400,
      'Lỗi.',
      reasons as unknown as ApiError['details'],
    );
    expect(readNotPublishableReasons(error)).toEqual([]);
  });

  it('giá trị không phải ApiError trả danh sách rỗng', () => {
    expect(readNotPublishableReasons(new Error('mạng'))).toEqual([]);
    expect(readNotPublishableReasons(null)).toEqual([]);
  });
});
