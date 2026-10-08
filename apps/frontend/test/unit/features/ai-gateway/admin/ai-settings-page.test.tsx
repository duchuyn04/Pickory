// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AiSettingsPage } from '../../../../../src/features/ai-gateway/admin/ai-settings-page';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';

let container: HTMLDivElement;
let root: Root;

const readerActor: ActorData = {
  anonymousId: 'anon',
  userId: 'user-1',
  kind: 'staff',
  roles: ['admin'],
  permissions: ['ai-usage:read'],
  consent: { personalization: false, marketing: false },
};

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status });
}

function stubServer(options: { budgetWarning: boolean; settingsStatus?: number }) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string) => {
      const url = new URL(input, 'http://localhost');
      if (url.pathname === '/api/admin/ai/usage') {
        return jsonResponse({
          data: {
            costUsd: 1,
            calls: 2,
            errorRate: 0,
            p95LatencyMs: 10,
            byDay: [],
            byFeature: [],
            budgetWarning: options.budgetWarning,
          },
        });
      }
      if (url.pathname === '/api/admin/ai/settings') {
        if (options.settingsStatus === 500) {
          return jsonResponse({ error: { code: 'X', message: 'Máy chủ hỏng.' } }, 500);
        }
        return jsonResponse({
          data: { monthlyBudgetUsd: 100, chatDailyLimitUser: 10, chatDailyLimitAnon: 5 },
        });
      }
      return jsonResponse({ error: { code: 'NOT_FOUND', message: 'Không có route giả.' } }, 404);
    }),
  );
}

async function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  queryClient.setQueryData(['actor'], readerActor);
  await act(async () =>
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AiSettingsPage />
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  );
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

describe('AiSettingsPage: thông báo trên đầu trang', () => {
  it('cảnh báo ngân sách là cảnh báo vàng với thẻ strong thật, không có icon lỗi', async () => {
    stubServer({ budgetWarning: true });
    await renderPage();

    const warning = container.querySelector<HTMLElement>('.alert-warn')!;
    expect(warning).not.toBeNull();
    expect(warning.getAttribute('role')).toBe('alert');
    expect(warning.querySelector('strong')?.textContent).toBe('Cảnh báo ngân sách tháng:');
    // Chữ của trang không được chứa thẻ HTML viết ra thành chữ.
    expect(warning.textContent).not.toContain('<strong>');
    expect(warning.textContent).toContain('Chi tiêu AI trong tháng đã chạm hoặc vượt 80%');
    expect(warning.classList.contains('alert-list')).toBe(false);
    expect(warning.querySelector('svg')).toBeNull();
  });

  it('không vượt ngân sách thì không hiện cảnh báo', async () => {
    stubServer({ budgetWarning: false });
    await renderPage();
    expect(container.querySelector('.alert-warn')).toBeNull();
  });

  it('lỗi tải cài đặt dùng ErrorAlert và giữ chữ ghép', async () => {
    stubServer({ budgetWarning: false, settingsStatus: 500 });
    await renderPage();
    const alert = container.querySelector<HTMLElement>('.ai-settings-note[role="alert"]')!;
    expect(alert.classList.contains('alert-list')).toBe(true);
    expect(alert.textContent).toBe('Lỗi tải cài đặt: Máy chủ hỏng.');
  });
});
