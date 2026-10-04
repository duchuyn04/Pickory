// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React, { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../../../../src/app';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';

let container: HTMLDivElement;
let root: Root;
let client: QueryClient;
let currentUrl: string;

const guest: ActorData = {
  anonymousId: 'test-anonymous',
  userId: null,
  kind: 'guest',
  roles: [],
  permissions: [],
  consent: { personalization: false, marketing: false },
};

function LocationTracker() {
  const location = useLocation();
  currentUrl = location.pathname + location.search;
  return null;
}

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  currentUrl = '';
  localStorage.clear();
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  client.clear();
  vi.unstubAllGlobals();
});

async function renderPage(url: string) {
  await act(async () => {
    root.render(
      <StrictMode>
        <QueryClientProvider client={client}>
          <MemoryRouter initialEntries={[url]}>
            <LocationTracker />
            <App />
          </MemoryRouter>
        </QueryClientProvider>
      </StrictMode>,
    );
  });
}

function setInput(id: string, value: string) {
  const input = container.querySelector(id);
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
  if (!(input instanceof HTMLInputElement) || !setter)
    throw new Error('Thiếu input trong form reset.');
  setter.call(input, value);
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

describe('reset mật khẩu và phiên cũ', () => {
  it('link thiếu token hiện alert và không hiện form hoặc gửi POST reset', async () => {
    const resetRequests: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        if (init?.method === 'POST') resetRequests.push(String(input));
        return new Response(JSON.stringify({ data: guest }));
      }),
    );
    await renderPage('/reset-password');
    expect(container.querySelector('[role="alert"]')).not.toBeNull();
    expect(container.querySelector('form')).toBeNull();
    expect(resetRequests).toEqual([]);
  });

  it.each<{ kind: 'customer' | 'staff'; loginPath: string }>([
    { kind: 'customer', loginPath: '/login' },
    { kind: 'staff', loginPath: '/admin/login' },
  ])('reset của $kind bỏ actor cũ và giữ người dùng ở $loginPath', async ({ kind, loginPath }) => {
    client.setQueryData<ActorData>(['actor'], { ...guest, userId: 'old-user', kind });
    let resolveActor!: (response: Response) => void;
    const actorResponse = new Promise<Response>((resolve) => {
      resolveActor = resolve;
    });
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        if (String(input) === '/api/me') return actorResponse;
        if (String(input) === '/api/auth/reset-password') {
          const body = typeof init?.body === 'string' ? JSON.parse(init.body) : null;
          return body?.token === 'retained-token'
            ? new Response(JSON.stringify({ data: { success: true, userKind: kind } }))
            : new Response(
                JSON.stringify({
                  error: { code: 'INVALID_TOKEN', message: 'Token không hợp lệ.' },
                }),
                { status: 400 },
              );
        }
        throw new Error('Request ngoài luồng reset: ' + String(input));
      }),
    );
    await renderPage('/reset-password?token=retained-token');
    expect(currentUrl).toBe('/reset-password');

    await act(async () => {
      setInput('#reset-password', 'NewPassword@123');
      setInput('#reset-confirm', 'NewPassword@123');
    });
    await act(async () => {
      const form = container.querySelector('form');
      if (!form) throw new Error('Thiếu form reset.');
      form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    });
    expect(client.getQueryData(['actor'])).toBeUndefined();
    const findLogin = () =>
      Array.from(container.querySelectorAll<HTMLAnchorElement>(`a[href="${loginPath}"]`)).find(
        (link) => !link.closest('header'),
      );
    expect(findLogin()).toBeUndefined();

    await act(async () => {
      resolveActor(new Response(JSON.stringify({ data: guest })));
    });
    expect(client.getQueryData<ActorData>(['actor'])?.kind).toBe('guest');
    const login = findLogin();
    if (!(login instanceof HTMLAnchorElement)) throw new Error('Thiếu link đăng nhập sau reset.');
    await act(async () => login.click());
    expect(currentUrl).toBe(loginPath);
    expect(container.querySelector('input[type="email"]')).not.toBeNull();
  });
});
