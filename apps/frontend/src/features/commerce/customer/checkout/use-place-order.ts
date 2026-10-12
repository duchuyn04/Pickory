import { useMutation } from '@tanstack/react-query';
import type { ApiError } from '../../../../shared/api';
import { placeOrder, type PlaceOrderPayload, type PlacedOrder } from './checkout-api';

interface PlaceOrderVariables {
  payload: PlaceOrderPayload;
  idempotencyKey: string;
}

// Khóa nằm trong biến của mutation để trang quyết định khóa. Hook không tự sinh khóa mới.
export function usePlaceOrder() {
  return useMutation<PlacedOrder, ApiError, PlaceOrderVariables>({
    mutationFn: ({ payload, idempotencyKey }) => placeOrder(payload, idempotencyKey),
  });
}
