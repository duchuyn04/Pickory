// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../../../../../src/app';
import type { OrderInvoicesResponse } from '../../../../../../src/features/commerce/admin/orders/order-invoices-schema';
import { findExportsPrintingExample } from '../../../exports-printing/contract-fixtures';
import { actorWith, findButton, waitFor } from '../../../identity/admin/admin-list-test-kit';
import {
  createCustomerActor,
  createGuestActor,
  createPrintHarness,
  jsonResponse,
  type PrintHarness,
} from './print-page-test-kit';

let harness: PrintHarness | undefined;

afterEach(async () => {
  await harness?.cleanup();
  harness = undefined;
  vi.restoreAllMocks();
});

describe('trang in hóa đơn - kiểm quyền và tải dữ liệu', () => {
  it('hiện thông báo đang nạp khi chưa có kết quả actor và không gọi API hóa đơn', async () => {
    let settleMe: ((response: Response) => void) | undefined;
    harness = createPrintHarness(
      () =>
        new Promise<Response>((resolve) => {
          settleMe = resolve;
        }),
      () => jsonResponse({}),
    );

    await harness.render('/admin/orders/print?codes=PK7KQ2M9XA');

    const status = harness.container.querySelector('[role="status"]');
    expect(status?.textContent).toContain('Đang nạp');
    expect(harness.requestedUrls).toEqual([]);
    expect(harness.printSpy).not.toHaveBeenCalled();

    // Đóng request đầu tiên của module API để các ca kiểm thử sau không bị kẹt ở cổng chờ.
    settleMe?.(jsonResponse({ data: createGuestActor() }));
    await act(async () => {
      await Promise.resolve();
    });
  });

  it('chuyển hướng về trang đăng nhập quản trị khi người dùng là khách vãng lai', async () => {
    harness = createPrintHarness(
      () => jsonResponse({ data: createGuestActor() }),
      () => jsonResponse({}),
    );

    await harness.render('/admin/orders/print?codes=PK7KQ2M9XA');
    await waitFor(
      () => harness?.container.querySelector('[data-testid="admin-login-redirect"]') !== null,
      'chuyển hướng khi là khách vãng lai',
    );

    expect(harness.requestedUrls).toEqual([]);
    expect(harness.printSpy).not.toHaveBeenCalled();
  });

  it('chuyển hướng về trang đăng nhập quản trị khi tài khoản không phải nhân viên', async () => {
    harness = createPrintHarness(
      () => jsonResponse({ data: createCustomerActor() }),
      () => jsonResponse({}),
    );

    await harness.render('/admin/orders/print?codes=PK7KQ2M9XA');
    await waitFor(
      () => harness?.container.querySelector('[data-testid="admin-login-redirect"]') !== null,
      'chuyển hướng khi là khách hàng',
    );

    expect(harness.requestedUrls).toEqual([]);
    expect(harness.printSpy).not.toHaveBeenCalled();
  });

  it('hiện ForbiddenNotice và không gọi API khi thiếu quyền orders:read', async () => {
    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['products:read']) }),
      () => jsonResponse({}),
    );

    await harness.render('/admin/orders/print?codes=PK7KQ2M9XA');
    await waitFor(
      () => (harness?.container.textContent ?? '').includes('403 - Không có quyền truy cập'),
      'thông báo thiếu quyền',
    );

    expect(harness.container.textContent).toContain('orders:read');
    expect(harness.container.textContent).toContain('hóa đơn');
    expect(harness.requestedUrls).toEqual([]);
    expect(harness.printSpy).not.toHaveBeenCalled();
    expect(findButton(harness.container, 'Đóng')).toBeDefined();
    expect(findButton(harness.container, 'In')).toBeUndefined();
  });

  it('hiện ErrorAlert và không gọi API khi danh sách mã đơn rỗng', async () => {
    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => jsonResponse({}),
    );

    await harness.render('/admin/orders/print?codes=');
    await waitFor(
      () => (harness?.container.textContent ?? '').includes('Chọn ít nhất 1 đơn để in.'),
      'lỗi thiếu mã đơn',
    );

    expect(harness.requestedUrls).toEqual([]);
    expect(harness.printSpy).not.toHaveBeenCalled();
    expect(findButton(harness.container, 'Đóng')).toBeDefined();
    expect(findButton(harness.container, 'In')).toBeUndefined();
  });

  it('hiện ForbiddenNotice khi API trả 403', async () => {
    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices missing permission',
    );
    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => jsonResponse(example.response, example.status),
    );

    await harness.render('/admin/orders/print?codes=PK7KQ2M9XA');
    await waitFor(
      () => (harness?.container.textContent ?? '').includes('403 - Không có quyền truy cập'),
      'thông báo 403 từ API',
    );

    expect(harness.requestedUrls).toEqual(['/api/admin/order-invoices?codes=PK7KQ2M9XA']);
    expect(harness.printSpy).not.toHaveBeenCalled();
  });

  it('hiện ErrorAlert kèm mã đơn thiếu khi API trả 404 ORDER_NOT_FOUND', async () => {
    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices unknown code',
    );
    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => jsonResponse(example.response, example.status),
    );

    await harness.render('/admin/orders/print?codes=PK7KQ2M9XA,PKZZZZZZZZ');
    await waitFor(
      () => (harness?.container.textContent ?? '').includes('PKZZZZZZZZ'),
      'lỗi 404 mã đơn thiếu',
    );

    expect(harness.container.textContent).toContain('Không tìm thấy một số đơn hàng.');
    expect(harness.container.textContent).toContain('Không tìm thấy đơn PKZZZZZZZZ.');
    expect(harness.printSpy).not.toHaveBeenCalled();
  });

  it('hiện thông báo lỗi khi API trả 400 vượt quá 50 đơn', async () => {
    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices too many codes',
    );
    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => jsonResponse(example.response, example.status),
    );

    await harness.render('/admin/orders/print?codes=PK7KQ2M9XA');
    await waitFor(
      () => (harness?.container.textContent ?? '').includes('Mỗi lần in tối đa 50 đơn.'),
      'lỗi 400 quá số đơn',
    );

    expect(harness.printSpy).not.toHaveBeenCalled();
  });

  it('hiện ErrorAlert khi gặp lỗi mạng', async () => {
    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => Promise.reject(new TypeError('Failed to fetch')),
    );

    await harness.render('/admin/orders/print?codes=PK7KQ2M9XA');
    await waitFor(
      () => (harness?.container.textContent ?? '').includes('Không kết nối được máy chủ.'),
      'lỗi mạng',
    );

    expect(harness.printSpy).not.toHaveBeenCalled();
  });

  it('hiện lỗi và không vẽ hóa đơn khi response sai cấu trúc hợp đồng', async () => {
    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices shop profile not set',
    );
    const broken = structuredClone(example.response) as {
      data: { invoices: Record<string, unknown>[] };
    };
    delete broken.data.invoices[0]!.vatInvoice;

    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => jsonResponse(broken, 200),
    );

    await harness.render('/admin/orders/print?codes=PK3HN8RT2B');
    await waitFor(
      () => (harness?.container.textContent ?? '').includes('Tải dữ liệu hóa đơn thất bại.'),
      'lỗi sai schema response',
    );

    expect(harness.container.querySelectorAll('article')).toHaveLength(0);
    expect(harness.printSpy).not.toHaveBeenCalled();
  });

  it('hiện thông báo đang nạp hóa đơn trong lúc chờ API trả dữ liệu', async () => {
    let settleApi: ((response: Response) => void) | undefined;
    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () =>
        new Promise<Response>((resolve) => {
          settleApi = resolve;
        }),
    );

    await harness.render('/admin/orders/print?codes=PK3HN8RT2B');
    await waitFor(
      () =>
        (harness?.container.querySelector('[role="status"]')?.textContent ?? '').includes(
          'Đang nạp hóa đơn',
        ),
      'hiện trạng thái đang nạp hóa đơn',
    );

    expect(harness.container.querySelector('[role="alert"]')).toBeNull();
    expect(findButton(harness.container, 'In')).toBeUndefined();
    expect(findButton(harness.container, 'Đóng')).toBeDefined();
    expect(harness.printSpy).not.toHaveBeenCalled();

    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices shop profile not set',
    );
    settleApi?.(jsonResponse(example.response, example.status));
    await act(async () => {
      await Promise.resolve();
    });
  });

  it('hiện thông báo khi API trả danh sách hóa đơn rỗng', async () => {
    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices shop profile not set',
    );
    const emptyPayload = structuredClone(example.response) as OrderInvoicesResponse;
    emptyPayload.data.invoices = [];

    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => jsonResponse(emptyPayload, 200),
    );

    await harness.render('/admin/orders/print?codes=PK3HN8RT2B');
    await waitFor(
      () => (harness?.container.textContent ?? '').includes('Không có hóa đơn để in.'),
      'thông báo danh sách hóa đơn rỗng',
    );

    expect(findButton(harness.container, 'In')).toBeUndefined();
    expect(findButton(harness.container, 'Đóng')).toBeDefined();
    expect(harness.printSpy).not.toHaveBeenCalled();
  });
});

describe('trang in hóa đơn - mở hộp thoại in một lần và thanh công cụ', () => {
  it('chỉ gọi window.print sau khi decode() ảnh logo hoàn tất và gọi đúng một lần dù có nhiều hóa đơn', async () => {
    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices two orders with vat and cancelled',
    );
    const pendingDecodes: Array<() => void> = [];

    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => jsonResponse(example.response, example.status),
    );

    // Giữ promise decode ở trạng thái chờ để kiểm trang không mở hộp thoại in quá sớm.
    Object.defineProperty(HTMLImageElement.prototype, 'decode', {
      configurable: true,
      writable: true,
      value: vi.fn(
        () =>
          new Promise<void>((resolve) => {
            pendingDecodes.push(resolve);
          }),
      ),
    });

    await harness.render('/admin/orders/print?codes=PK7KQ2M9XA,PK3HN8RT2B');
    await waitFor(
      () => harness?.container.querySelectorAll('article.invoice-sheet').length === 2,
      'vẽ đủ hai hóa đơn trước khi decode',
    );

    expect(harness.printSpy).not.toHaveBeenCalled();

    // Chỉ mới giải mã logo đầu tiên thì trang vẫn phải chờ logo thứ hai.
    await act(async () => {
      pendingDecodes[0]?.();
      await Promise.resolve();
    });
    expect(harness.printSpy).not.toHaveBeenCalled();

    // Khi toàn bộ logo đã giải mã xong, hộp thoại in mới mở đúng một lần.
    await act(async () => {
      for (const resolve of pendingDecodes) {
        resolve();
      }
      await Promise.resolve();
    });

    await waitFor(() => harness?.printSpy.mock.calls.length === 1, 'gọi window.print lần đầu');
    expect(harness.printSpy).toHaveBeenCalledTimes(1);
  });

  it('vẫn gọi window.print một lần khi decode() bị từ chối do ảnh hỏng', async () => {
    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices shop profile not set',
    );

    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => jsonResponse(example.response, example.status),
    );

    Object.defineProperty(HTMLImageElement.prototype, 'decode', {
      configurable: true,
      writable: true,
      value: vi.fn(() => Promise.reject(new Error('Image broken'))),
    });

    await harness.render('/admin/orders/print?codes=PK3HN8RT2B');
    await waitFor(
      () => harness?.printSpy.mock.calls.length === 1,
      'gọi window.print dù decode bị từ chối',
    );

    expect(harness.printSpy).toHaveBeenCalledTimes(1);
  });

  it('không gọi window.print lần thứ hai khi component render lại hoặc nạp lại dữ liệu', async () => {
    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices shop profile not set',
    );
    let fetchCount = 0;

    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => {
        fetchCount += 1;
        const payload = structuredClone(example.response) as OrderInvoicesResponse;
        // Đổi nhẹ dữ liệu ở lần nạp thứ hai để TanStack Query cấp tham chiếu mới kích hoạt effect.
        if (fetchCount > 1) {
          payload.data.invoices[0]!.customer.name = 'Lê Văn Hải Cập Nhật';
        }
        return jsonResponse(payload, example.status);
      },
    );

    await harness.render('/admin/orders/print?codes=PK3HN8RT2B');
    await waitFor(() => harness?.printSpy.mock.calls.length === 1, 'gọi window.print lần đầu');

    // Buộc render lại và nạp lại dữ liệu mới từ API để kiểm biến ref chặn gọi in lần hai.
    await harness.render('/admin/orders/print?codes=PK3HN8RT2B');
    await act(async () => {
      await harness?.client.invalidateQueries({ queryKey: ['admin-order-invoices'] });
    });
    await waitFor(
      () => (harness?.container.textContent ?? '').includes('Lê Văn Hải Cập Nhật'),
      'nạp lại dữ liệu hóa đơn lần hai',
    );
    await act(async () => {
      await Promise.resolve();
    });

    expect(harness.printSpy).toHaveBeenCalledTimes(1);
  });

  it('không gọi window.print khi rời trang trước lúc giải mã ảnh logo xong', async () => {
    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices shop profile not set',
    );
    let resolveDecode: (() => void) | undefined;

    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => jsonResponse(example.response, example.status),
    );

    Object.defineProperty(HTMLImageElement.prototype, 'decode', {
      configurable: true,
      writable: true,
      value: vi.fn(
        () =>
          new Promise<void>((resolve) => {
            resolveDecode = resolve;
          }),
      ),
    });

    await harness.render('/admin/orders/print?codes=PK3HN8RT2B');
    await waitFor(
      () => harness?.container.querySelectorAll('article.invoice-sheet').length === 1,
      'vẽ hóa đơn trước khi rời trang',
    );

    await harness.cleanup();
    harness = undefined;

    // Cho lời hứa decode hoàn tất sau khi component đã gỡ khỏi cây tài liệu.
    resolveDecode?.();
    await act(async () => {
      await Promise.resolve();
    });

    expect(resolveDecode).toBeDefined();
    expect(window.print).not.toHaveBeenCalled();
  });

  it('chỉ gọi window.print một lần trong StrictMode khi cache đã có dữ liệu ngay lúc mount', async () => {
    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices shop profile not set',
    );
    const cachedData = (example.response as OrderInvoicesResponse).data;

    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => jsonResponse(example.response, example.status),
    );

    // Nạp sẵn actor và hóa đơn vào cache để effect phụ thuộc dữ liệu chạy hai lần ngay lúc mount.
    harness.client.setQueryData(['actor'], actorWith(['orders:read']));
    harness.client.setQueryData(['admin-order-invoices', ['PK3HN8RT2B']], cachedData);

    await harness.render('/admin/orders/print?codes=PK3HN8RT2B');
    await waitFor(() => harness?.printSpy.mock.calls.length === 1, 'gọi in một lần từ cache');
    await act(async () => {
      await Promise.resolve();
    });

    expect(harness.printSpy).toHaveBeenCalledTimes(1);
  });

  it('không gọi lại API khi cửa sổ lấy lại focus sau lúc mở hộp thoại in', async () => {
    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices shop profile not set',
    );
    let fetchCount = 0;

    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => {
        fetchCount += 1;
        if (fetchCount > 1) {
          return Promise.reject(new TypeError('Network failure'));
        }
        return jsonResponse(example.response, example.status);
      },
    );

    await harness.render('/admin/orders/print?codes=PK3HN8RT2B');
    await waitFor(() => harness?.printSpy.mock.calls.length === 1, 'gọi in lần đầu');

    await act(async () => {
      window.dispatchEvent(new Event('focus'));
      window.dispatchEvent(new Event('visibilitychange'));
      document.dispatchEvent(new Event('visibilitychange'));
      await new Promise((resolve) => setTimeout(resolve, 50));
    });

    expect(fetchCount).toBe(1);
    expect(
      harness.container.querySelector('article[data-testid="invoice-PK3HN8RT2B"]'),
    ).not.toBeNull();
    expect(harness.container.querySelector('[role="alert"]')).toBeNull();
  });

  it('đặt document.title theo số lượng đơn và trả lại tiêu đề cũ khi rời trang', async () => {
    const singleExample = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices shop profile not set',
    );
    const twoExample = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices two orders with vat and cancelled',
    );
    document.title = 'Tiêu đề gốc';

    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => jsonResponse(singleExample.response, singleExample.status),
    );
    await harness.render('/admin/orders/print?codes=PK3HN8RT2B');
    await waitFor(() => document.title === 'Hóa đơn PK3HN8RT2B', 'đổi tiêu đề một đơn');
    await harness.cleanup();
    expect(document.title).toBe('Tiêu đề gốc');

    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => jsonResponse(twoExample.response, twoExample.status),
    );
    await harness.render('/admin/orders/print?codes=PK7KQ2M9XA,PK3HN8RT2B');
    await waitFor(() => document.title === 'In hóa đơn', 'đổi tiêu đề nhiều đơn');
    await harness.cleanup();
    harness = undefined;
    expect(document.title).toBe('Tiêu đề gốc');
  });

  it('thanh công cụ chứa nút In gọi thêm window.print và nút Đóng gọi window.close', async () => {
    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices shop profile not set',
    );

    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => jsonResponse(example.response, example.status),
    );

    await harness.render('/admin/orders/print?codes=PK3HN8RT2B');
    await waitFor(() => harness?.printSpy.mock.calls.length === 1, 'gọi window.print tự động');

    const toolbar = harness.container.querySelector('.invoice-print-toolbar');
    expect(toolbar).not.toBeNull();

    const printButton = findButton(toolbar as HTMLElement, 'In');
    const closeButton = findButton(toolbar as HTMLElement, 'Đóng');
    expect(printButton).toBeDefined();
    expect(closeButton).toBeDefined();

    await act(async () => {
      printButton!.click();
    });
    expect(harness.printSpy).toHaveBeenCalledTimes(2);

    await act(async () => {
      closeButton!.click();
    });
    expect(harness.closeSpy).toHaveBeenCalledTimes(1);
  });

  it('route /admin/orders/print trong App dựng trang in ngoài khung quản trị', async () => {
    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices shop profile not set',
    );
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const printSpy = vi.spyOn(window, 'print').mockImplementation(() => {});
    const originalDecodeDescriptor = Object.getOwnPropertyDescriptor(
      HTMLImageElement.prototype,
      'decode',
    );
    Object.defineProperty(HTMLImageElement.prototype, 'decode', {
      configurable: true,
      writable: true,
      value: vi.fn(() => Promise.resolve()),
    });

    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input);
        if (url === '/api/me') return jsonResponse({ data: actorWith(['orders:read']) });
        return jsonResponse(example.response, example.status);
      }),
    );

    try {
      await act(async () => {
        root.render(
          <StrictMode>
            <QueryClientProvider client={client}>
              <MemoryRouter initialEntries={['/admin/orders/print?codes=PK3HN8RT2B']}>
                <App />
              </MemoryRouter>
            </QueryClientProvider>
          </StrictMode>,
        );
      });

      await waitFor(
        () => container.querySelector('article[data-testid="invoice-PK3HN8RT2B"]') !== null,
        'route trang in trong App',
      );

      expect(container.querySelector('#admin-sidebar')).toBeNull();
      expect(printSpy).toHaveBeenCalledTimes(1);
    } finally {
      await act(async () => root.unmount());
      container.remove();
      client.clear();
      if (originalDecodeDescriptor) {
        Object.defineProperty(HTMLImageElement.prototype, 'decode', originalDecodeDescriptor);
      } else {
        delete (HTMLImageElement.prototype as Partial<HTMLImageElement>).decode;
      }
      vi.unstubAllGlobals();
    }
  });
});
