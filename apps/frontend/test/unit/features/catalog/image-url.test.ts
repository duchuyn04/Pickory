import { describe, expect, it } from 'vitest';
import { withCloudinaryTransform } from '../../../../src/features/catalog/shared/image-url';

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
