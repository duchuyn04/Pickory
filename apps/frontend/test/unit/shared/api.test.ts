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

  it('lỗi 400 kèm details chỉ hiện câu của từng trường, không hiện tên trường', async () => {
    const details = [
      { key: 'items[0].quantity', message: 'Số lượng phải lớn hơn 0.' },
      { key: 'phone', message: 'Số điện thoại không hợp lệ.' },
      { key: 'phoneConfirm', message: 'Số điện thoại không hợp lệ.' },
    ];
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response(
            JSON.stringify({
              error: {
                code: 'VALIDATION_ERROR',
                message:
                  'Dữ liệu không hợp lệ (items[0].quantity: Số lượng phải lớn hơn 0.; phone: Số điện thoại không hợp lệ.).',
                details,
              },
            }),
            { status: 400 },
          ),
      ),
    );
    const error = await apiRequest('/api/x').catch((caught: unknown) => caught);
    // Câu trùng chỉ hiện một lần. `details` giữ nguyên để form gắn lỗi vào đúng ô.
    expect(error).toMatchObject({
      message: 'Số lượng phải lớn hơn 0. Số điện thoại không hợp lệ.',
      details,
    });
  });

  it('lỗi 400 không có details thì bỏ tên trường khỏi câu tổng', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response(
            JSON.stringify({
              error: {
                code: 'VALIDATION_ERROR',
                message: 'Dữ liệu không hợp lệ (body.note: Ghi chú quá dài.; email: Email sai.).',
              },
            }),
            { status: 400 },
          ),
      ),
    );
    await expect(apiRequest('/api/x')).rejects.toMatchObject({
      message: 'Ghi chú quá dài. Email sai.',
    });
  });

  it.each([
    [401, 'Phiên đăng nhập đã hết hạn. Hãy đăng nhập lại.'],
    [403, 'Tài khoản không có quyền thực hiện thao tác này.'],
    [429, 'Thao tác quá nhanh. Hãy chờ một lát rồi thử lại.'],
    [502, 'Máy chủ đang gặp sự cố. Hãy thử lại sau.'],
  ])(
    'phản hồi %i không theo mẫu chung thì hiện câu dễ hiểu, không nhắc mã trạng thái',
    async (status, message) => {
      // Proxy trả trang HTML khi backend lỗi. Trang này không có `{ error }` để đọc câu lỗi.
      vi.stubGlobal(
        'fetch',
        vi.fn(async () => new Response('<html>Bad Gateway</html>', { status })),
      );
      const error = await apiRequest('/api/x').catch((caught: unknown) => caught);
      expect(error).toMatchObject({ code: 'UNKNOWN_ERROR', status, message });
      expect((error as ApiError).message).not.toMatch(/\d{3}/);
    },
  );

  it('thân lỗi có câu rỗng thì dùng câu theo mã trạng thái', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response(JSON.stringify({ error: { code: 'INTERNAL_ERROR', message: '' } }), {
            status: 500,
          }),
      ),
    );
    await expect(apiRequest('/api/x')).rejects.toMatchObject({
      message: 'Máy chủ đang gặp sự cố. Hãy thử lại sau.',
    });
  });
});
