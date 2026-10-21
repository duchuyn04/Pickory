import { useEffect, useRef } from 'react';
import { trackProductView } from './track-product-view';

// Gửi một lượt xem khi sản phẩm đã hiện thật. Mỗi lần dựng trang sản phẩm gửi tối đa một lượt.
export function useTrackProductView(isProductShown: boolean) {
  const hasTracked = useRef(false);

  useEffect(() => {
    if (!isProductShown) return;
    // Cờ trong `useRef` chặn lần chạy effect thứ hai của StrictMode. Lần đó xảy ra khi sản phẩm đã có sẵn trong cache lúc mount.
    if (hasTracked.current) return;
    hasTracked.current = true;
    void trackProductView();
  }, [isProductShown]);
}
