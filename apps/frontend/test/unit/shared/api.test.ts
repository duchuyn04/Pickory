import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, apiRequest } from '../../../src/shared/api';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('apiRequest', () => {
  it('phản hồi 204 không có thân là thành công và trả undefined', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(null, { status: 204 })),
    );
    await expect(
      apiRequest('/api/admin/products/p1', { method: 'DELETE' }),
    ).resolves.toBeUndefined();
  });

  it('phản hồi 200 không có thân vẫn là lỗi vì thiếu `data`', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('', { status: 200 })),
    );
    await expect(apiRequest('/api/x')).rejects.toMatchObject({ code: 'UNKNOWN_ERROR' });
  });

  it('trả phần `data` của phản hồi thành công', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify({ data: { id: 'p1' } }), { status: 200 })),
    );
    await expect(apiRequest('/api/x')).resolves.toEqual({ id: 'p1' });
  });

  it('chuyển phản hồi lỗi thành ApiError kèm details', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response(
            JSON.stringify({
              error: { code: 'PRODUCT_NOT_PUBLISHABLE', message: 'Lỗi.', details: ['a'] },
            }),
            { status: 422 },
          ),
      ),
    );
    const error = await apiRequest('/api/x').catch((caught: unknown) => caught);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 422, details: ['a'] });
  });
});
