import { describe, expect, it } from 'vitest';
import {
  cartButtonLabel,
  canCheckout,
  canIncreaseQuantity,
  checkoutHint,
  parseQuantityInput,
  unavailableLabel,
} from '../../../../../src/features/commerce/customer/cart-labels';
import { buildCart, buildCartLine, buildRemovedLine } from './cart-fixtures';

describe('unavailableLabel', () => {
  it.each([
    ['sku_disabled', 'Phiên bản này đã ngừng bán'],
    ['product_unavailable', 'Sản phẩm đã ngừng kinh doanh'],
    ['out_of_stock', 'Hết hàng'],
    ['removed', 'Sản phẩm không còn bán'],
  ] as const)('nhãn của %s', (reason, label) => {
    expect(unavailableLabel(buildCartLine({ unavailableReason: reason, purchasable: false }))).toBe(
      label,
    );
  });

  it('insufficient_stock ghi số còn lại theo tồn kho', () => {
    const line = buildCartLine({
      unavailableReason: 'insufficient_stock',
      purchasable: false,
      quantity: 4,
      stock: 2,
    });
    expect(unavailableLabel(line)).toBe('Chỉ còn 2 sản phẩm');
  });

  it('dòng mua được không có nhãn', () => {
    expect(unavailableLabel(buildCartLine())).toBeNull();
  });
});

describe('canCheckout và checkoutHint', () => {
  it('giỏ rỗng không đặt hàng được và có lời giải thích', () => {
    const cart = buildCart([]);
    expect(canCheckout(cart)).toBe(false);
    expect(checkoutHint(cart)).toBe('Giỏ hàng đang trống.');
  });

  it('giỏ có dòng không mua được thì tắt nút kèm lời giải thích', () => {
    const cart = buildCart([buildCartLine(), buildRemovedLine()]);
    expect(canCheckout(cart)).toBe(false);
    expect(checkoutHint(cart)).toBe(
      'Hãy xóa hoặc đổi các sản phẩm không mua được trước khi đặt hàng.',
    );
  });

  it('giỏ chỉ có dòng mua được thì cho đặt hàng và không có lời giải thích', () => {
    const cart = buildCart([buildCartLine()]);
    expect(canCheckout(cart)).toBe(true);
    expect(checkoutHint(cart)).toBeNull();
  });
});

describe('parseQuantityInput', () => {
  it.each([
    ['1', 1],
    ['12', 12],
    [' 3 ', 3],
  ])('nhận %s', (raw, expected) => {
    expect(parseQuantityInput(raw)).toBe(expected);
  });

  it.each(['', '0', '-1', '1.5', 'abc', '2e1'])('từ chối %s', (raw) => {
    expect(parseQuantityInput(raw)).toBeNull();
  });
});

describe('cartButtonLabel', () => {
  it('giỏ trống', () => {
    expect(cartButtonLabel(0)).toBe('Giỏ hàng, trống');
  });

  it('giỏ có dòng ghi số dòng', () => {
    expect(cartButtonLabel(3)).toBe('Giỏ hàng, 3 sản phẩm');
  });
});

describe('canIncreaseQuantity', () => {
  it.each(['sku_disabled', 'product_unavailable', 'out_of_stock'] as const)(
    'không cho tăng số lượng khi dòng %s',
    (reason) => {
      expect(
        canIncreaseQuantity(buildCartLine({ unavailableReason: reason, purchasable: false })),
      ).toBe(false);
    },
  );

  it('cho tăng với dòng mua được', () => {
    expect(canIncreaseQuantity(buildCartLine())).toBe(true);
  });

  it('vẫn cho tăng khi chỉ còn một phần tồn kho, để khách tự chỉnh về mức phù hợp', () => {
    const line = buildCartLine({
      unavailableReason: 'insufficient_stock',
      purchasable: false,
      stock: 2,
      quantity: 4,
    });
    expect(canIncreaseQuantity(line)).toBe(true);
  });
});
