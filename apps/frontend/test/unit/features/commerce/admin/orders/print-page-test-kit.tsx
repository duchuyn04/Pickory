import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { vi } from 'vitest';
import { AdminOrderPrintPage } from '../../../../../../src/features/commerce/admin/orders/admin-order-print-page';
import type { ActorData } from '../../../../../../src/features/identity/hooks/use-actor';

export interface PrintHarness {
  container: HTMLDivElement;
  client: QueryClient;
  requestedUrls: string[];
  printSpy: ReturnType<typeof vi.spyOn>;
  closeSpy: ReturnType<typeof vi.spyOn>;
  render: (url: string) => Promise<void>;
  cleanup: () => Promise<void>;
}

export function createGuestActor(): ActorData {
  return {
    anonymousId: 'anonymous-guest',
    userId: null,
    kind: 'guest',
    roles: [],
    permissions: [],
    consent: { personalization: false, marketing: false },
  };
}

export function createCustomerActor(): ActorData {
  return {
    anonymousId: 'anonymous-customer',
    userId: 'user-customer',
    email: 'khach@pickory.local',
    name: 'Khách Hàng',
    kind: 'customer',
    roles: ['customer'],
    permissions: [],
    consent: { personalization: false, marketing: false },
  };
}

export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

// Dựng cây React kèm StrictMode để bắt lỗi hủy effect khi dữ liệu đã sẵn có lúc mount.
export function createPrintHarness(
  respondMe: () => Promise<Response> | Response,
  respondApi: (url: string) => Promise<Response> | Response,
): PrintHarness {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  const container = document.createElement('div');
  document.body.append(container);
  const root: Root = createRoot(container);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const requestedUrls: string[] = [];
  const printSpy = vi.spyOn(window, 'print').mockImplementation(() => {});
  const closeSpy = vi.spyOn(window, 'close').mockImplementation(() => {});

  const originalDecodeDescriptor = Object.getOwnPropertyDescriptor(
    HTMLImageElement.prototype,
    'decode',
  );
  // Định nghĩa sẵn phương thức giải mã ảnh vì jsdom chưa cài sẵn hàm này.
  Object.defineProperty(HTMLImageElement.prototype, 'decode', {
    configurable: true,
    writable: true,
    value: vi.fn(() => Promise.resolve()),
  });

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url === '/api/me') {
        return respondMe();
      }
      requestedUrls.push(url);
      return respondApi(url);
    }),
  );

  return {
    container,
    client,
    requestedUrls,
    printSpy,
    closeSpy,
    render: (url) =>
      act(async () => {
        root.render(
          <StrictMode>
            <QueryClientProvider client={client}>
              <MemoryRouter initialEntries={[url]}>
                <Routes>
                  <Route path="/admin/orders/print" element={<AdminOrderPrintPage />} />
                  <Route path="/admin/login" element={<div data-testid="admin-login-redirect" />} />
                </Routes>
              </MemoryRouter>
            </QueryClientProvider>
          </StrictMode>,
        );
      }),
    cleanup: async () => {
      await act(async () => root.unmount());
      container.remove();
      client.clear();
      if (originalDecodeDescriptor) {
        Object.defineProperty(HTMLImageElement.prototype, 'decode', originalDecodeDescriptor);
      } else {
        delete (HTMLImageElement.prototype as Partial<HTMLImageElement>).decode;
      }
      vi.unstubAllGlobals();
    },
  };
}
