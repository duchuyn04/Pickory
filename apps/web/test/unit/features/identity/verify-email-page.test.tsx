// @vitest-environment jsdom
import React, { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { VerifyEmailPage } from '../../../../src/features/identity/verify-email-page';

let container: HTMLDivElement;
let root: Root;
let currentUrl: string;

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
  currentUrl = '';
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

async function renderPage(url: string) {
  await act(async () => {
    root.render(
      <StrictMode>
        <MemoryRouter initialEntries={[url]}>
          <LocationTracker />
          <VerifyEmailPage />
        </MemoryRouter>
      </StrictMode>,
    );
  });
}

describe('verify trong StrictMode', () => {
  it.each([200, 400])(
    'kết thúc trạng thái chờ sau response %i, không tiêu thụ token hai lần',
    async (status) => {
      let resolveResponse!: (response: Response) => void;
      const response = new Promise<Response>((resolve) => {
        resolveResponse = resolve;
      });
      const fetchMock = vi.fn(() => response);
      vi.stubGlobal('fetch', fetchMock);
      await renderPage('/verify-email?token=one-time-token');

      expect(currentUrl).toBe('/verify-email');
      expect(fetchMock).toHaveBeenCalledTimes(1);
      expect(container.querySelector('[role="status"]')).not.toBeNull();
      expect(container.querySelector('a[href="/"]')).toBeNull();

      await act(async () => {
        resolveResponse(
          new Response(
            JSON.stringify(
              status === 200
                ? { data: { success: true } }
                : {
                    error: {
                      code: 'INVALID_TOKEN',
                      message: 'Liên kết không hợp lệ hoặc đã hết hạn.',
                    },
                  },
            ),
            { status },
          ),
        );
      });

      expect(fetchMock).toHaveBeenCalledTimes(1);
      expect(container.querySelector('a[href="/"]')).not.toBeNull();
      if (status === 200) {
        expect(container.querySelector('[role="status"]')).not.toBeNull();
        expect(container.querySelector('[role="alert"]')).toBeNull();
      } else {
        expect(container.querySelector('[role="status"]')).toBeNull();
        expect(container.querySelector('[role="alert"]')).not.toBeNull();
      }
    },
  );

  it('link thiếu token hiện lỗi ngay, không gọi xác thực', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    await renderPage('/verify-email');
    expect(fetchMock).not.toHaveBeenCalled();
    expect(container.querySelector('[role="alert"]')).not.toBeNull();
    expect(container.querySelector('[role="status"]')).toBeNull();
  });
});
