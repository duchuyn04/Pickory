// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider, type UseQueryResult } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../../../src/shared/api';
import { waitFor } from '../../identity/admin/admin-list-test-kit';
import { TransferSessionDialog } from '../../../../../src/features/support-desk/admin/transfer-session-dialog';
import * as supportApi from '../../../../../src/features/support-desk/admin/support-desk-api';
import * as actorHook from '../../../../../src/features/identity/hooks/use-actor';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';
import type { StaffPresence } from '../../../../../src/features/support-desk/admin/support-desk-schemas';

let container: HTMLDivElement;
let root: Root;
let queryClient: QueryClient;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  });

  // Giả lập tài khoản hiện tại đang đăng nhập là staff-0001.
  vi.spyOn(actorHook, 'useActor').mockReturnValue({
    data: {
      userId: 'staff-0001',
      kind: 'staff',
      anonymousId: 'anon-1',
      roles: ['staff'],
      permissions: ['conversations:reply'],
      consent: { personalization: false, marketing: false },
    },
  } as unknown as UseQueryResult<ActorData>);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  queryClient.clear();
  vi.unstubAllGlobals();
});

describe('TransferSessionDialog', () => {
  it('chỉ liệt kê nhân viên đang sẵn sàng và loại chính người dùng', async () => {
    vi.spyOn(supportApi, 'fetchAvailableStaff').mockResolvedValueOnce([
      { staffId: 'staff-0001', status: 'available', lastSeenAt: '2026-11-09T08:00:00Z' },
      {
        staffId: 'staff-0002',
        status: 'available',
        lastSeenAt: '2026-11-09T08:00:00Z',
        displayName: 'Minh',
      },
      {
        staffId: 'staff-0003',
        status: 'away',
        lastSeenAt: '2026-11-09T08:00:00Z',
        displayName: 'Hùng',
      },
    ]);

    await act(async () => {
      root.render(
        <QueryClientProvider client={queryClient}>
          <TransferSessionDialog
            sessionId="session-1"
            onSuccess={vi.fn()}
            onSessionClosed={vi.fn()}
            onLostHold={vi.fn()}
            onClose={vi.fn()}
          />
        </QueryClientProvider>,
      );
    });

    await waitFor(
      () => container.textContent?.includes('Minh') ?? false,
      'danh sách nhân viên hiện ra',
    );

    // Chỉ nhân viên 0002 hiển thị trong danh sách chuyển.
    expect(container.textContent).toContain('Minh');
    expect(container.textContent).not.toContain('Hùng');
    expect(container.textContent).not.toContain('Nhân viên 0001');
    const transferBtn = container.querySelector(
      '.dialog-foot button.btn-primary',
    ) as HTMLButtonElement;
    // Mặc định chưa chọn ai nên nút Chuyển bị tắt.
    expect(transferBtn.disabled).toBe(true);
  });

  it('gửi request chuyển phiên khi chọn người nhận và nhập ghi chú', async () => {
    vi.spyOn(supportApi, 'fetchAvailableStaff').mockResolvedValueOnce([
      {
        staffId: 'staff-0002',
        status: 'available',
        lastSeenAt: '2026-11-09T08:00:00Z',
        displayName: 'Minh',
      },
    ]);
    const transferSpy = vi.spyOn(supportApi, 'transferSupportSession').mockResolvedValueOnce({
      sessionId: 'session-1',
      staffId: 'staff-0002',
      claimedAt: '2026-11-09T08:30:00Z',
    });
    const onSuccess = vi.fn();

    await act(async () => {
      root.render(
        <QueryClientProvider client={queryClient}>
          <TransferSessionDialog
            sessionId="session-1"
            onSuccess={onSuccess}
            onSessionClosed={vi.fn()}
            onLostHold={vi.fn()}
            onClose={vi.fn()}
          />
        </QueryClientProvider>,
      );
    });

    await waitFor(() => container.querySelector('input[type="radio"]') !== null, 'radio hiện ra');

    const radioInput = container.querySelector('input[type="radio"]') as HTMLInputElement;
    const noteTextarea = container.querySelector('textarea') as HTMLTextAreaElement;

    const textareaSetter = Object.getOwnPropertyDescriptor(
      HTMLTextAreaElement.prototype,
      'value',
    )!.set!;
    await act(async () => {
      radioInput.click();
      textareaSetter.call(noteTextarea, '  Khách cần kiểm tra bảo hành đơn PK123  ');
      noteTextarea.dispatchEvent(new Event('input', { bubbles: true }));
      noteTextarea.dispatchEvent(new Event('change', { bubbles: true }));
    });

    const transferBtn = container.querySelector(
      '.dialog-foot button.btn-primary',
    ) as HTMLButtonElement;
    expect(transferBtn.disabled).toBe(false);

    await act(async () => {
      transferBtn.click();
    });

    // Xác nhận API nhận ghi chú đã cắt khoảng trắng hai đầu.
    expect(transferSpy).toHaveBeenCalledWith(
      'session-1',
      'staff-0002',
      'Khách cần kiểm tra bảo hành đơn PK123',
    );
    expect(onSuccess).toHaveBeenCalledWith('Minh');
  });

  it('gửi null khi ghi chú rỗng hoặc chỉ có khoảng trắng', async () => {
    vi.spyOn(supportApi, 'fetchAvailableStaff').mockResolvedValueOnce([
      {
        staffId: 'staff-0002',
        status: 'available',
        lastSeenAt: '2026-11-09T08:00:00Z',
        displayName: 'Minh',
      },
    ]);
    const transferSpy = vi.spyOn(supportApi, 'transferSupportSession').mockResolvedValueOnce({
      sessionId: 'session-1',
      staffId: 'staff-0002',
      claimedAt: '2026-11-09T08:30:00Z',
    });

    await act(async () => {
      root.render(
        <QueryClientProvider client={queryClient}>
          <TransferSessionDialog
            sessionId="session-1"
            onSuccess={vi.fn()}
            onSessionClosed={vi.fn()}
            onLostHold={vi.fn()}
            onClose={vi.fn()}
          />
        </QueryClientProvider>,
      );
    });

    await waitFor(() => container.querySelector('input[type="radio"]') !== null, 'radio hiện ra');

    const radioInput = container.querySelector('input[type="radio"]') as HTMLInputElement;
    const noteTextarea = container.querySelector('textarea') as HTMLTextAreaElement;

    await act(async () => {
      radioInput.click();
      noteTextarea.value = '    ';
      noteTextarea.dispatchEvent(new Event('input', { bubbles: true }));
      noteTextarea.dispatchEvent(new Event('change', { bubbles: true }));
    });

    const transferBtn = container.querySelector(
      '.dialog-foot button.btn-primary',
    ) as HTMLButtonElement;

    await act(async () => {
      transferBtn.click();
    });

    // Khi ghi chú rỗng, hàm chuyển chuỗi thành null theo hợp đồng API.
    expect(transferSpy).toHaveBeenCalledWith('session-1', 'staff-0002', null);
  });

  it('xử lý lỗi 409 SUPPORT_STAFF_UNAVAILABLE bằng cách đọc lại danh sách và hiện thông báo', async () => {
    const fetchPresenceSpy = vi
      .spyOn(supportApi, 'fetchAvailableStaff')
      .mockResolvedValueOnce([
        {
          staffId: 'staff-0002',
          status: 'available',
          lastSeenAt: '2026-11-09T08:00:00Z',
          displayName: 'Minh',
        },
      ])
      .mockResolvedValueOnce([
        {
          staffId: 'staff-0002',
          status: 'away',
          lastSeenAt: '2026-11-09T08:00:00Z',
          displayName: 'Minh',
        },
      ]);

    vi.spyOn(supportApi, 'transferSupportSession').mockRejectedValueOnce(
      new ApiError('SUPPORT_STAFF_UNAVAILABLE', 409, 'Người nhận không ở trạng thái sẵn sàng.'),
    );

    await act(async () => {
      root.render(
        <QueryClientProvider client={queryClient}>
          <TransferSessionDialog
            sessionId="session-1"
            onSuccess={vi.fn()}
            onSessionClosed={vi.fn()}
            onLostHold={vi.fn()}
            onClose={vi.fn()}
          />
        </QueryClientProvider>,
      );
    });

    await waitFor(() => container.querySelector('input[type="radio"]') !== null, 'radio hiện ra');

    const radioInput = container.querySelector('input[type="radio"]') as HTMLInputElement;
    await act(async () => {
      radioInput.click();
    });

    const transferBtn = container.querySelector(
      '.dialog-foot button.btn-primary',
    ) as HTMLButtonElement;
    await act(async () => {
      transferBtn.click();
    });

    await waitFor(
      () => container.querySelector('[role="alert"]') !== null,
      'thông báo lỗi hiện ra trong role=alert',
    );

    // Xác nhận presence được gọi lại để đồng bộ trạng thái mới.
    expect(fetchPresenceSpy).toHaveBeenCalledTimes(2);
    const alert = container.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Người nhận không ở trạng thái sẵn sàng.');

    // Người nhận đã away bị loại khỏi danh sách và nút Chuyển bị vô hiệu hóa lại.
    expect(container.querySelectorAll('input[type="radio"]')).toHaveLength(0);
    expect(transferBtn.disabled).toBe(true);
  });

  it('chặn gửi lặp khi người dùng bấm đúp nút Chuyển', async () => {
    vi.spyOn(supportApi, 'fetchAvailableStaff').mockResolvedValueOnce([
      {
        staffId: 'staff-0002',
        status: 'available',
        lastSeenAt: '2026-11-09T08:00:00Z',
        displayName: 'Minh',
      },
    ]);

    let resolveTransfer: () => void = () => {};
    const transferPromise = new Promise<{ sessionId: string; staffId: string; claimedAt: string }>(
      (resolve) => {
        resolveTransfer = () =>
          resolve({
            sessionId: 'session-1',
            staffId: 'staff-0002',
            claimedAt: '2026-11-09T08:30:00Z',
          });
      },
    );
    const transferSpy = vi
      .spyOn(supportApi, 'transferSupportSession')
      .mockReturnValueOnce(transferPromise);

    await act(async () => {
      root.render(
        <QueryClientProvider client={queryClient}>
          <TransferSessionDialog
            sessionId="session-1"
            onSuccess={vi.fn()}
            onSessionClosed={vi.fn()}
            onLostHold={vi.fn()}
            onClose={vi.fn()}
          />
        </QueryClientProvider>,
      );
    });

    await waitFor(() => container.querySelector('input[type="radio"]') !== null, 'radio hiện ra');
    const radioInput = container.querySelector('input[type="radio"]') as HTMLInputElement;
    await act(async () => {
      radioInput.click();
    });

    const transferBtn = container.querySelector(
      '.dialog-foot button.btn-primary',
    ) as HTMLButtonElement;

    // Bấm liên tiếp hai lần trong cùng một lượt khi yêu cầu đang thực thi.
    await act(async () => {
      transferBtn.click();
      transferBtn.click();
    });

    // Chỉ gửi một request duy nhất lên máy chủ.
    expect(transferSpy).toHaveBeenCalledTimes(1);

    await act(async () => {
      resolveTransfer();
    });
  });

  it('gọi onSessionClosed và onClose khi gặp lỗi 409 SUPPORT_SESSION_CLOSED', async () => {
    vi.spyOn(supportApi, 'fetchAvailableStaff').mockResolvedValueOnce([
      {
        staffId: 'staff-0002',
        status: 'available',
        lastSeenAt: '2026-11-09T08:00:00Z',
        displayName: 'Minh',
      },
    ]);
    vi.spyOn(supportApi, 'transferSupportSession').mockRejectedValueOnce(
      new ApiError('SUPPORT_SESSION_CLOSED', 409, 'Phiên đã kết thúc.'),
    );
    const onSessionClosed = vi.fn();
    const onClose = vi.fn();

    await act(async () => {
      root.render(
        <QueryClientProvider client={queryClient}>
          <TransferSessionDialog
            sessionId="session-1"
            onSuccess={vi.fn()}
            onSessionClosed={onSessionClosed}
            onLostHold={vi.fn()}
            onClose={onClose}
          />
        </QueryClientProvider>,
      );
    });

    await waitFor(() => container.querySelector('input[type="radio"]') !== null, 'radio hiện ra');
    const radioInput = container.querySelector('input[type="radio"]') as HTMLInputElement;
    await act(async () => {
      radioInput.click();
    });

    const transferBtn = container.querySelector(
      '.dialog-foot button.btn-primary',
    ) as HTMLButtonElement;
    await act(async () => {
      transferBtn.click();
    });

    // Đóng hộp thoại và đồng bộ trạng thái phiên đã kết thúc.
    expect(onSessionClosed).toHaveBeenCalledTimes(1);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('gọi onLostHold và onClose khi gặp lỗi 403 SUPPORT_SESSION_NOT_ASSIGNED', async () => {
    vi.spyOn(supportApi, 'fetchAvailableStaff').mockResolvedValueOnce([
      {
        staffId: 'staff-0002',
        status: 'available',
        lastSeenAt: '2026-11-09T08:00:00Z',
        displayName: 'Minh',
      },
    ]);
    vi.spyOn(supportApi, 'transferSupportSession').mockRejectedValueOnce(
      new ApiError('SUPPORT_SESSION_NOT_ASSIGNED', 403, 'Bạn chưa nhận hội thoại này.'),
    );
    const onLostHold = vi.fn();
    const onClose = vi.fn();

    await act(async () => {
      root.render(
        <QueryClientProvider client={queryClient}>
          <TransferSessionDialog
            sessionId="session-1"
            onSuccess={vi.fn()}
            onSessionClosed={vi.fn()}
            onLostHold={onLostHold}
            onClose={onClose}
          />
        </QueryClientProvider>,
      );
    });

    await waitFor(() => container.querySelector('input[type="radio"]') !== null, 'radio hiện ra');
    const radioInput = container.querySelector('input[type="radio"]') as HTMLInputElement;
    await act(async () => {
      radioInput.click();
    });

    const transferBtn = container.querySelector(
      '.dialog-foot button.btn-primary',
    ) as HTMLButtonElement;
    await act(async () => {
      transferBtn.click();
    });

    // Đóng hộp thoại và đặt lại quyền giữ phiên ở màn hình chính.
    expect(onLostHold).toHaveBeenCalledTimes(1);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('hiển thị ErrorAlert khi API trả lỗi thông thường và giữ hộp thoại mở', async () => {
    vi.spyOn(supportApi, 'fetchAvailableStaff').mockResolvedValueOnce([
      {
        staffId: 'staff-0002',
        status: 'available',
        lastSeenAt: '2026-11-09T08:00:00Z',
        displayName: 'Minh',
      },
    ]);
    vi.spyOn(supportApi, 'transferSupportSession').mockRejectedValueOnce(
      new ApiError('VALIDATION_ERROR', 400, 'Ghi chú quá dài.'),
    );
    const onSessionClosed = vi.fn();
    const onLostHold = vi.fn();
    const onClose = vi.fn();

    await act(async () => {
      root.render(
        <QueryClientProvider client={queryClient}>
          <TransferSessionDialog
            sessionId="session-1"
            onSuccess={vi.fn()}
            onSessionClosed={onSessionClosed}
            onLostHold={onLostHold}
            onClose={onClose}
          />
        </QueryClientProvider>,
      );
    });

    await waitFor(() => container.querySelector('input[type="radio"]') !== null, 'radio hiện ra');
    const radioInput = container.querySelector('input[type="radio"]') as HTMLInputElement;
    await act(async () => {
      radioInput.click();
    });

    const transferBtn = container.querySelector(
      '.dialog-foot button.btn-primary',
    ) as HTMLButtonElement;
    await act(async () => {
      transferBtn.click();
    });

    // Hiện thông báo trong role=alert và giữ nguyên hộp thoại.
    const alert = container.querySelector('[role="alert"]');
    expect(alert).not.toBeNull();
    expect(alert?.textContent).toContain('Ghi chú quá dài.');
    expect(onSessionClosed).not.toHaveBeenCalled();
    expect(onLostHold).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
  });

  it('chặn bấm Chuyển lần nữa khi refetch chưa xong sau lỗi SUPPORT_STAFF_UNAVAILABLE', async () => {
    let resolveRefetch: (val: StaffPresence[]) => void = () => {};
    const refetchPromise = new Promise<StaffPresence[]>((resolve) => {
      resolveRefetch = resolve;
    });

    // Lần 1 trả danh sách có staff-0002, lần 2 trả promise treo chưa hoàn tất.
    const fetchPresenceSpy = vi
      .spyOn(supportApi, 'fetchAvailableStaff')
      .mockResolvedValueOnce([
        {
          staffId: 'staff-0002',
          status: 'available',
          lastSeenAt: '2026-11-09T08:00:00Z',
          displayName: 'Minh',
        },
      ])
      .mockReturnValueOnce(refetchPromise);
    const transferSpy = vi
      .spyOn(supportApi, 'transferSupportSession')
      .mockRejectedValueOnce(
        new ApiError('SUPPORT_STAFF_UNAVAILABLE', 409, 'Người nhận không ở trạng thái sẵn sàng.'),
      );

    await act(async () => {
      root.render(
        <QueryClientProvider client={queryClient}>
          <TransferSessionDialog
            sessionId="session-1"
            onSuccess={vi.fn()}
            onSessionClosed={vi.fn()}
            onLostHold={vi.fn()}
            onClose={vi.fn()}
          />
        </QueryClientProvider>,
      );
    });

    await waitFor(() => container.querySelector('input[type="radio"]') !== null, 'radio hiện ra');
    const radioInput = container.querySelector('input[type="radio"]') as HTMLInputElement;
    await act(async () => {
      radioInput.click();
    });

    const transferBtn = container.querySelector(
      '.dialog-foot button.btn-primary',
    ) as HTMLButtonElement;

    // Bấm Chuyển lần đầu: transfer trả lỗi 409 và kích hoạt refetch dữ liệu.
    await act(async () => {
      transferBtn.click();
    });

    // Khẳng định API transfer đã gọi 1 lần và refetch đã bắt đầu.
    expect(transferSpy).toHaveBeenCalledTimes(1);
    expect(fetchPresenceSpy).toHaveBeenCalledTimes(2);

    // Trong lúc refetch chưa xong, nút Chuyển vẫn bị vô hiệu hóa để bảo vệ luồng.
    expect(transferBtn.disabled).toBe(true);

    // Thử bấm Chuyển lần nữa khi quá trình nạp lại chưa hoàn tất.
    await act(async () => {
      transferBtn.click();
    });

    // Khẳng định API không bị gọi thêm lần nào nữa.
    expect(transferSpy).toHaveBeenCalledTimes(1);

    // Cho refetch hoàn tất để kết thúc lượt kiểm thử.
    await act(async () => {
      resolveRefetch([]);
    });
  });

  it('hiển thị thông báo và tắt nút Chuyển khi người nhận chuyển sang away trước khi bấm Chuyển', async () => {
    vi.spyOn(supportApi, 'fetchAvailableStaff').mockResolvedValueOnce([
      {
        staffId: 'staff-0002',
        status: 'available',
        lastSeenAt: '2026-11-09T08:00:00Z',
        displayName: 'Minh',
      },
    ]);
    const transferSpy = vi.spyOn(supportApi, 'transferSupportSession');

    await act(async () => {
      root.render(
        <QueryClientProvider client={queryClient}>
          <TransferSessionDialog
            sessionId="session-1"
            onSuccess={vi.fn()}
            onSessionClosed={vi.fn()}
            onLostHold={vi.fn()}
            onClose={vi.fn()}
          />
        </QueryClientProvider>,
      );
    });

    await waitFor(() => container.querySelector('input[type="radio"]') !== null, 'radio hiện ra');
    const radioInput = container.querySelector('input[type="radio"]') as HTMLInputElement;
    await act(async () => {
      radioInput.click();
    });

    const transferBtn = container.querySelector(
      '.dialog-foot button.btn-primary',
    ) as HTMLButtonElement;
    expect(transferBtn.disabled).toBe(false);

    // Đồng nghiệp chuyển away qua sự kiện SSE, cache presence cập nhật danh sách rỗng.
    await act(async () => {
      queryClient.setQueryData(supportApi.PRESENCE_QUERY_KEY, []);
    });
    await waitFor(() => container.querySelector('input[type="radio"]') === null, 'radio biến mất');

    // Bấm nút Chuyển khi người nhận không còn trong danh sách sẵn sàng.
    await act(async () => {
      transferBtn.click();
    });

    // Khẳng định API transfer không được gọi.
    expect(transferSpy).not.toHaveBeenCalled();

    // Thông báo lỗi xuất hiện trong role=alert.
    const alert = container.querySelector('[role="alert"]');
    expect(alert).not.toBeNull();
    expect(alert?.textContent).toContain(
      'Đồng nghiệp này không còn sẵn sàng. Hãy chọn người khác.',
    );

    // Nút Chuyển bị tắt do lựa chọn đã bị hủy bỏ.
    expect(transferBtn.disabled).toBe(true);

    // Khi có đồng nghiệp mới sẵn sàng và người dùng chọn người đó, thông báo lỗi phải được xóa.
    await act(async () => {
      queryClient.setQueryData(supportApi.PRESENCE_QUERY_KEY, [
        {
          staffId: 'staff-0003',
          status: 'available',
          lastSeenAt: '2026-11-09T08:00:00Z',
          displayName: 'Hương',
        },
      ]);
    });
    await waitFor(
      () => container.querySelector('input[type="radio"]') !== null,
      'radio mới hiện ra',
    );
    const newRadio = container.querySelector('input[type="radio"]') as HTMLInputElement;
    await act(async () => {
      newRadio.click();
    });

    // Thông báo lỗi biến mất và nút Chuyển được bật lại.
    expect(container.querySelector('[role="alert"]')).toBeNull();
    expect(transferBtn.disabled).toBe(false);
  });
});
