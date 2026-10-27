import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { z } from 'zod';
import type { ProductDetail } from '../customer/catalog-api';

// Luồng sự kiện tồn kho công khai. Trang khách và trang quản lý sản phẩm cùng nghe luồng này.
export const STOCK_EVENTS_URL = '/api/stock-events';

// Tên sự kiện trong trường `event` của SSE. Nhịp tim không đổi dữ liệu.
const STOCK_CHANGED_EVENT = 'stock.changed';

// Dữ liệu SSE do API của mình phát. Kiểm một lần ở ranh giới để phần còn lại dùng kiểu đã chắc chắn.
const stockEventSchema = z.object({
  productId: z.string().min(1),
  skuId: z.string().min(1),
  stock: z.number().int().nonnegative(),
  productInStock: z.boolean(),
  minPrice: z.number().int().nullable(),
});

export type StockEvent = z.infer<typeof stockEventSchema>;

export interface LiveProductStock {
  inStock: boolean;
  minPrice: number | null;
}

export interface LiveSkuStock {
  productId: string;
  stock: number;
}

export interface LiveStockState {
  products: Record<string, LiveProductStock>;
  skus: Record<string, LiveSkuStock>;
}

// Trạng thái rỗng cho test và cho lần vẽ đầu tiên trước khi có sự kiện.
const EMPTY_STATE: LiveStockState = { products: {}, skus: {} };

const LiveStockContext = createContext<LiveStockState>(EMPTY_STATE);

// Dữ liệu SSE là chuỗi JSON. Dữ liệu hỏng trả null để bỏ qua sự kiện.
export function parseStockEvent(raw: unknown): StockEvent | null {
  if (typeof raw !== 'string') return null;
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return null;
  }

  const result = stockEventSchema.safeParse(parsed);
  return result.success ? result.data : null;
}

// Ghi đè trạng thái của đúng sản phẩm và đúng SKU trong sự kiện.
export function applyStockEvent(state: LiveStockState, event: StockEvent): LiveStockState {
  return {
    products: {
      ...state.products,
      [event.productId]: { inStock: event.productInStock, minPrice: event.minPrice },
    },
    skus: {
      ...state.skus,
      [event.skuId]: { productId: event.productId, stock: event.stock },
    },
  };
}

// Giữ một kết nối SSE cho cả tab. `EventSource` tự nối lại khi mạng chập chờn.
export function StockLiveProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<LiveStockState>(EMPTY_STATE);

  useEffect(() => {
    // Trình duyệt quá cũ không có `EventSource` thì không có luồng nào để nối.
    if (typeof EventSource === 'undefined') return;

    const source = new EventSource(STOCK_EVENTS_URL, { withCredentials: true });
    const handleStockChange = (event: MessageEvent) => {
      const stockEvent = parseStockEvent(event.data);
      if (stockEvent === null) return;
      setState((current) => applyStockEvent(current, stockEvent));
    };

    source.addEventListener(STOCK_CHANGED_EVENT, handleStockChange as EventListener);
    return () => {
      // Gỡ bộ nhận trước khi đóng, để sự kiện đang bay không cập nhật component đã gỡ.
      source.removeEventListener(STOCK_CHANGED_EVENT, handleStockChange as EventListener);
      source.close();
    };
  }, []);

  return <LiveStockContext.Provider value={state}>{children}</LiveStockContext.Provider>;
}

// Áp số lượng mới vào chi tiết sản phẩm. Không có sự kiện nào cho sản phẩm này thì giữ nguyên đối tượng cũ.
export function applyLiveProductStock(
  product: ProductDetail,
  state: LiveStockState,
): ProductDetail {
  const liveProduct = state.products[product.id];
  let changed = false;
  const skus = product.skus.map((sku) => {
    const liveSku = state.skus[sku.id];
    if (!liveSku) return sku;
    changed = true;
    return { ...sku, stock: liveSku.stock, inStock: liveSku.stock > 0 };
  });
  if (!liveProduct && !changed) return product;

  return {
    ...product,
    inStock: liveProduct?.inStock ?? product.inStock,
    minPrice: liveProduct?.minPrice ?? product.minPrice,
    skus,
  };
}

// Đọc bản mới nhất. Component không nằm trong provider nhận trạng thái rỗng và dùng dữ liệu API.
export function useLiveStock(): LiveStockState {
  return useContext(LiveStockContext);
}
