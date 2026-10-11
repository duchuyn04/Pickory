import { apiRequest } from '../../../shared/api';

// Nhãn lý do một dòng giỏ không mua được. Chuỗi khớp `CartUnavailableReason` của backend.
export type UnavailableReason =
  'sku_disabled' | 'product_unavailable' | 'out_of_stock' | 'insufficient_stock' | 'removed';

export interface CartLineImage {
  url: string;
  alt: string;
  skuId: string | null;
}

// Một dòng của `GET /api/cart`. Dòng `removed` có mọi trường sản phẩm và giá bằng null.
export interface CartLine {
  skuId: string;
  quantity: number;
  productId: string | null;
  productName: string | null;
  productSlug: string | null;
  optionValues: Record<string, string> | null;
  image: CartLineImage | null;
  unitPrice: number | null;
  compareAtPrice: number | null;
  lineTotal: number | null;
  stock: number | null;
  purchasable: boolean;
  unavailableReason: UnavailableReason | null;
}

// Giỏ hàng. Mọi route ghi của backend cũng trả lại cả giỏ theo dạng này.
export interface Cart {
  items: CartLine[];
  subtotal: number;
  hasUnavailableItems: boolean;
  maxQuantityPerSku: number;
  maxCartLines: number;
}

export function fetchCart(): Promise<Cart> {
  return apiRequest<Cart>('/api/cart');
}

// Không gửi `source`: nút trên trang sản phẩm không phải nguồn cần đo.
export function addCartItem(skuId: string, quantity: number): Promise<Cart> {
  return apiRequest<Cart>('/api/cart/items', {
    method: 'POST',
    body: JSON.stringify({ skuId, quantity }),
  });
}

export function setCartItemQuantity(skuId: string, quantity: number): Promise<Cart> {
  return apiRequest<Cart>(`/api/cart/items/${encodeURIComponent(skuId)}`, {
    method: 'PATCH',
    body: JSON.stringify({ quantity }),
  });
}

export function removeCartItem(skuId: string): Promise<Cart> {
  return apiRequest<Cart>(`/api/cart/items/${encodeURIComponent(skuId)}`, { method: 'DELETE' });
}
