import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { trackProductView } from '../../../../src/features/activity-tracking/track-product-view';

let consoleError: ReturnType<typeof vi.spyOn>;
let consoleWarn: ReturnType<typeof vi.spyOn>;

beforeEach(() => {
  consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
  consoleWarn = vi.spyOn(console, 'warn').mockImplementation(() => {});
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('trackProductView', () => {
  it('gửi đúng một lần POST /api/events với keepalive, cookie và body product_view', async () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    await trackProductView();

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [path, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(path).toBe('/api/events');
    expect(init.method).toBe('POST');
    expect(init.keepalive).toBe(true);
    expect(init.credentials).toBe('include');
    expect(init.body).toBe('{"type":"product_view"}');
    expect(consoleError).not.toHaveBeenCalled();
    expect(consoleWarn).not.toHaveBeenCalled();
  });

  it('mất mạng thì hàm vẫn resolve và không ghi console', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        throw new TypeError('Failed to fetch');
      }),
    );

    await expect(trackProductView()).resolves.toBeUndefined();

    expect(consoleError).not.toHaveBeenCalled();
    expect(consoleWarn).not.toHaveBeenCalled();
  });

  it.each([
    [429, 'TOO_MANY_REQUESTS'],
    [500, 'INTERNAL'],
  ])('API trả %i có body lỗi thì hàm vẫn resolve và không ghi console', async (status, code) => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => Response.json({ error: { code, message: 'Lỗi.' } }, { status })),
    );

    await expect(trackProductView()).resolves.toBeUndefined();

    expect(consoleError).not.toHaveBeenCalled();
    expect(consoleWarn).not.toHaveBeenCalled();
  });
});
