import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, type ReactNode } from 'react';
import type { Root } from 'react-dom/client';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { vi } from 'vitest';
import type { Cart } from '../../../../../src/features/commerce/customer/cart-api';
import { buildCartLine } from './cart-fixtures';

export interface RecordedCall {
  method: string;
  path: string;
  body: unknown;
}

export interface FakeApiReply {
  status: number;
  body: unknown;
}

// Cho phép test thay phản hồi của từng lệnh gọi, và có thể trả `Promise` để giữ phản hồi lại.
// Trả `null` để dùng phản hồi mặc định theo trạng thái giỏ.
export type FakeApiOverride = (call: RecordedCall) => FakeApiReply | Promise<FakeApiReply> | null;

// API giả của giỏ: giữ một giỏ trong bộ nhớ và ghi lại mọi lệnh gọi. Mọi route ghi trả lại cả giỏ như backend.
export function stubCartApi(initialCart: Cart, override: FakeApiOverride = () => null) {
  let cart = initialCart;
  const calls: RecordedCall[] = [];

  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = String(input);
    const method = (init?.method ?? 'GET').toUpperCase();
    const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;
    const call = { method, path, body };
    calls.push(call);

    const overridden = await override(call);
    if (overridden) return Response.json(overridden.body, { status: overridden.status });

    if (path === '/api/me') {
      return Response.json({
        data: {
          anonymousId: 'anon-1',
          userId: null,
          kind: 'guest',
          roles: [],
          permissions: [],
          consent: { personalization: false, marketing: false },
        },
      });
    }

    if (path === '/api/cart' && method === 'GET') {
      return Response.json({ data: cart });
    }

    if (path === '/api/cart/items' && method === 'POST') {
      const input = body as { skuId: string; quantity: number };
      const existing = cart.items.find((line) => line.skuId === input.skuId);
      cart = withItems(
        cart,
        existing
          ? cart.items.map((line) =>
              line === existing ? { ...line, quantity: line.quantity + input.quantity } : line,
            )
          : [...cart.items, buildCartLine({ skuId: input.skuId, quantity: input.quantity })],
      );
      return Response.json({ data: cart });
    }

    if (path.startsWith('/api/cart/items') && method === 'DELETE') {
      const skuId = decodeURIComponent(path.split('/').pop() ?? '');
      cart = withItems(
        cart,
        cart.items.filter((line) => line.skuId !== skuId),
      );
      return Response.json({ data: cart });
    }

    if (path.startsWith('/api/cart/items/') && method === 'PATCH') {
      const skuId = decodeURIComponent(path.split('/').pop() ?? '');
      const quantity = (body as { quantity: number }).quantity;
      cart = withItems(
        cart,
        cart.items.map((line) =>
          line.skuId === skuId
            ? { ...line, quantity, lineTotal: (line.unitPrice ?? 0) * quantity }
            : line,
        ),
      );
      return Response.json({ data: cart });
    }

    return Response.json(
      { error: { code: 'NOT_FOUND', message: 'Không có route giả.' } },
      { status: 404 },
    );
  });

  vi.stubGlobal('fetch', fetchMock);
  return {
    calls,
    fetchMock,
    callsTo: (method: string, pathPrefix: string) =>
      calls.filter((call) => call.method === method && call.path.startsWith(pathPrefix)),
    setCart(next: Cart) {
      cart = next;
    },
  };
}

function withItems(cart: Cart, items: Cart['items']): Cart {
  const subtotal = items
    .filter((line) => line.purchasable)
    .reduce((total, line) => total + (line.lineTotal ?? 0), 0);
  return { ...cart, items, subtotal, hasUnavailableItems: items.some((line) => !line.purchasable) };
}

export function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname + location.search}</output>;
}

// `retry: false` để lỗi hiện ngay, không chờ thử lại trong test.
export function newTestQueryClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false } } });
}

export async function renderWithProviders(
  root: Root,
  ui: ReactNode,
  url = '/',
  queryClient = newTestQueryClient(),
) {
  await act(async () =>
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[url]}>
          {ui}
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  );
  await settle();
  return queryClient;
}

// Nhường vài vòng sự kiện để fetch và React Query cập nhật xong.
export async function settle() {
  for (let turn = 0; turn < 5; turn += 1) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
  }
}
