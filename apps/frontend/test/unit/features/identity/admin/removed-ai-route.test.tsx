// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React, { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../../../../src/app';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';

let container: HTMLDivElement;
let root: Root;
let requestedUrls: string[];

const adminActor: ActorData = {
  anonymousId: 'anonymous-admin',
  userId: 'user-admin',
  email: 'admin@pickory.local',
  name: 'Nguyễn Quản Trị',
  kind: 'staff',
  roles: ['admin'],
  permissions: [],
  consent: { personalization: false, marketing: false },
};

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  requestedUrls = [];
  window.localStorage.clear();
  // Route không khớp làm React Router ghi cảnh báo. Test chặn cảnh báo này để log không bị nhiễu.
  vi.spyOn(console, 'warn').mockImplementation(() => undefined);
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      requestedUrls.push(url);
      if (url === '/api/me') return new Response(JSON.stringify({ data: adminActor }));
      return new Response(JSON.stringify({ data: [] }));
    }),
  );
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

async function renderAdminApp(path: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  await act(async () => {
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[path]}>
          <App />
        </MemoryRouter>
      </QueryClientProvider>,
    );
  });
  // Chờ các request khởi tạo (như `/api/me`) chạy xong trước khi kiểm.
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 100));
  });
}

describe('màn cấu hình AI đã gỡ', () => {
  it('mở /admin/ai không hiện màn cấu hình AI và không gọi API AI', async () => {
    await renderAdminApp('/admin/ai');

    // Ứng dụng đã khởi động và hỏi phiên admin. Thiếu request này thì trang trắng cũng làm test xanh.
    expect(requestedUrls).toContain('/api/me');
    expect(container.textContent).not.toContain('Cấu hình và sử dụng AI');
    expect(requestedUrls.filter((url) => url.startsWith('/api/admin/ai'))).toEqual([]);
  });

  it('cùng phiên admin, /admin vẫn dựng khung back-office và menu không có mục AI', async () => {
    await renderAdminApp('/admin');

    // Đối chứng cho ca trên: harness này dựng được khung admin khi route có thật.
    const navigationText = container.querySelector('nav[aria-label="Quản trị"]')?.textContent ?? '';
    expect(navigationText).toContain('Nhân viên');
    expect(navigationText).not.toContain('Cấu hình AI');
  });
});
