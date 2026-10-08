import { useEffect, useState } from 'react';
import { ApiError, apiRequest } from '../../../shared/api';
import type { ProductListResponse } from './catalog-api';

export type ProductListState =
  // `data` giữ kết quả trước trong lúc tải kết quả mới, để lưới không nháy trắng khi đổi bộ lọc.
  | { status: 'loading'; data: ProductListResponse | null }
  | { status: 'success'; data: ProductListResponse }
  | { status: 'error'; data: null; error: ApiError };

function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) return error;
  return new ApiError('UNKNOWN_ERROR', 0, 'Không tải được danh sách sản phẩm.');
}

// Tải danh sách sản phẩm theo đường dẫn API. Đường dẫn đổi thì hủy request cũ và bỏ response của nó.
export function useProductList(apiPath: string): ProductListState {
  const [state, setState] = useState<ProductListState>({ status: 'loading', data: null });

  useEffect(() => {
    const controller = new AbortController();
    setState((previous) => ({ status: 'loading', data: previous.data }));

    apiRequest<ProductListResponse>(apiPath, { signal: controller.signal })
      .then((data) => {
        // Request đã bị thay thế thì bỏ response, để kết quả cũ về muộn không ghi đè kết quả mới.
        if (controller.signal.aborted) return;
        setState({ status: 'success', data });
      })
      .catch((error: unknown) => {
        // Request bị hủy báo lỗi mạng, nhưng đó là chủ ý của ta nên không hiện cho khách.
        if (controller.signal.aborted) return;
        setState({ status: 'error', data: null, error: toApiError(error) });
      });

    // Hủy request khi đường dẫn đổi hoặc trang đóng, để mạng không giữ request vô ích.
    return () => controller.abort();
  }, [apiPath]);

  return state;
}
