import type { Cart, CartLine } from '../../../../../src/features/commerce/customer/cart-api';

// Dòng giỏ mua được. Test ghi đè từng trường cần khác.
export function buildCartLine(overrides: Partial<CartLine> = {}): CartLine {
  return {
    skuId: 'sku-1',
    quantity: 1,
    productId: 'product-1',
    productName: 'Laptop Kaze',
    productSlug: 'laptop-kaze',
    optionValues: { Màu: 'Đen' },
    image: {
      url: 'https://res.cloudinary.com/demo/image/upload/pickory/products/den',
      alt: 'Ảnh màu đen',
      skuId: 'sku-1',
    },
    unitPrice: 20_000_000,
    compareAtPrice: null,
    lineTotal: 20_000_000,
    stock: 10,
    purchasable: true,
    unavailableReason: null,
    ...overrides,
  };
}

export function buildCart(items: CartLine[] = [], overrides: Partial<Cart> = {}): Cart {
  const subtotal = items
    .filter((line) => line.purchasable)
    .reduce((total, line) => total + (line.lineTotal ?? 0), 0);
  return {
    items,
    subtotal,
    hasUnavailableItems: items.some((line) => !line.purchasable),
    maxQuantityPerSku: 5,
    maxCartLines: 20,
    ...overrides,
  };
}

// Dòng không còn dữ liệu catalog: mọi trường sản phẩm và giá là null.
export function buildRemovedLine(skuId = 'sku-gone', quantity = 1): CartLine {
  return {
    skuId,
    quantity,
    productId: null,
    productName: null,
    productSlug: null,
    optionValues: null,
    image: null,
    unitPrice: null,
    compareAtPrice: null,
    lineTotal: null,
    stock: null,
    purchasable: false,
    unavailableReason: 'removed',
  };
}
