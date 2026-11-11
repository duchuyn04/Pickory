// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { StaffMessageComposer } from '../../../../../src/features/support-desk/admin/staff-message-composer';
import {
  actorWith,
  CANNED_PATH,
  clickButton,
  createFakeServer,
  errorResponse,
  errorTextOf,
  findButtonByName,
  PERSONAL,
  renderView,
  requestsOf,
  stubFetch,
  typeInto,
  type FakeServer,
  type RenderedView,
} from './support-settings-test-kit';

let view: RenderedView;
let server: FakeServer;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  server = createFakeServer();
  stubFetch(server);
});

afterEach(async () => {
  await view?.unmount();
  vi.unstubAllGlobals();
});

async function openComposer(permissions: string[]) {
  view = await renderView(
    actorWith(permissions),
    <StaffMessageComposer
      isHeld
      canReply={permissions.includes('conversations:reply')}
      disabledNote={null}
      isSending={false}
      variables={{ tenKhach: 'Lan', maDon: null }}
      onSend={async () => true}
    />,
  );
  return view.container;
}

function dialogTitles(): string[] {
  return Array.from(document.body.querySelectorAll('[role="dialog"] h2')).map(
    (heading) => heading.textContent ?? '',
  );
}

function personalRows(): string[] {
  return Array.from(document.body.querySelectorAll('.canned-shortcut')).map(
    (item) => item.textContent ?? '',
  );
}

async function pressEscape() {
  await act(async () => {
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
  });
}

describe('nút Quản lý mẫu trong khung soạn tin', () => {
  it('người có conversations:reply thấy nút', async () => {
    const root = await openComposer(['conversations:reply']);

    expect(findButtonByName(root, 'Quản lý mẫu')).not.toBeNull();
  });

  it('người thiếu conversations:reply không thấy nút', async () => {
    const root = await openComposer(['conversations:read']);

    expect(findButtonByName(root, 'Quản lý mẫu')).toBeNull();
  });
});

describe('hộp thoại Mẫu của tôi', () => {
  it('chỉ hiện mẫu cá nhân, không hiện mẫu dùng chung và không có nhãn Chỉ xem', async () => {
    const root = await openComposer(['conversations:reply']);

    await clickButton(root, 'Quản lý mẫu');

    expect(dialogTitles()).toEqual(['Mẫu của tôi']);
    expect(personalRows()).toEqual([`/${PERSONAL.shortcut}`]);
    expect(document.body.textContent).not.toContain('Chỉ xem');
  });

  it('chưa có mẫu cá nhân thì hiện Bạn chưa có mẫu nào.', async () => {
    server.canned = server.canned.filter((entry) => entry.shared);
    const root = await openComposer(['conversations:reply']);

    await clickButton(root, 'Quản lý mẫu');

    expect(document.body.textContent).toContain('Bạn chưa có mẫu nào.');
  });

  it('tạo mẫu cá nhân gửi shared false và khung soạn tin dùng ngay danh sách mới', async () => {
    const root = await openComposer(['conversations:reply']);
    await clickButton(root, 'Quản lý mẫu');
    await clickButton(document.body, 'Thêm mẫu');
    await typeInto(document.body, '#canned-response-shortcut', 'baohanh');
    await typeInto(document.body, '#canned-response-body', 'Bảo hành 12 tháng.');

    await clickButton(document.body, 'Lưu mẫu');

    expect(requestsOf(server, 'POST', CANNED_PATH)[0]?.body).toEqual({
      shortcut: 'baohanh',
      body: 'Bảo hành 12 tháng.',
      shared: false,
    });
    expect(personalRows()).toContain('/baohanh');
    // Đóng hộp thoại rồi gõ `/bao` vào ô soạn: danh sách gợi ý của khung soạn phải có mẫu mới.
    await clickButton(document.body, 'Đóng');
    await typeInto(root, 'textarea', '/bao');
    const options = Array.from(root.querySelectorAll('[role="option"]')).map(
      (option) => option.textContent ?? '',
    );
    expect(options.some((option) => option.includes('/baohanh'))).toBe(true);
  });

  it('409 của mẫu cá nhân hiện đúng câu của máy chủ dưới ô phím tắt', async () => {
    server.postCanned = () =>
      errorResponse(409, 'CANNED_SHORTCUT_TAKEN', 'Bạn đã có mẫu dùng phím tắt này.');
    const root = await openComposer(['conversations:reply']);
    await clickButton(root, 'Quản lý mẫu');
    await clickButton(document.body, 'Thêm mẫu');
    await typeInto(document.body, '#canned-response-shortcut', 'kiemdon');
    await typeInto(document.body, '#canned-response-body', 'Nội dung giữ lại.');

    await clickButton(document.body, 'Lưu mẫu');

    expect(errorTextOf(document.body, '#canned-response-shortcut')).toBe(
      'Bạn đã có mẫu dùng phím tắt này.',
    );
  });

  it('Esc khi hộp thêm mẫu đang mở chỉ đóng hộp thêm mẫu, hộp Mẫu của tôi còn mở', async () => {
    const root = await openComposer(['conversations:reply']);
    await clickButton(root, 'Quản lý mẫu');
    await clickButton(document.body, 'Thêm mẫu');
    expect(dialogTitles()).toEqual(['Mẫu của tôi', 'Thêm mẫu']);

    await pressEscape();

    expect(dialogTitles()).toEqual(['Mẫu của tôi']);
  });

  it('đóng hộp thoại thì focus về nút Quản lý mẫu', async () => {
    const root = await openComposer(['conversations:reply']);
    await clickButton(root, 'Quản lý mẫu');

    await clickButton(document.body, 'Đóng');

    expect(dialogTitles()).toEqual([]);
    expect(document.activeElement).toBe(findButtonByName(root, 'Quản lý mẫu'));
  });

  it('mở hộp thoại thì khóa cuộn trang nền và đóng thì trả lại', async () => {
    document.body.style.overflow = '';
    const root = await openComposer(['conversations:reply']);

    await clickButton(root, 'Quản lý mẫu');
    expect(document.body.style.overflow).toBe('hidden');

    await clickButton(document.body, 'Đóng');
    expect(document.body.style.overflow).toBe('');
  });

  it('hai hộp chồng nhau: đóng hộp trong vẫn khóa cuộn, đóng hộp ngoài trả lại giá trị cũ', async () => {
    document.body.style.overflow = 'auto';
    const root = await openComposer(['conversations:reply']);
    await clickButton(root, 'Quản lý mẫu');
    await clickButton(document.body, 'Thêm mẫu');
    expect(dialogTitles()).toEqual(['Mẫu của tôi', 'Thêm mẫu']);

    await clickButton(document.body, 'Hủy');
    expect(dialogTitles()).toEqual(['Mẫu của tôi']);
    expect(document.body.style.overflow).toBe('hidden');

    await clickButton(document.body, 'Đóng');
    expect(document.body.style.overflow).toBe('auto');
  });
});
