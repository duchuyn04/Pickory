// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import type { ActorData } from '../../../src/features/identity/hooks/use-actor';
import type * as DownloadFileModule from '../../../src/shared/download-file';
import { ApiError } from '../../../src/shared/api';
import * as downloadFileModule from '../../../src/shared/download-file';
import { ExportButton } from '../../../src/shared/export-button';
import {
  newTestQueryClient,
  renderWithProviders,
  settle,
} from '../features/commerce/customer/cart-test-utils';
import {
  buildExampleResponse,
  readServerErrorExample,
} from '../features/exports-printing/error-examples';

// Giả `downloadFile` để test điều khiển được lúc request bắt đầu và kết thúc. Giữ nguyên các export còn lại của module.
vi.mock('../../../src/shared/download-file', async (importOriginal) => {
  const original = await importOriginal<typeof DownloadFileModule>();
  return { ...original, downloadFile: vi.fn() };
});

const EXPORT_PATH = '/api/admin/exports/orders';
const REQUIRED_PERMISSIONS = ['orders.read', 'reports.export'];
const NETWORK_FAILURE_MESSAGE = 'Không tải được file. Vui lòng thử lại.';

let container: HTMLDivElement;
let root: Root;
let downloadFileMock: Mock<(path: string) => Promise<void>>;

const staffActor: ActorData = {
  anonymousId: 'anon-staff',
  userId: 'user-staff',
  kind: 'staff',
  roles: ['order_manager'],
  permissions: REQUIRED_PERMISSIONS,
  consent: { personalization: false, marketing: false },
};

// Điều khiển một lần tải: test quyết định lúc nào lần tải xong hoặc lỗi.
interface PendingDownload {
  resolve: () => void;
  reject: (reason: unknown) => void;
}

// Làm lần gọi `downloadFile` kế tiếp chờ đến khi test cho phép kết thúc.
function holdNextDownload(): PendingDownload {
  let resolve!: () => void;
  let reject!: (reason: unknown) => void;
  const pending = new Promise<void>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  downloadFileMock.mockReturnValueOnce(pending);
  return { resolve, reject };
}

interface RenderOptions {
  actor?: ActorData;
  requiredPermissions?: readonly string[];
  label?: string;
}

async function renderButton({ actor, requiredPermissions, label }: RenderOptions = {}) {
  const queryClient = newTestQueryClient();
  // Đặt actor vào cache để test không phụ thuộc một response `/api/me` riêng.
  if (actor) queryClient.setQueryData(['actor'], actor);
  await renderWithProviders(
    root,
    <ExportButton
      path={EXPORT_PATH}
      requiredPermissions={requiredPermissions ?? REQUIRED_PERMISSIONS}
      label={label}
    />,
    '/',
    queryClient,
  );
}

function findButton(): HTMLButtonElement | null {
  return container.querySelector<HTMLButtonElement>('button');
}

function requireButton(): HTMLButtonElement {
  const button = findButton();
  if (!button) throw new Error('Thiếu nút xuất file.');
  return button;
}

function findAlert(): HTMLElement | null {
  return container.querySelector<HTMLElement>('[role="alert"]');
}

beforeEach(() => {
  // Bật chế độ `act` của React. Thiếu cờ này React ghi cảnh báo vào `console.error`, và test kiểm cảnh báo sẽ sai.
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  downloadFileMock = vi.mocked(downloadFileModule.downloadFile);
  downloadFileMock.mockReset();
  downloadFileMock.mockResolvedValue(undefined);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('ExportButton: hiện hoặc ẩn theo quyền', () => {
  it('đủ cả hai quyền thì có nút `Xuất Excel`', async () => {
    await renderButton({ actor: staffActor });
    expect(requireButton().textContent).toBe('Xuất Excel');
  });

  it.each([['orders.read'], ['reports.export']])(
    'thiếu quyền `%s` thì không có nút',
    async (missingPermission) => {
      const actor = {
        ...staffActor,
        permissions: REQUIRED_PERMISSIONS.filter((permission) => permission !== missingPermission),
      };
      await renderButton({ actor });
      expect(findButton()).toBeNull();
    },
  );

  it('actor chưa nạp thì không có nút', async () => {
    // `/api/me` không bao giờ trả lời, nên actor ở trạng thái đang nạp.
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise<Response>(() => {})),
    );
    await renderButton();
    expect(findButton()).toBeNull();
  });

  it('actor có role `admin` thì có nút dù danh sách `permissions` rỗng', async () => {
    await renderButton({ actor: { ...staffActor, roles: ['admin'], permissions: [] } });
    expect(findButton()).not.toBeNull();
  });

  it('danh sách quyền rỗng thì nút luôn hiện', async () => {
    await renderButton({ actor: { ...staffActor, permissions: [] }, requiredPermissions: [] });
    expect(findButton()).not.toBeNull();
  });

  it('danh sách quyền rỗng thì nút hiện ngay cả khi actor chưa nạp', async () => {
    // `/api/me` không bao giờ trả lời, nên actor ở trạng thái đang nạp.
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise<Response>(() => {})),
    );
    await renderButton({ requiredPermissions: [] });
    expect(findButton()).not.toBeNull();
  });

  it('nhãn tùy chỉnh thay nhãn mặc định', async () => {
    await renderButton({ actor: staffActor, label: 'Xuất báo cáo' });
    expect(requireButton().textContent).toBe('Xuất báo cáo');
  });
});

describe('ExportButton: dạng nút và bàn phím', () => {
  it('là thẻ `<button type="button">` gốc nên Enter và Space tự phát `click`', async () => {
    await renderButton({ actor: staffActor });
    const button = requireButton();
    // Trình duyệt phát `click` khi nhấn Enter hoặc Space trên `<button>` gốc. jsdom không tự phát, nên test kiểm đúng thẻ rồi gọi `click()`.
    expect(button.tagName).toBe('BUTTON');
    expect(button.type).toBe('button');
    expect(button.className).toBe('btn btn-outline');
    expect(button.tabIndex).toBe(0);

    await act(async () => button.click());
    expect(downloadFileMock).toHaveBeenCalledTimes(1);
  });
});

describe('ExportButton: tải file', () => {
  it('bấm thì gọi `downloadFile` đúng một lần với đúng `path`', async () => {
    await renderButton({ actor: staffActor });
    await act(async () => requireButton().click());
    expect(downloadFileMock).toHaveBeenCalledTimes(1);
    expect(downloadFileMock).toHaveBeenCalledWith(EXPORT_PATH);
  });

  it('trong lúc tải nút bị khóa, đổi chữ và có `aria-busy`', async () => {
    await renderButton({ actor: staffActor });
    const download = holdNextDownload();

    await act(async () => requireButton().click());

    const busyButton = requireButton();
    expect(busyButton.disabled).toBe(true);
    expect(busyButton.textContent).toBe('Đang tạo file...');
    expect(busyButton.getAttribute('aria-busy')).toBe('true');

    await act(async () => download.resolve());
  });

  it('hai lần bấm liên tiếp trong cùng một `act` chỉ gọi `downloadFile` một lần', async () => {
    await renderButton({ actor: staffActor });
    const download = holdNextDownload();
    const button = requireButton();

    // Cả hai lần bấm chạy trước khi React kịp vẽ lại, nên `disabled` chưa có tác dụng.
    await act(async () => {
      button.click();
      button.click();
    });

    expect(downloadFileMock).toHaveBeenCalledTimes(1);
    await act(async () => download.resolve());
  });

  it('tải xong thì nút mở lại và chữ về nhãn', async () => {
    await renderButton({ actor: staffActor });
    const download = holdNextDownload();
    await act(async () => requireButton().click());

    await act(async () => download.resolve());

    const idleButton = requireButton();
    expect(idleButton.disabled).toBe(false);
    expect(idleButton.textContent).toBe('Xuất Excel');
    expect(idleButton.hasAttribute('aria-busy')).toBe(false);
    expect(findAlert()).toBeNull();
  });

  it('sau khi tải xong có thể bấm lần nữa', async () => {
    await renderButton({ actor: staffActor });
    await act(async () => requireButton().click());
    await act(async () => requireButton().click());
    expect(downloadFileMock).toHaveBeenCalledTimes(2);
  });
});

describe('ExportButton: lỗi', () => {
  it('lỗi 422 hiện câu của server trong `role="alert"` và mở lại nút', async () => {
    const serverError = readServerErrorExample('orders export too large');
    downloadFileMock.mockRejectedValueOnce(
      new ApiError(serverError.code, 422, serverError.message),
    );
    await renderButton({ actor: staffActor });

    await act(async () => requireButton().click());

    const alert = findAlert();
    expect(alert?.textContent).toContain(serverError.message);
    expect(requireButton().disabled).toBe(false);
    expect(requireButton().textContent).toBe('Xuất Excel');
  });

  it('lỗi 422 của `downloadFile` thật hiện câu của ví dụ `orders export too large` trong `role="alert"`', async () => {
    const serverError = readServerErrorExample('orders export too large');
    // Dùng `downloadFile` thật thay bản giả, để thử cả đường từ phản hồi của server tới nội dung thông báo.
    const actualModule = await vi.importActual<typeof DownloadFileModule>(
      '../../../src/shared/download-file',
    );
    downloadFileMock.mockImplementationOnce(actualModule.downloadFile);
    const fetchMock = vi.fn<typeof fetch>(async () =>
      buildExampleResponse('orders export too large'),
    );
    vi.stubGlobal('fetch', fetchMock);
    await renderButton({ actor: staffActor });

    await act(async () => requireButton().click());

    expect(fetchMock).toHaveBeenCalledWith(EXPORT_PATH, { credentials: 'include' });
    expect(findAlert()?.textContent).toContain(serverError.message);
    expect(requireButton().disabled).toBe(false);
  });

  it('lỗi mạng hiện `Không tải được file. Vui lòng thử lại.`', async () => {
    downloadFileMock.mockRejectedValueOnce(
      new ApiError('NETWORK_ERROR', 0, NETWORK_FAILURE_MESSAGE),
    );
    await renderButton({ actor: staffActor });

    await act(async () => requireButton().click());

    expect(findAlert()?.textContent).toContain(NETWORK_FAILURE_MESSAGE);
  });

  it('lỗi không phải `ApiError` hiện câu lỗi chung của việc tải file', async () => {
    downloadFileMock.mockRejectedValueOnce(new Error('lỗi bất ngờ nội bộ'));
    await renderButton({ actor: staffActor });

    await act(async () => requireButton().click());

    const alertText = findAlert()?.textContent ?? '';
    expect(alertText).toContain(NETWORK_FAILURE_MESSAGE);
    // Câu của lỗi lạ có thể lộ chi tiết kỹ thuật, nên không được hiện cho người dùng.
    expect(alertText).not.toContain('lỗi bất ngờ nội bộ');
  });

  it('lỗi hiện ngay dưới nút, cùng một khối cha', async () => {
    downloadFileMock.mockRejectedValueOnce(new ApiError('EXPORT_TOO_LARGE', 422, 'Quá lớn.'));
    await renderButton({ actor: staffActor });

    await act(async () => requireButton().click());

    const alert = findAlert();
    expect(alert).not.toBeNull();
    expect(alert?.previousElementSibling).toBe(requireButton());
  });

  it('bấm lại sau lỗi thì lỗi cũ biến mất khi lần tải mới bắt đầu', async () => {
    downloadFileMock.mockRejectedValueOnce(new ApiError('EXPORT_TOO_LARGE', 422, 'Quá lớn.'));
    await renderButton({ actor: staffActor });
    await act(async () => requireButton().click());
    expect(findAlert()).not.toBeNull();

    const download = holdNextDownload();
    await act(async () => requireButton().click());

    // Lần tải mới chưa kết thúc nhưng lỗi cũ đã phải biến mất.
    expect(findAlert()).toBeNull();
    await act(async () => download.resolve());
    expect(findAlert()).toBeNull();
  });
});

describe('ExportButton: gỡ khỏi cây khi đang tải', () => {
  it('gỡ component khi đang tải rồi request lỗi thì không ném lỗi và không ghi `console.error`', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    await renderButton({ actor: staffActor });
    const download = holdNextDownload();
    await act(async () => requireButton().click());

    // Đổi trang: nút bị gỡ khi request vẫn đang chạy.
    await act(async () => root.render(<div />));
    await act(async () =>
      download.reject(new ApiError('NETWORK_ERROR', 0, NETWORK_FAILURE_MESSAGE)),
    );
    await settle();

    expect(consoleError).not.toHaveBeenCalled();
    expect(container.querySelector('[role="alert"]')).toBeNull();
  });
});
