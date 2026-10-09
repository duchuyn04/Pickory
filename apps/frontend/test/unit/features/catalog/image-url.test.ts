import { describe, expect, it } from 'vitest';
import {
  CATALOG_IMAGE_WIDTHS,
  buildCloudinarySrcSet,
  withCloudinaryTransform,
} from '../../../../src/features/catalog/shared/image-url';

describe('withCloudinaryTransform', () => {
  it('chèn định dạng tự động, chất lượng tự động và chiều rộng ngay sau `/upload/`', () => {
    expect(
      withCloudinaryTransform(
        'https://res.cloudinary.com/demo/image/upload/pickory/products/mat-truoc',
        200,
      ),
    ).toBe(
      'https://res.cloudinary.com/demo/image/upload/f_auto,q_auto,w_200/pickory/products/mat-truoc',
    );
  });

  it('URL không phải ảnh Cloudinary thì giữ nguyên', () => {
    expect(withCloudinaryTransform('https://example.com/anh.jpg', 400)).toBe(
      'https://example.com/anh.jpg',
    );
  });

  it('URL rỗng vẫn rỗng, để ô ảnh hiện trạng thái chưa có ảnh', () => {
    expect(withCloudinaryTransform('', 200)).toBe('');
  });
});

describe('buildCloudinarySrcSet', () => {
  it('liệt kê bốn chiều rộng 200, 400, 800 và 1200 kèm bộ mô tả `w`', () => {
    const srcSet = buildCloudinarySrcSet(
      'https://res.cloudinary.com/demo/image/upload/pickory/products/mat-truoc',
    );

    expect(CATALOG_IMAGE_WIDTHS).toEqual([200, 400, 800, 1200]);
    expect(srcSet.split(', ')).toEqual([
      'https://res.cloudinary.com/demo/image/upload/f_auto,q_auto,w_200/pickory/products/mat-truoc 200w',
      'https://res.cloudinary.com/demo/image/upload/f_auto,q_auto,w_400/pickory/products/mat-truoc 400w',
      'https://res.cloudinary.com/demo/image/upload/f_auto,q_auto,w_800/pickory/products/mat-truoc 800w',
      'https://res.cloudinary.com/demo/image/upload/f_auto,q_auto,w_1200/pickory/products/mat-truoc 1200w',
    ]);
  });
});
