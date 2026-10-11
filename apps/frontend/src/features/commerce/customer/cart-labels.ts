import type { Cart, CartLine } from './cart-api';

// Nhãn tiếng Việt của lý do dòng không mua được. Dòng mua được thì trả null.
export function unavailableLabel(line: CartLine): string | null {
  switch (line.unavailableReason) {
    case 'sku_disabled':
      return 'Phiên bản này đã ngừng bán';
    case 'product_unavailable':
      return 'Sản phẩm đã ngừng kinh doanh';
    case 'out_of_stock':
      return 'Hết hàng';
    case 'insufficient_stock':
      return `Chỉ còn ${line.stock ?? 0} sản phẩm`;
    case 'removed':
      return 'Sản phẩm không còn bán';
    case null:
      return null;
  }
}

// Hàng đã hết, đã tắt hoặc ngừng kinh doanh thì không cho tăng, nhưng khách vẫn giảm được.
// Dòng thiếu một phần tồn kho vẫn cho tăng, vì khách tự chỉnh số lượng về mức phù hợp.
export function canIncreaseQuantity(line: CartLine): boolean {
  return (
    line.unavailableReason !== 'sku_disabled' &&
    line.unavailableReason !== 'product_unavailable' &&
    line.unavailableReason !== 'out_of_stock'
  );
}

// Chỉ giỏ có dòng và mọi dòng mua được mới sang bước đặt hàng.
export function canCheckout(cart: Cart): boolean {
  return cart.items.length > 0 && !cart.hasUnavailableItems;
}

// Lời giải thích đặt cạnh nút đặt hàng khi nút tắt. Trả null khi nút dùng được.
export function checkoutHint(cart: Cart): string | null {
  if (cart.items.length === 0) return 'Giỏ hàng đang trống.';
  if (cart.hasUnavailableItems) {
    return 'Hãy xóa hoặc đổi các sản phẩm không mua được trước khi đặt hàng.';
  }
  return null;
}

// Đọc ô số lượng. Chỉ nhận số nguyên từ 1 trở lên, khớp luật của backend.
export function parseQuantityInput(raw: string): number | null {
  const trimmed = raw.trim();
  if (!/^\d+$/.test(trimmed)) return null;
  const quantity = Number(trimmed);
  return quantity >= 1 ? quantity : null;
}

// Tên truy cập của nút giỏ trên header. Số đếm là số dòng, khớp badge.
export function cartButtonLabel(lineCount: number): string {
  return lineCount === 0 ? 'Giỏ hàng, trống' : `Giỏ hàng, ${lineCount} sản phẩm`;
}
