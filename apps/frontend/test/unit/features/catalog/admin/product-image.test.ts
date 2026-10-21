import { describe, expect, it } from 'vitest';
import {
  firstProductImage,
  resolveImageAlt,
} from '../../../../../src/features/catalog/admin/product-image';
import type { ProductImageDetail } from '../../../../../src/features/catalog/admin/product-form';

function image(id: string, position: number, alt = `Ảnh ${id}`): ProductImageDetail {
  return {
    id,
    publicId: `pickory/products/${id}`,
    alt,
    skuId: null,
    position,
    url: `https://res.cloudinary.com/demo/image/upload/pickory/products/${id}.jpg`,
  };
}

describe('firstProductImage', () => {
  it('mảng rỗng trả null', () => {
    expect(firstProductImage([])).toBeNull();
  });

  it('mảng không theo thứ tự thì trả ảnh có position nhỏ nhất', () => {
    const images = [image('b', 2), image('a', 0), image('c', 1)];

    expect(firstProductImage(images)!.id).toBe('a');
  });

  it('hai ảnh cùng position nhỏ nhất thì trả ảnh đứng trước trong mảng', () => {
    const images = [image('x', 3), image('first', 1), image('second', 1)];

    expect(firstProductImage(images)!.id).toBe('first');
  });

  it('mảng một ảnh trả chính ảnh đó', () => {
    expect(firstProductImage([image('only', 5)])!.id).toBe('only');
  });

  it('không đổi mảng đầu vào', () => {
    const images = [image('b', 2), image('a', 0)];

    firstProductImage(images);

    expect(images.map((item) => item.id)).toEqual(['b', 'a']);
  });
});

describe('resolveImageAlt', () => {
  it('lấy alt của ảnh', () => {
    expect(resolveImageAlt(image('a', 0, 'Mặt trước'), 'Điện thoại')).toBe('Mặt trước');
  });

  it('bỏ khoảng trắng hai đầu của alt', () => {
    expect(resolveImageAlt(image('a', 0, '  Mặt trước  '), 'Điện thoại')).toBe('Mặt trước');
  });

  it('alt rỗng hoặc toàn khoảng trắng thì dùng tên sản phẩm', () => {
    expect(resolveImageAlt(image('a', 0, ''), 'Điện thoại')).toBe('Điện thoại');
    expect(resolveImageAlt(image('a', 0, '   '), 'Điện thoại')).toBe('Điện thoại');
  });
});
