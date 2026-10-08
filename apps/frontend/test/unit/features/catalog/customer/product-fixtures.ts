import type {
  ProductDetail,
  ProductSkuInfo,
} from '../../../../../src/features/catalog/customer/catalog-api';

// Bốn SKU ghép từ hai tùy chọn "Màu" và "Bộ nhớ". Bản Đen 512GB hết hàng và bản Bạc 256GB có giá niêm yết.
export const skuBlack256: ProductSkuInfo = {
  id: 'sku-black-256',
  code: 'LAP-DEN-256',
  optionValues: { Màu: 'Đen', 'Bộ nhớ': '256GB' },
  price: 20_000_000,
  compareAtPrice: null,
  inStock: true,
};
export const skuBlack512: ProductSkuInfo = {
  id: 'sku-black-512',
  code: 'LAP-DEN-512',
  optionValues: { Màu: 'Đen', 'Bộ nhớ': '512GB' },
  price: 24_000_000,
  compareAtPrice: null,
  inStock: false,
};
export const skuSilver256: ProductSkuInfo = {
  id: 'sku-silver-256',
  code: 'LAP-BAC-256',
  optionValues: { Màu: 'Bạc', 'Bộ nhớ': '256GB' },
  price: 21_000_000,
  compareAtPrice: 25_000_000,
  inStock: true,
};
export const skuSilver512: ProductSkuInfo = {
  id: 'sku-silver-512',
  code: 'LAP-BAC-512',
  optionValues: { Màu: 'Bạc', 'Bộ nhớ': '512GB' },
  price: 26_000_000,
  compareAtPrice: null,
  inStock: true,
};

export const imageBlack = {
  url: 'https://res.cloudinary.com/demo/image/upload/pickory/products/den',
  alt: 'Ảnh màu đen',
  skuId: skuBlack256.id,
};
export const imageSilver = {
  url: 'https://res.cloudinary.com/demo/image/upload/pickory/products/bac',
  alt: 'Ảnh màu bạc',
  skuId: skuSilver256.id,
};
export const imageShared = {
  url: 'https://res.cloudinary.com/demo/image/upload/pickory/products/chung',
  alt: 'Ảnh chung',
  skuId: null,
};

export function buildProductDetail(overrides: Partial<ProductDetail> = {}): ProductDetail {
  return {
    id: 'product-1',
    slug: 'laptop-1',
    name: 'Laptop 1',
    status: 'active',
    shortDesc: '',
    description: { type: 'doc', content: [] },
    warrantyMonths: 12,
    brand: { id: 'brand-1', name: 'ASUS', slug: 'asus' },
    category: { id: 'cat-2', name: 'Laptop', slug: 'laptop' },
    parentCategory: { id: 'cat-1', name: 'Máy tính', slug: 'may-tinh' },
    specs: [],
    options: [
      { name: 'Màu', values: ['Đen', 'Bạc'] },
      { name: 'Bộ nhớ', values: ['256GB', '512GB'] },
    ],
    skus: [skuBlack256, skuBlack512, skuSilver256, skuSilver512],
    images: [imageBlack, imageSilver, imageShared],
    minPrice: 20_000_000,
    inStock: true,
    ...overrides,
  };
}
