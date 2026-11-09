// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../../../src/shared/api';
import { EndSessionDialog } from '../../../../../src/features/support-desk/admin/end-session-dialog';
import * as supportApi from '../../../../../src/features/support-desk/admin/support-desk-api';

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

describe('EndSessionDialog', () => {
  it('hiển thị tiêu đề, lời nhắc chấm điểm và nút Hủy đóng hộp thoại', async () => {
    const onClose = vi.fn();
    const onSuccess = vi.fn();
    const onSessionClosed = vi.fn();

    await act(async () => {
      root.render(
        <EndSessionDialog
          sessionId="session-1"
          onSuccess={onSuccess}
          onSessionClosed={onSessionClosed}
          onClose={onClose}
        />,
      );
    });

    // Xác nhận tiêu đề và nội dung hướng dẫn chấm điểm của hộp thoại.
    expect(container.querySelector('#end-session-title')?.textContent).toBe(
      'Kết thúc hội thoại này?',
    );
    expect(container.textContent).toContain(
      'Khách hàng sẽ nhận được biểu mẫu chấm điểm dịch vụ sau khi kết thúc hội thoại.',
    );

    const cancelButton = container.querySelector(
      '.dialog-foot button.btn-outline',
    ) as HTMLButtonElement;
    expect(cancelButton).not.toBeNull();

    await act(async () => {
      cancelButton.click();
    });

    // Nút Hủy phải gọi hàm đóng hộp thoại.
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(onSuccess).not.toHaveBeenCalled();
  });

  it('gọi API kết thúc phiên và đóng hộp thoại khi thành công', async () => {
    const onClose = vi.fn();
    const onSuccess = vi.fn();
    const onSessionClosed = vi.fn();
    const closeSpy = vi.spyOn(supportApi, 'closeSupportSession').mockResolvedValueOnce({
      sessionId: 'session-1',
      closedAt: '2026-11-09T09:00:00Z',
      closedBy: 'staff-0001',
    });

    await act(async () => {
      root.render(
        <EndSessionDialog
          sessionId="session-1"
          onSuccess={onSuccess}
          onSessionClosed={onSessionClosed}
          onClose={onClose}
        />,
      );
    });

    const confirmButton = container.querySelector(
      '.dialog-foot button.btn-danger',
    ) as HTMLButtonElement;

    await act(async () => {
      confirmButton.click();
    });

    // Xác nhận API được gọi với đúng mã phiên và gọi callback thành công.
    expect(closeSpy).toHaveBeenCalledWith('session-1');
    expect(onSuccess).toHaveBeenCalledTimes(1);
  });

  it('chặn gửi lặp khi người dùng bấm đúp nút Kết thúc', async () => {
    let resolveClose: () => void = () => {};
    const closePromise = new Promise<{ sessionId: string; closedAt: string; closedBy: string }>(
      (resolve) => {
        resolveClose = () =>
          resolve({
            sessionId: 'session-1',
            closedAt: '2026-11-09T09:00:00Z',
            closedBy: 'staff-0001',
          });
      },
    );
    const closeSpy = vi.spyOn(supportApi, 'closeSupportSession').mockReturnValueOnce(closePromise);

    await act(async () => {
      root.render(
        <EndSessionDialog
          sessionId="session-1"
          onSuccess={vi.fn()}
          onSessionClosed={vi.fn()}
          onClose={vi.fn()}
        />,
      );
    });

    const confirmButton = container.querySelector(
      '.dialog-foot button.btn-danger',
    ) as HTMLButtonElement;

    // Bấm đúp liên tiếp trong lúc request đang xử lý.
    await act(async () => {
      confirmButton.click();
      confirmButton.click();
    });

    // Chỉ gửi một request duy nhất lên máy chủ.
    expect(closeSpy).toHaveBeenCalledTimes(1);

    await act(async () => {
      resolveClose();
    });
  });

  it('gọi onSessionClosed và onClose khi API trả lỗi 409 SUPPORT_SESSION_CLOSED', async () => {
    vi.spyOn(supportApi, 'closeSupportSession').mockRejectedValueOnce(
      new ApiError('SUPPORT_SESSION_CLOSED', 409, 'Phiên đã kết thúc.'),
    );
    const onSessionClosed = vi.fn();
    const onClose = vi.fn();

    await act(async () => {
      root.render(
        <EndSessionDialog
          sessionId="session-1"
          onSuccess={vi.fn()}
          onSessionClosed={onSessionClosed}
          onClose={onClose}
        />,
      );
    });

    const confirmButton = container.querySelector(
      '.dialog-foot button.btn-danger',
    ) as HTMLButtonElement;

    await act(async () => {
      confirmButton.click();
    });

    // Lỗi 409 cập nhật trạng thái phiên đã kết thúc và đóng hộp thoại.
    expect(onSessionClosed).toHaveBeenCalledTimes(1);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('hiển thị ErrorAlert khi API trả ApiError, giữ hộp thoại mở và nút Kết thúc bật lại', async () => {
    vi.spyOn(supportApi, 'closeSupportSession').mockRejectedValueOnce(
      new ApiError('INTERNAL_ERROR', 500, 'Lỗi máy chủ nội bộ.'),
    );
    const onSessionClosed = vi.fn();
    const onClose = vi.fn();

    await act(async () => {
      root.render(
        <EndSessionDialog
          sessionId="session-1"
          onSuccess={vi.fn()}
          onSessionClosed={onSessionClosed}
          onClose={onClose}
        />,
      );
    });

    const confirmButton = container.querySelector(
      '.dialog-foot button.btn-danger',
    ) as HTMLButtonElement;

    await act(async () => {
      confirmButton.click();
    });

    // Thông báo lỗi phải xuất hiện trong vùng role=alert.
    const alert = container.querySelector('[role="alert"]');
    expect(alert).not.toBeNull();
    expect(alert?.textContent).toContain('Lỗi máy chủ nội bộ.');

    // Hộp thoại vẫn mở và nút Kết thúc được bật lại để thử lại.
    expect(container.querySelector('#end-session-title')).not.toBeNull();
    expect(confirmButton.disabled).toBe(false);
    expect(onSessionClosed).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
  });

  it('hiển thị câu lỗi mặc định khi gặp lỗi không phải ApiError', async () => {
    vi.spyOn(supportApi, 'closeSupportSession').mockRejectedValueOnce(new Error('Mạng bị đứt'));

    await act(async () => {
      root.render(
        <EndSessionDialog
          sessionId="session-1"
          onSuccess={vi.fn()}
          onSessionClosed={vi.fn()}
          onClose={vi.fn()}
        />,
      );
    });

    const confirmButton = container.querySelector(
      '.dialog-foot button.btn-danger',
    ) as HTMLButtonElement;

    await act(async () => {
      confirmButton.click();
    });

    // Hiện thông báo lỗi mặc định khi lỗi không xác định được mã lỗi.
    const alert = container.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Chưa kết thúc được hội thoại. Hãy thử lại.');
  });
});
