// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React, { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdjustStockDialog } from '../../../../../src/features/catalog/admin/adjust-stock-dialog';
import type { AdjustStockResult } from '../../../../../src/features/catalog/admin/inventory-logic';

let container: HTMLDivElement;
let root: Root;

const result: AdjustStockResult = {
  skuId: 'sku-1',
  stock: 12,
  movement: {
    id: 'mv-1',
    delta: 7,
    type: 'manual',
    orderId: null,
    actorId: 'user-admin',
    reason: 'Nhập lô mới',
    createdAt: '2026-10-08T03:30:00Z',
  },
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

// Harness có nút mở dialog như trang thật, để test kiểm focus trả về đúng nút đã mở.
async function renderDialog(fetchMock: ReturnType<typeof vi.fn>) {
  vi.stubGlobal('fetch', fetchMock);
  const onAdjusted = vi.fn();
  function Harness() {
    const [open, setOpen] = React.useState(false);
    return (
      <QueryClientProvider client={new QueryClient()}>
        <button type="button" id="opener" onClick={() => setOpen(true)}>
          Điều chỉnh tồn kho
        </button>
        {open && (
          <AdjustStockDialog
            skuId="sku-1"
            skuLabel="MAC-01"
            currentStock={5}
            onClose={() => setOpen(false)}
            onAdjusted={onAdjusted}
          />
        )}
      </QueryClientProvider>
    );
  }
  await act(async () => root.render(<Harness />));
  const opener = document.querySelector<HTMLButtonElement>('#opener')!;
  // Trình duyệt thật đưa focus vào nút khi bấm. `click()` của DOM không làm vậy nên test tự đặt focus.
  opener.focus();
  await act(async () => opener.click());
  return onAdjusted;
}

async function change(selector: string, value: string) {
  const element = document.querySelector<
    HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement
  >(selector)!;
  await act(async () => {
    // Dùng setter gốc để React nhận thay đổi như sự kiện nhập thật.
    const prototype =
      element instanceof HTMLSelectElement
        ? HTMLSelectElement.prototype
        : element instanceof HTMLTextAreaElement
          ? HTMLTextAreaElement.prototype
          : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(prototype, 'value')!.set!.call(element, value);
    element.dispatchEvent(
      new Event(element instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }),
    );
  });
}

function submitButton(): HTMLButtonElement {
  return document.querySelector<HTMLButtonElement>('button[type="submit"]')!;
}

async function submit() {
  await act(async () => submitButton().click());
}

function previewValue(label: string): string | undefined {
  const term = [...document.querySelectorAll('dt')].find((item) => item.textContent === label);
  return term?.nextElementSibling?.textContent ?? undefined;
}

describe('AdjustStockDialog', () => {
  it('hiện tồn kho hiện tại và tồn kho sau điều chỉnh theo số đang nhập', async () => {
    await renderDialog(vi.fn());
    expect(previewValue('Tồn kho hiện tại')).toBe('5');
    expect(previewValue('Sau điều chỉnh')).toBe('5');
    await change('#adjust-delta', '7');
    expect(previewValue('Sau điều chỉnh')).toBe('12');
    await change('#adjust-delta', '-8');
    expect(previewValue('Sau điều chỉnh')).toBe('-3');
    await change('#adjust-delta', '1.5');
    expect(previewValue('Sau điều chỉnh')).toBe('Chưa hợp lệ');
  });

  it('bỏ trống lý do thì lỗi hiện dưới ô và không gửi request', async () => {
    const fetchMock = vi.fn();
    await renderDialog(fetchMock);
    await change('#adjust-delta', '3');
    await submit();

    const reason = document.querySelector<HTMLTextAreaElement>('#adjust-reason')!;
    expect(reason.getAttribute('aria-invalid')).toBe('true');
    expect(reason.getAttribute('aria-describedby')).toBe('adjust-reason-error');
    expect(document.getElementById('adjust-reason-error')?.textContent).toBe('Lý do là bắt buộc.');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('số lượng bằng 0 hoặc không phải số nguyên thì lỗi hiện dưới ô số lượng', async () => {
    const fetchMock = vi.fn();
    await renderDialog(fetchMock);
    await change('#adjust-reason', 'Lý do');
    await change('#adjust-delta', '0');
    await submit();
    const delta = document.querySelector<HTMLInputElement>('#adjust-delta')!;
    expect(delta.getAttribute('aria-invalid')).toBe('true');
    expect(delta.getAttribute('aria-describedby')).toBe('adjust-delta-error');
    expect(document.getElementById('adjust-delta-error')?.textContent).toBe(
      'Số lượng thay đổi phải khác 0.',
    );
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('khách trả hàng với số lượng âm bị từ chối ở client', async () => {
    const fetchMock = vi.fn();
    await renderDialog(fetchMock);
    await change('#adjust-type', 'return');
    await change('#adjust-delta', '-2');
    await change('#adjust-reason', 'Lý do');
    await submit();
    expect(document.getElementById('adjust-delta-error')?.textContent).toBe(
      'Khách trả hàng phải có số lượng lớn hơn 0.',
    );
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('gửi đúng body và báo kết quả cho trang', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ data: result }));
    const onAdjusted = await renderDialog(fetchMock);
    await change('#adjust-delta', '7');
    await change('#adjust-reason', '  Nhập lô mới  ');
    await submit();

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('/api/admin/skus/sku-1/adjust-stock');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({
      delta: 7,
      reason: 'Nhập lô mới',
      type: 'manual',
    });
    expect(onAdjusted).toHaveBeenCalledWith(result);
    // Thành công thì dialog đóng.
    expect(document.querySelector('[role="dialog"]')).toBeNull();
  });

  it('khóa nút gửi trong lúc chờ API để không gửi hai lần', async () => {
    let resolveResponse: (value: Response) => void = () => {};
    const fetchMock = vi.fn().mockReturnValue(
      new Promise<Response>((resolve) => {
        resolveResponse = resolve;
      }),
    );
    await renderDialog(fetchMock);
    await change('#adjust-delta', '1');
    await change('#adjust-reason', 'Lý do');
    await submit();

    expect(submitButton().disabled).toBe(true);
    await submit();
    expect(fetchMock).toHaveBeenCalledTimes(1);

    await act(async () => resolveResponse(jsonResponse({ data: result })));
    expect(document.querySelector('[role="dialog"]')).toBeNull();
  });

  it('gửi form lần hai bằng phím Enter trong lúc chờ API cũng không tạo request thứ hai', async () => {
    const fetchMock = vi.fn().mockReturnValue(new Promise<Response>(() => {}));
    await renderDialog(fetchMock);
    await change('#adjust-delta', '1');
    await change('#adjust-reason', 'Lý do');
    await submit();

    // Phím Enter gửi sự kiện submit của form, không đi qua nút đã bị khóa.
    const form = document.querySelector<HTMLFormElement>('#adjust-stock-form')!;
    await act(async () => {
      form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('lỗi 409 hiện thông báo tiếng Việt của API và dialog không đóng', async () => {
    const message = 'Tồn kho hiện có 5, không thể giảm 9.';
    const fetchMock = vi
      .fn()
      .mockResolvedValue(jsonResponse({ error: { code: 'INSUFFICIENT_STOCK', message } }, 409));
    const onAdjusted = await renderDialog(fetchMock);
    await change('#adjust-delta', '-9');
    await change('#adjust-reason', 'Kiểm kê lệch');
    await submit();

    expect(document.querySelector('[role="dialog"] [role="alert"]')?.textContent).toBe(message);
    expect(document.querySelector('[role="dialog"]')).not.toBeNull();
    expect(onAdjusted).not.toHaveBeenCalled();
    // Giữ nguyên dữ liệu đã nhập và mở lại nút gửi để admin sửa số lượng.
    expect(document.querySelector<HTMLInputElement>('#adjust-delta')!.value).toBe('-9');
    expect(submitButton().disabled).toBe(false);
  });

  it('đóng bằng phím Esc thì trả focus về nút đã mở dialog', async () => {
    await renderDialog(vi.fn());
    const opener = document.querySelector<HTMLButtonElement>('#opener')!;
    await act(async () =>
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })),
    );
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    expect(document.activeElement).toBe(opener);
  });
});
