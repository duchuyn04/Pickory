// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../../../src/shared/api';
import { ProductStatusPanel } from '../../../../../src/features/catalog/admin/product-status-panel';

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

interface PanelOptions {
  status?: string;
  canWrite?: boolean;
  hasUnsavedChanges?: boolean;
  onChangeStatus?: (target: string) => Promise<void>;
  onDelete?: () => Promise<void>;
}

async function renderPanel(options: PanelOptions = {}) {
  const onChangeStatus = options.onChangeStatus ?? vi.fn(async () => {});
  const onDelete = options.onDelete ?? vi.fn(async () => {});
  await act(async () =>
    root.render(
      <ProductStatusPanel
        productName="MacBook Air M3"
        status={options.status ?? 'draft'}
        canWrite={options.canWrite ?? true}
        hasUnsavedChanges={options.hasUnsavedChanges ?? false}
        onChangeStatus={onChangeStatus}
        onDelete={onDelete}
      />,
    ),
  );
  return { onChangeStatus, onDelete };
}

function buttonByName(name: string): HTMLButtonElement | undefined {
  return [...document.querySelectorAll('button')].find((button) => button.textContent === name);
}

function statusButtonNames(): string[] {
  return [...container.querySelectorAll<HTMLButtonElement>('.status-actions button')].map(
    (button) => button.textContent ?? '',
  );
}

async function click(button: HTMLButtonElement | undefined) {
  await act(async () => button!.click());
}

async function pressEscape() {
  await act(async () => {
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
  });
}

const notPublishable = new ApiError(
  'PRODUCT_NOT_PUBLISHABLE',
  422,
  'Sản phẩm chưa đủ điều kiện để đăng bán.',
  // Dạng thật của backend: mảng chuỗi lý do.
  [
    'Sản phẩm phải có ít nhất 1 ảnh để đăng bán.',
    'SKU MAC-01: thiếu cân nặng hoặc kích thước đóng gói.',
  ] as unknown as ApiError['details'],
);

describe('ProductStatusPanel: nút theo trạng thái', () => {
  it.each([
    ['draft', 'Bản nháp', ['Đăng bán']],
    ['active', 'Đang bán', ['Ẩn sản phẩm', 'Ngừng kinh doanh']],
    ['hidden', 'Đã ẩn', ['Bán lại', 'Ngừng kinh doanh']],
    ['discontinued', 'Ngừng bán', ['Bán lại', 'Ẩn sản phẩm']],
  ])('từ %s hiện nhãn "%s" và đúng các nút', async (status, label, buttons) => {
    await renderPanel({ status });
    expect(container.querySelector('.badge')?.textContent).toBe(label);
    expect(statusButtonNames()).toEqual(buttons);
  });

  it('có nút Xóa sản phẩm kiểu nguy hiểm, nằm ngoài nhóm nút trạng thái', async () => {
    await renderPanel();
    const deleteButton = buttonByName('Xóa sản phẩm')!;
    expect(deleteButton.className).toContain('btn-danger');
    expect(deleteButton.closest('.status-actions')).toBeNull();
  });

  it('mọi nút trong khối là type="button" để không gửi form sản phẩm', async () => {
    await renderPanel({ status: 'active' });
    for (const button of container.querySelectorAll('button')) {
      expect(button.type).toBe('button');
    }
  });
});

describe('ProductStatusPanel: đổi trạng thái', () => {
  it('bấm nút gọi onChangeStatus với trạng thái đích và hiện thông báo role="status"', async () => {
    const { onChangeStatus } = await renderPanel({ status: 'draft' });
    await click(buttonByName('Đăng bán'));
    expect(onChangeStatus).toHaveBeenCalledExactlyOnceWith('active');
    expect(container.querySelector('[role="status"]')?.textContent).toBe(
      'Đã đổi trạng thái thành "Đang bán".',
    );
    expect(container.querySelector('[role="alert"]')).toBeNull();
  });

  it('lỗi 422 hiện thông báo của API và từng lý do trong danh sách, nhãn không đổi', async () => {
    await renderPanel({
      status: 'draft',
      onChangeStatus: vi.fn(async () => {
        throw notPublishable;
      }),
    });
    await click(buttonByName('Đăng bán'));
    const alert = container.querySelector('[role="alert"]')!;
    expect(alert.textContent).toContain('Sản phẩm chưa đủ điều kiện để đăng bán.');
    const reasons = [...alert.querySelectorAll('li')].map((item) => item.textContent);
    expect(reasons).toEqual([
      'Sản phẩm phải có ít nhất 1 ảnh để đăng bán.',
      'SKU MAC-01: thiếu cân nặng hoặc kích thước đóng gói.',
    ]);
    expect(alert.classList.contains('alert-list')).toBe(true);
    expect(alert.querySelector('svg[aria-hidden="true"]')).not.toBeNull();
    expect(alert.querySelector('.alert-list-title')?.textContent).toBe(
      'Sản phẩm chưa đủ điều kiện để đăng bán.',
    );
    expect(container.querySelector('.badge')?.textContent).toBe('Bản nháp');
    expect(container.querySelector('[role="status"]')?.textContent ?? '').toBe('');
  });

  it('lần đổi thành công kế tiếp xóa lỗi 422 cũ', async () => {
    const onChangeStatus = vi
      .fn<(target: string) => Promise<void>>()
      .mockRejectedValueOnce(notPublishable)
      .mockResolvedValueOnce(undefined);
    await renderPanel({ onChangeStatus });
    await click(buttonByName('Đăng bán'));
    expect(container.querySelector('[role="alert"]')).not.toBeNull();
    await click(buttonByName('Đăng bán'));
    expect(container.querySelector('[role="alert"]')).toBeNull();
  });

  it('lỗi 409 INVALID_STATUS_TRANSITION hiện thông báo tiếng Việt, không có danh sách lý do', async () => {
    await renderPanel({
      status: 'active',
      onChangeStatus: vi.fn(async () => {
        throw new ApiError(
          'INVALID_STATUS_TRANSITION',
          409,
          'Không thể chuyển trạng thái từ active sang draft.',
        );
      }),
    });
    await click(buttonByName('Ẩn sản phẩm'));
    const alert = container.querySelector('[role="alert"]')!;
    expect(alert.textContent).toBe('Không thể chuyển trạng thái từ active sang draft.');
    expect(alert.querySelector('li')).toBeNull();
  });

  it('lỗi mạng hiện thông báo tiếng Việt', async () => {
    await renderPanel({
      onChangeStatus: vi.fn(async () => {
        throw new ApiError('NETWORK_ERROR', 0, 'Không kết nối được máy chủ.');
      }),
    });
    await click(buttonByName('Đăng bán'));
    expect(container.querySelector('[role="alert"]')?.textContent).toBe(
      'Không kết nối được máy chủ.',
    );
  });

  it('khóa mọi nút trong lúc chờ API và không gọi lần hai', async () => {
    let finish!: () => void;
    const onChangeStatus = vi.fn(
      () =>
        new Promise<void>((resolve) => {
          finish = resolve;
        }),
    );
    await renderPanel({ status: 'hidden', onChangeStatus });
    await click(buttonByName('Bán lại'));

    const buttons = [...container.querySelectorAll<HTMLButtonElement>('button')];
    expect(buttons.length).toBeGreaterThan(0);
    expect(buttons.every((button) => button.disabled)).toBe(true);
    await click(buttons[0]);
    expect(onChangeStatus).toHaveBeenCalledTimes(1);

    await act(async () => finish());
    expect(
      [...container.querySelectorAll<HTMLButtonElement>('button')].some(
        (button) => button.disabled,
      ),
    ).toBe(false);
  });
});

describe('ProductStatusPanel: bấm hai lần liên tiếp', () => {
  it('hai lần bấm nút trạng thái trong cùng một lượt vẽ chỉ gọi API một lần', async () => {
    const onChangeStatus = vi.fn(() => new Promise<void>(() => {}));
    await renderPanel({ onChangeStatus });
    const publish = buttonByName('Đăng bán')!;
    // Cả hai lần bấm chạy trước khi React vẽ lại, nên thuộc tính `disabled` chưa kịp có.
    await act(async () => {
      publish.click();
      publish.click();
    });
    expect(onChangeStatus).toHaveBeenCalledTimes(1);
  });

  it('API trả lỗi thì mở lại và cho bấm lần nữa', async () => {
    const onChangeStatus = vi
      .fn<(target: string) => Promise<void>>()
      .mockRejectedValueOnce(new ApiError('NETWORK_ERROR', 0, 'Không kết nối được máy chủ.'))
      .mockResolvedValueOnce(undefined);
    await renderPanel({ onChangeStatus });
    await click(buttonByName('Đăng bán'));
    await click(buttonByName('Đăng bán'));
    expect(onChangeStatus).toHaveBeenCalledTimes(2);
  });
});

describe('ProductStatusPanel: thay đổi chưa lưu', () => {
  it('khóa các nút trạng thái và hiện câu hướng dẫn', async () => {
    const { onChangeStatus } = await renderPanel({ hasUnsavedChanges: true });
    const publish = buttonByName('Đăng bán')!;
    expect(publish.disabled).toBe(true);
    expect(container.textContent).toContain('Lưu thay đổi trước khi đổi trạng thái.');
    await click(publish);
    expect(onChangeStatus).not.toHaveBeenCalled();
  });

  it('nối câu hướng dẫn với nút bằng aria-describedby', async () => {
    await renderPanel({ hasUnsavedChanges: true });
    const hintId = buttonByName('Đăng bán')!.getAttribute('aria-describedby')!;
    expect(document.getElementById(hintId)?.textContent).toBe(
      'Lưu thay đổi trước khi đổi trạng thái.',
    );
  });

  it('không có thay đổi thì không hiện câu hướng dẫn và nút bật', async () => {
    await renderPanel({ hasUnsavedChanges: false });
    expect(container.textContent).not.toContain('Lưu thay đổi trước');
    expect(buttonByName('Đăng bán')!.disabled).toBe(false);
  });

  it('nút xóa vẫn bật khi có thay đổi chưa lưu', async () => {
    await renderPanel({ hasUnsavedChanges: true });
    expect(buttonByName('Xóa sản phẩm')!.disabled).toBe(false);
  });
});

describe('ProductStatusPanel: quyền', () => {
  it('thiếu products:write thì vẫn thấy nhãn nhưng không có nút nào', async () => {
    await renderPanel({ canWrite: false, status: 'active' });
    expect(container.querySelector('.badge')?.textContent).toBe('Đang bán');
    expect(container.querySelectorAll('button')).toHaveLength(0);
    expect(container.textContent).not.toContain('Xóa sản phẩm');
    expect(container.textContent).not.toContain('Lưu thay đổi trước');
  });
});

describe('ProductStatusPanel: xóa sản phẩm', () => {
  async function openDeleteDialog(options: PanelOptions = {}) {
    const mocks = await renderPanel(options);
    await click(buttonByName('Xóa sản phẩm'));
    return mocks;
  }

  const dialog = () => document.querySelector<HTMLElement>('[role="dialog"]');
  const confirmButton = () =>
    [...dialog()!.querySelectorAll('button')].find(
      (button) => button.textContent === 'Xóa vĩnh viễn',
    );

  it('bấm Xóa sản phẩm mở hộp thoại nêu tên sản phẩm và cảnh báo không hoàn tác', async () => {
    const { onDelete } = await openDeleteDialog();
    expect(dialog()).not.toBeNull();
    expect(dialog()!.textContent).toContain('MacBook Air M3');
    expect(dialog()!.textContent).toContain('không thể hoàn tác');
    expect(onDelete).not.toHaveBeenCalled();
  });

  it('focus nằm ở nút Hủy để Enter không xóa nhầm', async () => {
    await openDeleteDialog();
    expect(document.activeElement?.textContent).toBe('Hủy');
  });

  it('Hủy đóng hộp thoại và không gọi API', async () => {
    const { onDelete } = await openDeleteDialog();
    await click(buttonByName('Hủy'));
    expect(dialog()).toBeNull();
    expect(onDelete).not.toHaveBeenCalled();
  });

  it('phím Esc đóng hộp thoại và không gọi API', async () => {
    const { onDelete } = await openDeleteDialog();
    await pressEscape();
    expect(dialog()).toBeNull();
    expect(onDelete).not.toHaveBeenCalled();
  });

  it('xác nhận gọi onDelete đúng một lần', async () => {
    const { onDelete } = await openDeleteDialog();
    await click(confirmButton());
    expect(onDelete).toHaveBeenCalledTimes(1);
  });

  it('lỗi 409 PRODUCT_HAS_ORDERS hiện trong hộp thoại và hộp thoại không đóng', async () => {
    await openDeleteDialog({
      onDelete: vi.fn(async () => {
        throw new ApiError(
          'PRODUCT_HAS_ORDERS',
          409,
          'Sản phẩm đã có đơn hàng nên không xóa được.',
        );
      }),
    });
    await click(confirmButton());
    expect(dialog()).not.toBeNull();
    expect(dialog()!.querySelector('[role="alert"]')?.textContent).toBe(
      'Sản phẩm đã có đơn hàng nên không xóa được.',
    );
    expect(confirmButton()!.disabled).toBe(false);
  });

  it('khóa nút và không gọi lần hai trong lúc chờ API', async () => {
    const onDelete = vi.fn(() => new Promise<void>(() => {}));
    await openDeleteDialog({ onDelete });
    await click(confirmButton());
    expect(confirmButton()!.disabled).toBe(true);
    expect(buttonByName('Hủy')!.disabled).toBe(true);
    await click(confirmButton());
    expect(onDelete).toHaveBeenCalledTimes(1);
  });

  it('hai lần bấm Xóa vĩnh viễn trong cùng một lượt vẽ chỉ gọi API một lần', async () => {
    const onDelete = vi.fn(() => new Promise<void>(() => {}));
    await openDeleteDialog({ onDelete });
    const confirm = confirmButton()!;
    await act(async () => {
      confirm.click();
      confirm.click();
    });
    expect(onDelete).toHaveBeenCalledTimes(1);
  });

  it('xóa lỗi thì mở lại và cho xác nhận lần nữa', async () => {
    const onDelete = vi
      .fn<() => Promise<void>>()
      .mockRejectedValueOnce(new ApiError('NETWORK_ERROR', 0, 'Không kết nối được máy chủ.'))
      .mockResolvedValueOnce(undefined);
    await openDeleteDialog({ onDelete });
    await click(confirmButton());
    await click(confirmButton());
    expect(onDelete).toHaveBeenCalledTimes(2);
  });

  it('Esc không đóng hộp thoại trong lúc chờ API', async () => {
    await openDeleteDialog({ onDelete: vi.fn(() => new Promise<void>(() => {})) });
    await click(confirmButton());
    await pressEscape();
    expect(dialog()).not.toBeNull();
  });

  it('đóng hộp thoại thì trả focus về nút Xóa sản phẩm', async () => {
    await renderPanel();
    const opener = buttonByName('Xóa sản phẩm')!;
    opener.focus();
    await click(opener);
    await click(buttonByName('Hủy'));
    expect(document.activeElement).toBe(opener);
  });
});
