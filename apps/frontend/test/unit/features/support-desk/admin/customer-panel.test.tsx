// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import type { UseQueryResult } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CustomerPanel } from '../../../../../src/features/support-desk/admin/customer-panel';
import type { SessionCustomer } from '../../../../../src/features/support-desk/admin/support-desk-schemas';
import type { OpenSupportSession } from '../../../../../src/features/support-desk/admin/support-desk-session';

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

// Giả lập kết quả trả về của useQuery để kiểm tra từng trạng thái hiển thị.
function createQueryResult(
  overrides: Partial<UseQueryResult<SessionCustomer>> = {},
): UseQueryResult<SessionCustomer> {
  return {
    data: undefined,
    error: null,
    isError: false,
    isLoading: false,
    isFetching: false,
    isSuccess: true,
    status: 'success',
    refetch: vi.fn(),
    ...overrides,
  } as unknown as UseQueryResult<SessionCustomer>;
}

const mockSession: OpenSupportSession = {
  sessionId: '5e550000-0000-4000-8000-000000000001',
  conversationId: 'c0a80000-0000-4000-8000-000000000001',
  isHeld: true,
  isClosed: false,
};

describe('CustomerPanel', () => {
  it('hiển thị câu hướng dẫn khi chưa chọn phiên nào', async () => {
    await act(async () => {
      root.render(
        <MemoryRouter>
          <CustomerPanel session={null} customerQuery={createQueryResult()} />
        </MemoryRouter>,
      );
    });

    // Xác nhận tiêu đề và dòng hướng dẫn khi chưa có phiên mở.
    const heading = container.querySelector('#support-customer-title');
    expect(heading?.textContent).toBe('Thông tin khách');
    expect(container.textContent).toContain('Tên, email và đơn gần đây của khách sẽ hiện ở đây.');
  });

  it('hiển thị thông báo đang tải khi chưa nhận được dữ liệu', async () => {
    const query = createQueryResult({ isLoading: true, data: undefined });
    await act(async () => {
      root.render(
        <MemoryRouter>
          <CustomerPanel session={mockSession} customerQuery={query} />
        </MemoryRouter>,
      );
    });

    // Xác nhận trạng thái đang tải dữ liệu khách.
    expect(container.textContent).toContain('Đang tải thông tin khách...');
  });

  it('hiển thị thông báo lỗi và gọi lại refetch khi bấm nút Thử lại', async () => {
    const refetchMock = vi.fn();
    const query = createQueryResult({ isError: true, data: undefined, refetch: refetchMock });

    await act(async () => {
      root.render(
        <MemoryRouter>
          <CustomerPanel session={mockSession} customerQuery={query} />
        </MemoryRouter>,
      );
    });

    // Xác nhận thông báo lỗi xuất hiện trên màn hình.
    expect(container.textContent).toContain('Không tải được thông tin khách.');

    const retryButton = container.querySelector('.support-queue-alert button') as HTMLButtonElement;
    expect(retryButton).not.toBeNull();

    await act(async () => {
      retryButton.click();
    });

    // Xác nhận hàm refetch được gọi khi nhân viên bấm thử lại.
    expect(refetchMock).toHaveBeenCalledTimes(1);
  });

  it('hiển thị thông tin khách đã đăng nhập và liên kết đơn hàng', async () => {
    const customerData: SessionCustomer = {
      customer: {
        isGuest: false,
        name: 'Nguyễn Minh Anh',
        email: 'minhanh@example.com',
      },
      orders: [
        {
          code: 'PK123',
          status: 'shipping',
          total: 459000,
          itemCount: 2,
          createdAt: '2026-11-01T09:12:00Z',
        },
      ],
    };
    const query = createQueryResult({ data: customerData });

    await act(async () => {
      root.render(
        <MemoryRouter>
          <CustomerPanel session={mockSession} customerQuery={query} />
        </MemoryRouter>,
      );
    });

    // Kiểm tra tên và email của khách đã đăng nhập.
    expect(container.textContent).toContain('Nguyễn Minh Anh');
    expect(container.textContent).toContain('minhanh@example.com');
    expect(container.textContent).not.toContain('Khách vãng lai');

    // Kiểm tra thông tin đơn hàng và link chuyển trang.
    const orderLink = container.querySelector('a[href="/admin/orders/PK123"]');
    expect(orderLink).not.toBeNull();
    expect(orderLink?.textContent).toBe('PK123');
    expect(container.textContent).toContain('Đang giao hàng');
    expect(container.textContent).toContain('459.000 đ');
    expect(container.textContent).toContain('2 sản phẩm');
  });

  it('hiển thị Chưa có thông tin và nhãn Khách vãng lai khi tên và email bằng null', async () => {
    const guestData: SessionCustomer = {
      customer: {
        isGuest: true,
        name: null,
        email: null,
      },
      orders: [],
    };
    const query = createQueryResult({ data: guestData });

    await act(async () => {
      root.render(
        <MemoryRouter>
          <CustomerPanel session={mockSession} customerQuery={query} />
        </MemoryRouter>,
      );
    });

    // Xác nhận hiển thị nhãn khách vãng lai và các giá trị chưa có thông tin.
    expect(container.textContent).toContain('Khách vãng lai');
    const fields = container.querySelectorAll('.support-customer-value');
    expect(fields).toHaveLength(2);
    expect(fields[0]?.textContent).toBe('Chưa có thông tin');
    expect(fields[1]?.textContent).toBe('Chưa có thông tin');
    expect(container.textContent).toContain('Khách chưa có đơn hàng.');
  });

  it('nút điều khiển ngăn thu gọn thay đổi aria-expanded và đóng khi đổi phiên qua key', async () => {
    const query = createQueryResult();

    await act(async () => {
      root.render(
        <MemoryRouter>
          <CustomerPanel
            key={`customer-${mockSession.sessionId}`}
            session={mockSession}
            customerQuery={query}
          />
        </MemoryRouter>,
      );
    });

    const toggleButton = container.querySelector('.support-customer-toggle') as HTMLButtonElement;
    expect(toggleButton).not.toBeNull();
    // Mặc định ngăn đóng nên aria-expanded bằng false.
    expect(toggleButton.getAttribute('aria-expanded')).toBe('false');

    await act(async () => {
      toggleButton.click();
    });
    // Bấm nút mở ngăn nên aria-expanded chuyển thành true.
    expect(toggleButton.getAttribute('aria-expanded')).toBe('true');

    // Đổi sang phiên khác thì component dựng lại qua key và tự động quay về trạng thái đóng.
    const anotherSession: OpenSupportSession = {
      ...mockSession,
      sessionId: '5e550000-0000-4000-8000-000000000002',
    };
    await act(async () => {
      root.render(
        <MemoryRouter>
          <CustomerPanel
            key={`customer-${anotherSession.sessionId}`}
            session={anotherSession}
            customerQuery={query}
          />
        </MemoryRouter>,
      );
    });

    const newToggleButton = container.querySelector(
      '.support-customer-toggle',
    ) as HTMLButtonElement;
    expect(newToggleButton.getAttribute('aria-expanded')).toBe('false');
  });

  it('ngăn thu gọn có aria-controls trỏ tới vùng nội dung có thuộc tính hidden khi đóng', async () => {
    const customerData: SessionCustomer = {
      customer: {
        isGuest: false,
        name: 'Nguyễn Minh Anh',
        email: 'minhanh@example.com',
      },
      orders: [],
    };
    const query = createQueryResult({ data: customerData });

    await act(async () => {
      root.render(
        <MemoryRouter>
          <CustomerPanel session={mockSession} customerQuery={query} />
        </MemoryRouter>,
      );
    });

    const toggleButton = container.querySelector('.support-customer-toggle') as HTMLButtonElement;
    const contentId = toggleButton.getAttribute('aria-controls');
    // Nút phải có aria-controls trỏ tới đúng id của vùng nội dung.
    expect(contentId).toBeTruthy();

    const contentRegion = container.querySelector(`#${contentId}`);
    expect(contentRegion).not.toBeNull();
    // Khi ngăn đóng, phần tử mang thuộc tính hidden nên không hiển thị trong luồng vẽ.
    expect(contentRegion?.hasAttribute('hidden')).toBe(true);

    await act(async () => {
      toggleButton.click();
    });

    // Khi ngăn mở, thuộc tính hidden bị gỡ bỏ và nội dung hiển thị cho người dùng.
    expect(contentRegion?.hasAttribute('hidden')).toBe(false);
    expect(contentRegion?.textContent).toContain('Nguyễn Minh Anh');
  });
});
