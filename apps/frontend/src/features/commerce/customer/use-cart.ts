import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import type { ApiError } from '../../../shared/api';
import { useActor } from '../../identity/hooks/use-actor';
import { addCartItem, fetchCart, removeCartItem, setCartItemQuantity, type Cart } from './cart-api';

// Một khóa duy nhất cho giỏ. Mini cart, trang giỏ và nút thêm giỏ cùng đọc và cùng ghi cache này.
export const CART_QUERY_KEY = ['cart'] as const;

export function useCart() {
  return useQuery<Cart, ApiError>({
    queryKey: CART_QUERY_KEY,
    queryFn: fetchCart,
  });
}

// Backend trả cả giỏ sau mỗi lần ghi. Đặt thẳng vào cache nên mọi nơi đổi ngay, không cần gọi lại GET.
function useCartMutation<Variables>(mutationFn: (variables: Variables) => Promise<Cart>) {
  const queryClient = useQueryClient();
  return useMutation<Cart, ApiError, Variables>({
    mutationFn,
    onSuccess: (cart) => {
      queryClient.setQueryData(CART_QUERY_KEY, cart);
    },
  });
}
// Đầu vào thêm giỏ có thể là SKU hoặc kèm số lượng mong muốn.
export type AddCartItemInput = string | { skuId: string; quantity: number };

export function useAddCartItem() {
  return useCartMutation((input: AddCartItemInput) => {
    // Chuỗi thuần tương thích với các màn chỉ cần thêm 1 sản phẩm.
    if (typeof input === 'string') {
      return addCartItem(input, 1);
    }
    return addCartItem(input.skuId, input.quantity);
  });
}

export function useSetCartQuantity() {
  return useCartMutation((input: { skuId: string; quantity: number }) =>
    setCartItemQuantity(input.skuId, input.quantity),
  );
}

export function useRemoveCartItem() {
  return useCartMutation((skuId: string) => removeCartItem(skuId));
}

// Đăng nhập, đăng xuất hoặc đổi tài khoản làm giỏ đổi chủ (backend gộp giỏ vãng lai vào giỏ tài khoản).
// Hook nạp lại giỏ khi người dùng đổi, để mini cart không giữ giỏ của phiên trước.
export function useRefreshCartOnActorChange() {
  const queryClient = useQueryClient();
  const { data: actor } = useActor();
  const ownerKey = actor ? (actor.userId ?? 'guest') : null;
  const previousOwnerKey = useRef<string | null>(null);

  useEffect(() => {
    // Chưa biết actor thì chưa so sánh. Lần đầu có actor chỉ ghi nhớ, vì giỏ vừa được nạp theo actor đó.
    if (ownerKey === null) return;
    if (previousOwnerKey.current !== null && previousOwnerKey.current !== ownerKey) {
      void queryClient.invalidateQueries({ queryKey: CART_QUERY_KEY });
    }
    previousOwnerKey.current = ownerKey;
  }, [ownerKey, queryClient]);
}
