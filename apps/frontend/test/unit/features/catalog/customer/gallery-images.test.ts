import { describe, expect, it } from 'vitest';
import { selectGalleryImages } from '../../../../../src/features/catalog/customer/gallery-images';
import {
  buildProductDetail,
  imageBlack,
  imageShared,
  imageSilver,
  skuBlack256,
  skuBlack512,
  skuSilver256,
} from './product-fixtures';

const product = buildProductDetail();

describe('selectGalleryImages', () => {
  it('đặt ảnh của màu đang chọn trước ảnh chung và ẩn ảnh riêng của màu khác', () => {
    expect(selectGalleryImages(product, skuBlack256)).toEqual([imageBlack, imageShared]);
  });

  it('hiện ảnh riêng của một SKU cho mọi biến thể cùng màu', () => {
    // SKU Đen 512GB không có ảnh riêng, nhưng cùng màu Đen với SKU có ảnh.
    expect(selectGalleryImages(product, skuBlack512)).toEqual([imageBlack, imageShared]);
  });

  it('đổi sang màu khác thì đổi ảnh riêng theo', () => {
    expect(selectGalleryImages(product, skuSilver256)).toEqual([imageSilver, imageShared]);
  });

  it('không có tùy chọn màu thì ảnh riêng chỉ hiện cho đúng SKU đó', () => {
    const noColor = buildProductDetail({
      options: [{ name: 'Bộ nhớ', values: ['256GB', '512GB'] }],
    });
    expect(selectGalleryImages(noColor, skuBlack256)).toEqual([imageBlack, imageShared]);
    expect(selectGalleryImages(noColor, skuSilver256)).toEqual([imageSilver, imageShared]);
    expect(selectGalleryImages(noColor, skuBlack512)).toEqual([imageShared]);
  });

  it('nhận tùy chọn màu không phân biệt hoa thường', () => {
    const lowerCase = buildProductDetail({
      options: [{ name: 'màu sắc', values: ['Đen', 'Bạc'] }],
      skus: product.skus.map((sku) => ({
        ...sku,
        optionValues: { 'màu sắc': sku.optionValues['Màu'] ?? '' },
      })),
    });
    // SKU đen 512GB cùng màu với SKU có ảnh riêng, nên chỉ đúng khi nhận ra "màu sắc" là tùy chọn màu.
    expect(selectGalleryImages(lowerCase, lowerCase.skus[1]!)).toContainEqual(imageBlack);
  });

  it('ẩn ảnh riêng của SKU đã tắt vì không còn SKU để suy ra màu', () => {
    const orphan = { ...imageSilver, skuId: 'sku-da-tat' };
    const withOrphan = buildProductDetail({ images: [orphan, imageShared] });
    expect(selectGalleryImages(withOrphan, skuBlack256)).toEqual([imageShared]);
  });

  it('chỉ trả ảnh chung khi chưa có SKU được chọn', () => {
    expect(selectGalleryImages(product, null)).toEqual([imageShared]);
  });
});
