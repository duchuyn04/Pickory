import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, apiRequest } from '../../../src/shared/api';

afterEach(() => {
  vi.unstubAllGlobals();
});
// Tách deferred promise để điều khiển thời điểm phản hồi trong test đồng thời.
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

// Nạp lại module để mỗi kịch bản có trạng thái request đầu độc lập.
async function loadFreshApiModule() {
  vi.resetModules();
  return import('../../../src/shared/api');
}

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

  it('hai lời gọi apiRequest cùng lúc thì fetch thứ hai chỉ chạy sau khi fetch thứ nhất trả kết quả', async () => {
    const { apiRequest: freshApiRequest } = await loadFreshApiModule();
    const firstDeferred = createDeferred<Response>();
    const secondDeferred = createDeferred<Response>();
    const fetchMock = vi.fn((path: string) => {
      if (path === '/api/first') return firstDeferred.promise;
      if (path === '/api/second') return secondDeferred.promise;
      throw new Error(`Đường dẫn không mong đợi: ${path}`);
    });
    vi.stubGlobal('fetch', fetchMock);

    // Gửi hai request cùng lúc để kiểm tra thứ tự thực hiện.
    const firstCall = freshApiRequest<{ id: string }>('/api/first');
    const secondCall = freshApiRequest<{ id: string }>('/api/second');

    // Chờ microtask để đảm bảo request thứ hai bắt đầu nếu không có bước chặn.
    await Promise.resolve();

    // Request đầu chạy một mình trong lúc fetch chưa trả kết quả.
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledWith('/api/first', expect.anything());

    // Mở phản hồi của request đầu để giải phóng request thứ hai.
    firstDeferred.resolve(
      new Response(JSON.stringify({ data: { id: 'first-done' } }), { status: 200 }),
    );
    await expect(firstCall).resolves.toEqual({ id: 'first-done' });

    // Request thứ hai chỉ được gọi fetch sau khi request đầu hoàn tất.
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock).toHaveBeenLastCalledWith('/api/second', expect.anything());

    // Hoàn tất request thứ hai để dọn dẹp bộ nhớ.
    secondDeferred.resolve(
      new Response(JSON.stringify({ data: { id: 'second-done' } }), { status: 200 }),
    );
    await expect(secondCall).resolves.toEqual({ id: 'second-done' });
  });

  it('sau request đầu, các request sau chạy song song, không chờ nhau', async () => {
    const { apiRequest: freshApiRequest } = await loadFreshApiModule();
    const secondDeferred = createDeferred<Response>();
    const thirdDeferred = createDeferred<Response>();
    const fetchMock = vi.fn((path: string) => {
      if (path === '/api/first') {
        return Promise.resolve(
          new Response(JSON.stringify({ data: { id: 'first-done' } }), { status: 200 }),
        );
      }
      if (path === '/api/second') return secondDeferred.promise;
      if (path === '/api/third') return thirdDeferred.promise;
      throw new Error(`Đường dẫn không mong đợi: ${path}`);
    });
    vi.stubGlobal('fetch', fetchMock);

    // Hoàn tất request đầu trước khi kiểm tra tính song song của các request sau.
    await expect(freshApiRequest('/api/first')).resolves.toEqual({ id: 'first-done' });
    expect(fetchMock).toHaveBeenCalledTimes(1);

    // Gửi đồng thời hai request tiếp theo khi cả hai deferred đều chưa mở.
    const secondCall = freshApiRequest<{ id: string }>('/api/second');
    const thirdCall = freshApiRequest<{ id: string }>('/api/third');

    // Chờ microtask để cả hai request tiến vào bước gọi fetch.
    await Promise.resolve();

    // Cả hai request sau đều đã gọi fetch đồng thời mà không chờ nhau.
    expect(fetchMock).toHaveBeenCalledTimes(3);

    // Giải phóng hai deferred để hoàn tất kịch bản.
    secondDeferred.resolve(
      new Response(JSON.stringify({ data: { id: 'second-done' } }), { status: 200 }),
    );
    thirdDeferred.resolve(
      new Response(JSON.stringify({ data: { id: 'third-done' } }), { status: 200 }),
    );

    await expect(secondCall).resolves.toEqual({ id: 'second-done' });
    await expect(thirdCall).resolves.toEqual({ id: 'third-done' });
  });

  it('request đầu gặp lỗi mạng không làm treo và không chặn request thứ hai', async () => {
    const { apiRequest: freshApiRequest } = await loadFreshApiModule();
    const firstDeferred = createDeferred<Response>();
    const secondDeferred = createDeferred<Response>();
    const fetchMock = vi.fn((path: string) => {
      if (path === '/api/first') return firstDeferred.promise;
      if (path === '/api/second') return secondDeferred.promise;
      throw new Error(`Đường dẫn không mong đợi: ${path}`);
    });
    vi.stubGlobal('fetch', fetchMock);

    const firstCall = freshApiRequest('/api/first');
    const secondCall = freshApiRequest<{ id: string }>('/api/second');

    // Gây lỗi mạng ở request đầu tiên.
    firstDeferred.reject(new TypeError('Failed to fetch'));

    await expect(firstCall).rejects.toMatchObject({ code: 'NETWORK_ERROR' });

    // Request thứ hai vẫn được giải phóng để gọi fetch sau khi request đầu lỗi.
    expect(fetchMock).toHaveBeenCalledTimes(2);

    secondDeferred.resolve(
      new Response(JSON.stringify({ data: { id: 'second-ok' } }), { status: 200 }),
    );
    await expect(secondCall).resolves.toEqual({ id: 'second-ok' });
  });

  it('request đầu trả lỗi HTTP 500 không chặn request thứ hai', async () => {
    const { apiRequest: freshApiRequest } = await loadFreshApiModule();
    const firstDeferred = createDeferred<Response>();
    const secondDeferred = createDeferred<Response>();
    const fetchMock = vi.fn((path: string) => {
      if (path === '/api/first') return firstDeferred.promise;
      if (path === '/api/second') return secondDeferred.promise;
      throw new Error(`Đường dẫn không mong đợi: ${path}`);
    });
    vi.stubGlobal('fetch', fetchMock);

    const firstCall = freshApiRequest('/api/first');
    const secondCall = freshApiRequest<{ id: string }>('/api/second');

    // Giả lập máy chủ phản hồi lỗi 500 cho request đầu tiên.
    firstDeferred.resolve(new Response('Server Error', { status: 500 }));

    await expect(firstCall).rejects.toMatchObject({ code: 'UNKNOWN_ERROR', status: 500 });

    // Bước chờ phải được giải phóng để request thứ hai nhận kết quả riêng.
    expect(fetchMock).toHaveBeenCalledTimes(2);

    secondDeferred.resolve(
      new Response(JSON.stringify({ data: { id: 'second-ok' } }), { status: 200 }),
    );
    await expect(secondCall).resolves.toEqual({ id: 'second-ok' });
  });
});
