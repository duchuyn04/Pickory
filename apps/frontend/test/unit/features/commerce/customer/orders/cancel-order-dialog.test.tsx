// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CancelOrderDialog } from '../../../../../../src/features/commerce/customer/orders/cancel-order-dialog';

let container: HTMLDivElement;
let root: Root;

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

describe('CancelOrderDialog', () => {
  it('chặn gửi lặp khi người dùng bấm đúp Hủy đơn và chỉ gọi onConfirm một lần', async () => {
    // onConfirm trả Promise chờ để giữ cờ isSubmitting đang chạy.
    let resolveConfirm: () => void = () => {};
    const confirmPromise = new Promise<void>((resolve) => {
      resolveConfirm = resolve;
    });
    const onConfirm = vi.fn(() => confirmPromise);
    const onClose = vi.fn();

    await act(async () => {
      root.render(<CancelOrderDialog code="PK12345678" onConfirm={onConfirm} onClose={onClose} />);
    });

    const confirmBtn = container.querySelector('button.btn-danger') as HTMLButtonElement;
    expect(confirmBtn).not.toBeNull();

    // Bấm liên tiếp hai lần trong cùng một lượt khi yêu cầu đang thực thi.
    await act(async () => {
      confirmBtn.click();
      confirmBtn.click();
    });

    // onConfirm chỉ được gọi đúng 1 lần nhờ cờ ref chặn bấm đúp.
    expect(onConfirm).toHaveBeenCalledTimes(1);

    // Giải phóng promise để kết thúc thao tác.
    await act(async () => {
      resolveConfirm();
    });
  });

  it('đóng hộp thoại khi bấm Giữ đơn mà không gọi onConfirm', async () => {
    const onConfirm = vi.fn();
    const onClose = vi.fn();

    await act(async () => {
      root.render(<CancelOrderDialog code="PK12345678" onConfirm={onConfirm} onClose={onClose} />);
    });

    const keepBtn = container.querySelector('button.btn-outline') as HTMLButtonElement;
    await act(async () => {
      keepBtn.click();
    });

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(onConfirm).not.toHaveBeenCalled();
  });
});
