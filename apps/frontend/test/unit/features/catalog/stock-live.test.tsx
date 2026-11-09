// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
// Tách deferred promise để điều khiển thời điểm phản hồi của request đầu.
interface Deferred<T> {
  promise: Promise<T>;
  resolve: (value: T) => void;
  reject: (reason?: unknown) => void;
}

function createDeferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

// Tải lại module qua vi.resetModules để kịch bản có trạng thái request đầu độc lập.
async function loadFreshStockLiveContext() {
  vi.resetModules();
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  vi.stubGlobal('EventSource', FakeEventSource);
  const stockLiveModule = await import('../../../../src/features/catalog/shared/stock-live');
  const apiModule = await import('../../../../src/shared/api');
  return {
    StockLiveProvider: stockLiveModule.StockLiveProvider,
    apiRequest: apiModule.apiRequest,
  };
}

import type { ProductListItem } from '../../../../src/features/catalog/customer/catalog-api';
import { ProductCard } from '../../../../src/features/catalog/customer/product-card';
import {
  applyLiveProductStock,
  parseStockEvent,
  StockLiveProvider,
} from '../../../../src/features/catalog/shared/stock-live';
import { apiRequest } from '../../../../src/shared/api';
import { buildProductDetail } from './customer/product-fixtures';

class FakeEventSource {
  static instances: FakeEventSource[] = [];

  readonly readyState = 0;
  onopen: ((event: Event) => void) | null = null;
  onerror: ((event: Event) => void) | null = null;
  closed = false;
  private readonly listeners = new Map<string, ((event: MessageEvent) => void)[]>();

  constructor(
    readonly url: string,
    readonly options?: EventSourceInit,
  ) {
    FakeEventSource.instances.push(this);
  }

  addEventListener(name: string, listener: EventListener): void {
    const listeners = this.listeners.get(name) ?? [];
    listeners.push(listener as (event: MessageEvent) => void);
    this.listeners.set(name, listeners);
  }

  removeEventListener(name: string, listener: EventListener): void {
    const listeners = this.listeners.get(name) ?? [];
    this.listeners.set(
      name,
      listeners.filter((candidate) => candidate !== listener),
    );
  }

  close(): void {
    this.closed = true;
  }

  emit(name: string, data: unknown): void {
    for (const listener of this.listeners.get(name) ?? []) {
      listener({ data } as MessageEvent);
    }
  }

  static reset(): void {
    FakeEventSource.instances = [];
  }
}

const product: ProductListItem = {
  id: 'prod-1',
  slug: 'laptop-1',
  name: 'Laptop 1',
  brand: { id: 'brand-1', name: 'ASUS', slug: 'asus' },
  minPrice: 1_000_000,
  inStock: true,
  status: 'active',
  image: null,
};

let container: HTMLDivElement;
let root: Root;

beforeEach(async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  vi.stubGlobal('EventSource', FakeEventSource);
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify({ data: { ok: true } }), { status: 200 })),
  );
  // Giải phóng bước chờ request đầu để các test chức năng của luồng mở kết nối.
  await apiRequest('/api/init');
  FakeEventSource.reset();
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

describe('parseStockEvent', () => {
  it('nhận dữ liệu hợp lệ và bỏ qua dữ liệu sai', () => {
    expect(
      parseStockEvent(
        JSON.stringify({
          productId: 'prod-1',
          skuId: 'sku-1',
          stock: 3,
          productInStock: true,
          minPrice: 100000,
        }),
      ),
    ).toEqual({
      productId: 'prod-1',
      skuId: 'sku-1',
      stock: 3,
      productInStock: true,
      minPrice: 100000,
    });

    expect(parseStockEvent('không phải JSON')).toBeNull();
    expect(parseStockEvent(JSON.stringify({ skuId: 'sku-1' }))).toBeNull();
    expect(
      parseStockEvent(
        JSON.stringify({
          productId: 'prod-1',
          skuId: 'sku-1',
          stock: -1,
          productInStock: false,
          minPrice: null,
        }),
      ),
    ).toBeNull();
  });
});

describe('applyLiveProductStock', () => {
  it('ghi đè số của đúng SKU và giữ nguyên SKU không có sự kiện', () => {
    const detail = buildProductDetail();
    const updated = applyLiveProductStock(detail, {
      products: { [detail.id]: { inStock: true, minPrice: 1_500_000 } },
      skus: { [detail.skus[0]!.id]: { productId: detail.id, stock: 2 } },
    });

    expect(updated.skus[0]!.stock).toBe(2);
    expect(updated.skus[0]!.inStock).toBe(true);
    expect(updated.skus[1]).toBe(detail.skus[1]);
    expect(updated.minPrice).toBe(1_500_000);
  });

  it('không có sự kiện thì trả lại đúng đối tượng cũ', () => {
    const detail = buildProductDetail();
    expect(applyLiveProductStock(detail, { products: {}, skus: {} })).toBe(detail);
  });
});

describe('StockLiveProvider', () => {
  it('mở một kết nối và cập nhật thẻ sản phẩm khi có đơn mới', async () => {
    await act(async () =>
      root.render(
        <StockLiveProvider>
          <MemoryRouter>
            <ProductCard product={product} />
          </MemoryRouter>
        </StockLiveProvider>,
      ),
    );

    const source = FakeEventSource.instances[0]!;
    expect(source.url).toBe('/api/stock-events');
    expect(source.options?.withCredentials).toBe(true);
    expect(container.textContent).not.toContain('Hết hàng');

    await act(async () => {
      source.emit(
        'stock.changed',
        JSON.stringify({
          productId: product.id,
          skuId: 'sku-1',
          stock: 0,
          productInStock: false,
          minPrice: 1_000_000,
        }),
      );
    });
    expect(container.textContent).toContain('Hết hàng');

    await act(async () => {
      source.emit(
        'stock.changed',
        JSON.stringify({
          productId: product.id,
          skuId: 'sku-1',
          stock: 5,
          productInStock: true,
          minPrice: 900_000,
        }),
      );
    });
    expect(container.textContent).not.toContain('Hết hàng');
    expect(container.textContent).toContain('900.000 đ');
  });

  it('đóng kết nối khi provider bị gỡ', async () => {
    await act(async () =>
      root.render(
        <StockLiveProvider>
          <p>Nội dung</p>
        </StockLiveProvider>,
      ),
    );
    const source = FakeEventSource.instances[0]!;

    await act(async () => root.render(<p>Nội dung</p>));

    expect(source.closed).toBe(true);
  });

  it('chờ request API đầu tiên hoàn tất mới tạo kết nối EventSource', async () => {
    const { StockLiveProvider: FreshStockLiveProvider, apiRequest: freshApiRequest } =
      await loadFreshStockLiveContext();
    const firstDeferred = createDeferred<Response>();
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => firstDeferred.promise),
    );

    // Kích hoạt request đầu tiên của tab để đưa bước chờ vào trạng thái đang chờ.
    const firstApiCall = freshApiRequest('/api/init');

    await act(async () => {
      root.render(
        <FreshStockLiveProvider>
          <p>Nội dung</p>
        </FreshStockLiveProvider>,
      );
    });

    // Trong lúc request đầu đang chạy, EventSource chưa được khởi tạo.
    expect(FakeEventSource.instances).toHaveLength(0);

    // Giải phóng request đầu để kích hoạt mở kết nối EventSource.
    await act(async () => {
      firstDeferred.resolve(new Response(JSON.stringify({ data: { ok: true } }), { status: 200 }));
      await firstApiCall;
    });

    // Khi request đầu hoàn tất, kết nối EventSource được tạo.
    expect(FakeEventSource.instances).toHaveLength(1);
    expect(FakeEventSource.instances[0]!.url).toBe('/api/stock-events');
  });

  it('gỡ component trong lúc chờ request đầu thì không mở kết nối EventSource sau đó', async () => {
    const { StockLiveProvider: FreshStockLiveProvider, apiRequest: freshApiRequest } =
      await loadFreshStockLiveContext();
    const firstDeferred = createDeferred<Response>();
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => firstDeferred.promise),
    );

    const firstApiCall = freshApiRequest('/api/init');

    await act(async () => {
      root.render(
        <FreshStockLiveProvider>
          <p>Nội dung</p>
        </FreshStockLiveProvider>,
      );
    });

    expect(FakeEventSource.instances).toHaveLength(0);

    // Gỡ provider trong lúc request đầu vẫn đang chạy.
    await act(async () => {
      root.render(<p>Đã gỡ</p>);
    });

    // Giải phóng request đầu sau khi đã gỡ component.
    await act(async () => {
      firstDeferred.resolve(new Response(JSON.stringify({ data: { ok: true } }), { status: 200 }));
      await firstApiCall;
    });

    // Kết nối không được tạo sau khi provider đã bị gỡ.
    expect(FakeEventSource.instances).toHaveLength(0);
  });

  it('provider gắn trước khi request API bắt đầu thì chờ request đó hoàn tất mới mở kết nối', async () => {
    const { StockLiveProvider: FreshStockLiveProvider, apiRequest: freshApiRequest } =
      await loadFreshStockLiveContext();
    const firstDeferred = createDeferred<Response>();
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => firstDeferred.promise),
    );

    // Gắn provider trước khi có bất kỳ request API nào được kích hoạt.
    await act(async () => {
      root.render(
        <FreshStockLiveProvider>
          <p>Nội dung</p>
        </FreshStockLiveProvider>,
      );
    });

    // Khi chưa có request API nào chạy, luồng vẫn ở trạng thái chờ.
    expect(FakeEventSource.instances).toHaveLength(0);

    // Bắt đầu request API đầu tiên với deferred promise chưa mở.
    const firstApiCall = freshApiRequest('/api/init');
    await act(async () => {
      await Promise.resolve();
    });

    // Trong lúc request đầu đang chạy, luồng vẫn chưa được tạo.
    expect(FakeEventSource.instances).toHaveLength(0);

    // Giải phóng request đầu tiên để mở cổng kết nối.
    await act(async () => {
      firstDeferred.resolve(new Response(JSON.stringify({ data: { ok: true } }), { status: 200 }));
      await firstApiCall;
    });

    // Khi request đầu hoàn tất, kết nối EventSource được khởi tạo.
    expect(FakeEventSource.instances).toHaveLength(1);
    expect(FakeEventSource.instances[0]!.url).toBe('/api/stock-events');
  });
});
