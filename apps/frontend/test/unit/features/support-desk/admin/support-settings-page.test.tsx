// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SupportSettingsPage } from '../../../../../src/features/support-desk/admin/support-settings-page';
import {
  actorWith,
  CANNED_PATH,
  clickButton,
  createFakeServer,
  createGate,
  errorResponse,
  errorTextOf,
  findButtonByName,
  jsonResponse,
  renderView,
  requestsOf,
  SETTINGS_PATH,
  settle,
  SHARED_EDITABLE,
  SHARED_READ_ONLY,
  stubFetch,
  typeInto,
  type FakeServer,
  type RenderedView,
} from './support-settings-test-kit';

const MANAGER = actorWith(['support-settings:manage', 'conversations:reply']);
const OPENS_AT = '#support-settings-opens-at';
const CLOSES_AT = '#support-settings-closes-at';
const WAIT_NUDGE = '#support-settings-wait-nudge';
const AUTO_CLOSE = '#support-settings-auto-close';
const HOLIDAY_INPUT = '#support-settings-holiday-input';

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

async function openPage(actor = MANAGER) {
  view = await renderView(actor, <SupportSettingsPage />);
  return view.container;
}

function valueOf(root: HTMLElement, selector: string): string {
  const input = root.querySelector<HTMLInputElement>(selector);
  if (!input) throw new Error(`Không thấy ô ${selector}`);
  return input.value;
}

function holidayLabels(root: HTMLElement): string[] {
  return Array.from(root.querySelectorAll('.holiday-date')).map((item) => item.textContent ?? '');
}

function statusRegion(root: HTMLElement): HTMLElement {
  const region = root.querySelector<HTMLElement>('.support-settings-status');
  if (!region) throw new Error('Thiếu vùng role="status" của nút lưu.');
  return region;
}

describe('SupportSettingsPage: quyền', () => {
  it('thiếu support-settings:manage thì hiện thông báo 403 và không có form', async () => {
    const root = await openPage(actorWith(['conversations:read']));

    const notice = root.querySelector('[role="alert"]');
    expect(notice?.textContent).toContain('403');
    expect(notice?.textContent).toContain('support-settings:manage');
    expect(root.querySelector('form')).toBeNull();
    expect(root.querySelector('h1')).toBeNull();
  });

  it('thiếu quyền thì không gọi API cấu hình hay API mẫu', async () => {
    await openPage(actorWith(['conversations:read']));

    expect(server.requests).toEqual([]);
  });
});

describe('SupportSettingsPage: tải và điền form', () => {
  it('có quyền thì hiện tiêu đề và điền đúng giá trị của máy chủ', async () => {
    const root = await openPage();

    expect(root.querySelector('h1')?.textContent).toBe('Cấu hình CSKH');
    expect(valueOf(root, OPENS_AT)).toBe('08:00');
    expect(valueOf(root, CLOSES_AT)).toBe('21:00');
    expect(valueOf(root, WAIT_NUDGE)).toBe('10');
    expect(valueOf(root, AUTO_CLOSE)).toBe('30');
  });

  it('ngày nghỉ hiện dạng dd/MM/yyyy và xếp tăng dần dù máy chủ trả lộn xộn', async () => {
    const root = await openPage();

    expect(holidayLabels(root)).toEqual(['06/02/2027', '07/02/2027']);
  });

  it('mọi ô có nhãn nhìn thấy nối với ô bằng htmlFor', async () => {
    const root = await openPage();

    const labelsByInput: [string, string][] = [
      [OPENS_AT, 'Giờ mở cửa'],
      [CLOSES_AT, 'Giờ đóng cửa'],
      [HOLIDAY_INPUT, 'Thêm ngày nghỉ'],
      [WAIT_NUDGE, 'Nhắc lại phiên chờ sau (phút)'],
      [AUTO_CLOSE, 'Tự đóng phiên im lặng sau (phút)'],
    ];
    for (const [selector, labelText] of labelsByInput) {
      const input = root.querySelector<HTMLInputElement>(selector);
      const label = root.querySelector<HTMLLabelElement>(`label[for="${input?.id}"]`);
      expect(label?.textContent, selector).toBe(labelText);
    }
  });

  it('lỗi tải cấu hình hiện thông báo của máy chủ và nút Thử lại đọc lại được', async () => {
    server.settingsGetFailure = errorResponse(503, 'INTERNAL_ERROR', 'Máy chủ đang bận.');
    const root = await openPage();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Máy chủ đang bận.');
    expect(root.querySelector(OPENS_AT)).toBeNull();

    server.settingsGetFailure = undefined;
    await clickButton(root, 'Thử lại');

    expect(valueOf(root, OPENS_AT)).toBe('08:00');
  });
});

describe('SupportSettingsPage: lưu cấu hình', () => {
  it('vùng role="status" có mặt trước khi lưu và rỗng', async () => {
    const root = await openPage();

    expect(statusRegion(root).getAttribute('role')).toBe('status');
    expect(statusRegion(root).textContent).toBe('');
  });

  it('PUT gửi đủ năm trường, không có updatedAt, rồi hiện thông báo và thời điểm cập nhật', async () => {
    const root = await openPage();
    await typeInto(root, OPENS_AT, '08:30');
    await typeInto(root, WAIT_NUDGE, '15');

    await clickButton(root, 'Lưu cấu hình');

    const [put] = requestsOf(server, 'PUT', SETTINGS_PATH);
    expect(requestsOf(server, 'PUT', SETTINGS_PATH)).toHaveLength(1);
    expect(put?.body).toEqual({
      opensAt: '08:30',
      closesAt: '21:00',
      holidays: ['2027-02-06', '2027-02-07'],
      waitNudgeMinutes: 15,
      autoCloseMinutes: 30,
    });
    const status = statusRegion(root);
    expect(status.textContent).toContain('Đã lưu cấu hình.');
    // 03:00 UTC là 10:00 giờ Việt Nam.
    expect(status.textContent).toContain('Cập nhật lần cuối 10:00 03/11/2026');
  });

  it('sửa ô tiếp theo thì thông báo đã lưu biến mất', async () => {
    const root = await openPage();
    await clickButton(root, 'Lưu cấu hình');
    expect(statusRegion(root).textContent).toContain('Đã lưu cấu hình.');

    await typeInto(root, CLOSES_AT, '20:00');

    expect(statusRegion(root).textContent).toBe('');
  });

  it('lưu xong trả focus về nút Lưu cấu hình', async () => {
    const root = await openPage();

    await clickButton(root, 'Lưu cấu hình');

    expect(document.activeElement?.id).toBe('support-settings-save');
  });

  it('gửi form hai lần khi đang lưu chỉ tạo một request PUT', async () => {
    const gate = createGate();
    server.putSettings = async () => {
      await gate.promise;
      return jsonResponse({ data: { ...server.settings, updatedAt: '2026-11-03T03:00:00Z' } });
    };
    const root = await openPage();
    const form = root.querySelector('form');
    if (!form) throw new Error('Thiếu form.');

    await act(async () => {
      form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    });
    await act(async () => {
      form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    });
    gate.open();
    await settle();

    expect(requestsOf(server, 'PUT', SETTINGS_PATH)).toHaveLength(1);
  });

  it('đang lưu thì nút tắt và đổi chữ thành Đang lưu...', async () => {
    const gate = createGate();
    server.putSettings = async () => {
      await gate.promise;
      return jsonResponse({ data: server.settings });
    };
    const root = await openPage();
    const saveButton = findButtonByName(root, 'Lưu cấu hình');

    await act(async () => saveButton?.click());

    const pendingButton = root.querySelector<HTMLButtonElement>('#support-settings-save');
    expect(pendingButton?.disabled).toBe(true);
    expect(pendingButton?.textContent).toBe('Đang lưu...');
    gate.open();
    await settle();
    expect(root.querySelector<HTMLButtonElement>('#support-settings-save')?.disabled).toBe(false);
  });
});

describe('SupportSettingsPage: kiểm phía client', () => {
  it('mốc nhắc lại bằng mốc tự đóng: không gửi, lỗi dưới ô mốc nhắc lại, focus về ô đó', async () => {
    const root = await openPage();
    await typeInto(root, WAIT_NUDGE, '30');

    await clickButton(root, 'Lưu cấu hình');

    expect(requestsOf(server, 'PUT', SETTINGS_PATH)).toHaveLength(0);
    expect(errorTextOf(root, WAIT_NUDGE)).toBe('Mốc nhắc lại phải nhỏ hơn mốc tự đóng.');
    expect(errorTextOf(root, AUTO_CLOSE)).toBeNull();
    expect(document.activeElement?.id).toBe('support-settings-wait-nudge');
  });

  it('giờ mở cửa sau giờ đóng cửa: lỗi dưới ô giờ mở cửa', async () => {
    const root = await openPage();
    await typeInto(root, OPENS_AT, '22:00');

    await clickButton(root, 'Lưu cấu hình');

    expect(requestsOf(server, 'PUT', SETTINGS_PATH)).toHaveLength(0);
    expect(errorTextOf(root, OPENS_AT)).toBe('Giờ mở cửa phải trước giờ đóng cửa.');
    expect(errorTextOf(root, CLOSES_AT)).toBeNull();
  });

  it('lỗi của ô nào tự xóa khi người dùng sửa ô đó', async () => {
    const root = await openPage();
    await typeInto(root, WAIT_NUDGE, '30');
    await clickButton(root, 'Lưu cấu hình');
    expect(errorTextOf(root, WAIT_NUDGE)).not.toBeNull();

    await typeInto(root, WAIT_NUDGE, '20');

    expect(root.querySelector('.form-error')).toBeNull();
  });

  it('số phút ngoài 1 đến 240 báo lỗi dưới đúng ô', async () => {
    const root = await openPage();
    await typeInto(root, AUTO_CLOSE, '241');

    await clickButton(root, 'Lưu cấu hình');

    expect(errorTextOf(root, AUTO_CLOSE)).toBe('Nhập số nguyên từ 1 đến 240.');
    expect(errorTextOf(root, WAIT_NUDGE)).toBeNull();
  });
});

describe('SupportSettingsPage: lỗi từ API', () => {
  it('400 VALIDATION_ERROR với key waitNudgeMinutes hiện dưới ô mốc nhắc lại, không dưới ô khác', async () => {
    server.putSettings = () =>
      errorResponse(400, 'VALIDATION_ERROR', 'Mốc nhắc lại phải nhỏ hơn mốc tự đóng.', [
        { key: 'waitNudgeMinutes', message: 'Mốc nhắc lại phải nhỏ hơn mốc tự đóng.' },
      ]);
    const root = await openPage();

    await clickButton(root, 'Lưu cấu hình');

    expect(errorTextOf(root, WAIT_NUDGE)).toBe('Mốc nhắc lại phải nhỏ hơn mốc tự đóng.');
    expect(errorTextOf(root, AUTO_CLOSE)).toBeNull();
    expect(errorTextOf(root, OPENS_AT)).toBeNull();
    expect(root.querySelector('.alert')).toBeNull();
    expect(document.activeElement?.id).toBe('support-settings-wait-nudge');
  });

  it('400 với key opensAt hiện dưới ô giờ mở cửa', async () => {
    server.putSettings = () =>
      errorResponse(400, 'VALIDATION_ERROR', 'Giờ mở cửa phải trước giờ đóng cửa.', [
        { key: 'opensAt', message: 'Giờ mở cửa phải trước giờ đóng cửa.' },
      ]);
    const root = await openPage();

    await clickButton(root, 'Lưu cấu hình');

    expect(errorTextOf(root, OPENS_AT)).toBe('Giờ mở cửa phải trước giờ đóng cửa.');
    expect(errorTextOf(root, CLOSES_AT)).toBeNull();
  });

  it('khóa lạ và lỗi 403 hiện bằng ErrorAlert đầu form, giữ nguyên dữ liệu đã nhập', async () => {
    server.putSettings = () =>
      errorResponse(400, 'VALIDATION_ERROR', 'Dữ liệu sai.', [
        { key: 'holidays', message: 'Ngày nghỉ sai.' },
      ]);
    const root = await openPage();
    await typeInto(root, OPENS_AT, '07:15');

    await clickButton(root, 'Lưu cấu hình');

    expect(root.querySelector('form [role="alert"]')?.textContent).toContain('Ngày nghỉ sai.');
    expect(root.querySelector('.form-error')).toBeNull();
    expect(valueOf(root, OPENS_AT)).toBe('07:15');

    server.putSettings = () => errorResponse(403, 'FORBIDDEN', 'Bạn không có quyền lưu.');
    await clickButton(root, 'Lưu cấu hình');
    expect(root.querySelector('form [role="alert"]')?.textContent).toContain(
      'Bạn không có quyền lưu.',
    );
  });
});

describe('SupportSettingsPage: ngày nghỉ', () => {
  async function addDay(root: HTMLElement, isoDay: string) {
    await typeInto(root, HOLIDAY_INPUT, isoDay);
    await clickButton(root, 'Thêm ngày nghỉ');
  }

  it('thêm ngày giữ danh sách tăng dần và xóa ô ngày', async () => {
    const root = await openPage();

    await addDay(root, '2027-01-01');

    expect(holidayLabels(root)).toEqual(['01/01/2027', '06/02/2027', '07/02/2027']);
    expect(valueOf(root, HOLIDAY_INPUT)).toBe('');
  });

  it('thêm ngày đã có thì không thêm và báo lỗi dưới ô ngày', async () => {
    const root = await openPage();

    await addDay(root, '2027-02-06');

    expect(holidayLabels(root)).toEqual(['06/02/2027', '07/02/2027']);
    expect(errorTextOf(root, HOLIDAY_INPUT)).toBe('Ngày này đã có trong danh sách.');
  });

  it('bấm thêm khi ô ngày trống thì báo Hãy chọn một ngày.', async () => {
    const root = await openPage();

    await clickButton(root, 'Thêm ngày nghỉ');

    expect(errorTextOf(root, HOLIDAY_INPUT)).toBe('Hãy chọn một ngày.');
    expect(holidayLabels(root)).toHaveLength(2);
  });

  it('xóa từng ngày bằng nút có tên truy cập riêng', async () => {
    const root = await openPage();

    await clickButton(root, 'Xóa ngày nghỉ 06/02/2027');

    expect(holidayLabels(root)).toEqual(['07/02/2027']);
    expect(findButtonByName(root, 'Xóa ngày nghỉ 06/02/2027')).toBeNull();
    expect(findButtonByName(root, 'Xóa ngày nghỉ 07/02/2027')).not.toBeNull();
  });

  it('xóa hết ngày thì hiện Chưa có ngày nghỉ nào.', async () => {
    const root = await openPage();
    await clickButton(root, 'Xóa ngày nghỉ 06/02/2027');
    await clickButton(root, 'Xóa ngày nghỉ 07/02/2027');

    expect(root.textContent).toContain('Chưa có ngày nghỉ nào.');
  });

  it('Enter trong ô ngày thêm ngày chứ không gửi cả cấu hình', async () => {
    const root = await openPage();
    const input = await typeInto(root, HOLIDAY_INPUT, '2027-03-01');

    await act(async () => {
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    });

    expect(holidayLabels(root)).toContain('01/03/2027');
    expect(requestsOf(server, 'PUT', SETTINGS_PATH)).toHaveLength(0);
  });

  it('ngày đã thêm và đã xóa đi vào body của PUT', async () => {
    const root = await openPage();
    await clickButton(root, 'Xóa ngày nghỉ 07/02/2027');
    await typeInto(root, HOLIDAY_INPUT, '2027-01-01');
    await clickButton(root, 'Thêm ngày nghỉ');

    await clickButton(root, 'Lưu cấu hình');

    expect(requestsOf(server, 'PUT', SETTINGS_PATH)[0]?.body?.holidays).toEqual([
      '2027-01-01',
      '2027-02-06',
    ]);
  });
});

describe('SupportSettingsPage: mẫu dùng chung', () => {
  function rowOf(root: HTMLElement, shortcut: string): HTMLElement | null {
    const rows = Array.from(root.querySelectorAll<HTMLElement>('.canned-row'));
    return (
      rows.find((row) => row.querySelector('.canned-shortcut')?.textContent === `/${shortcut}`) ??
      null
    );
  }

  it('chỉ hiện mẫu dùng chung, không hiện mẫu cá nhân', async () => {
    const root = await openPage();

    expect(rowOf(root, 'camon')).not.toBeNull();
    expect(rowOf(root, 'giaohang')).not.toBeNull();
    expect(rowOf(root, 'kiemdon')).toBeNull();
  });

  it('mẫu có editable bằng false chỉ có nhãn Chỉ xem, không có nút sửa và xóa', async () => {
    const root = await openPage();
    const row = rowOf(root, SHARED_READ_ONLY.shortcut);

    expect(row?.textContent).toContain('Chỉ xem');
    expect(row?.querySelectorAll('button')).toHaveLength(0);
  });

  it('mẫu sửa được có hai nút với tên truy cập riêng theo phím tắt', async () => {
    const root = await openPage();
    const row = rowOf(root, SHARED_EDITABLE.shortcut);

    expect(row?.textContent).not.toContain('Chỉ xem');
    expect(findButtonByName(row!, 'Sửa mẫu /giaohang')).not.toBeNull();
    expect(findButtonByName(row!, 'Xóa mẫu /giaohang')).not.toBeNull();
  });

  it('danh sách rỗng hiện Chưa có mẫu dùng chung nào.', async () => {
    server.canned = [];
    const root = await openPage();

    expect(root.textContent).toContain('Chưa có mẫu dùng chung nào.');
  });

  it('thêm mẫu: bỏ dấu / ở đầu, gửi shared true, đọc lại danh sách và hiện thông báo', async () => {
    const root = await openPage();
    await clickButton(root, 'Thêm mẫu');
    await typeInto(document.body, '#canned-response-shortcut', '/baohanh');
    await typeInto(document.body, '#canned-response-body', 'Bảo hành 12 tháng.');

    await clickButton(document.body, 'Lưu mẫu');

    expect(requestsOf(server, 'POST', CANNED_PATH)[0]?.body).toEqual({
      shortcut: 'baohanh',
      body: 'Bảo hành 12 tháng.',
      shared: true,
    });
    expect(document.body.querySelector('[role="dialog"]')).toBeNull();
    expect(rowOf(root, 'baohanh')).not.toBeNull();
    expect(root.querySelector('.canned-status')?.textContent).toBe('Đã lưu mẫu /baohanh.');
    expect(requestsOf(server, 'GET', CANNED_PATH).length).toBeGreaterThanOrEqual(2);
  });

  it('hộp thoại thêm mẫu có bộ đếm {số ký tự}/2000', async () => {
    const root = await openPage();
    await clickButton(root, 'Thêm mẫu');

    await typeInto(document.body, '#canned-response-body', 'Xin chào');

    expect(document.body.querySelector('.canned-counter')?.textContent).toBe('8/2000');
  });

  it('gửi rỗng thì lỗi hiện dưới hai ô, không gọi POST và hộp thoại còn mở', async () => {
    const root = await openPage();
    await clickButton(root, 'Thêm mẫu');

    await clickButton(document.body, 'Lưu mẫu');

    expect(requestsOf(server, 'POST', CANNED_PATH)).toHaveLength(0);
    expect(errorTextOf(document.body, '#canned-response-shortcut')).toBe('Hãy nhập phím tắt.');
    expect(errorTextOf(document.body, '#canned-response-body')).toBe('Hãy nhập nội dung mẫu.');
    expect(document.body.querySelector('[role="dialog"]')).not.toBeNull();
  });

  it('phím tắt chữ hoa bị chặn ở phía client', async () => {
    const root = await openPage();
    await clickButton(root, 'Thêm mẫu');
    await typeInto(document.body, '#canned-response-shortcut', 'BaoHanh');
    await typeInto(document.body, '#canned-response-body', 'Nội dung.');

    await clickButton(document.body, 'Lưu mẫu');

    expect(requestsOf(server, 'POST', CANNED_PATH)).toHaveLength(0);
    expect(errorTextOf(document.body, '#canned-response-shortcut')).toContain('chữ thường');
  });

  it('409 CANNED_SHORTCUT_TAKEN hiện dưới ô phím tắt và giữ dữ liệu đã nhập', async () => {
    server.postCanned = () =>
      errorResponse(409, 'CANNED_SHORTCUT_TAKEN', 'Bạn đã có mẫu dùng phím tắt này.');
    const root = await openPage();
    await clickButton(root, 'Thêm mẫu');
    await typeInto(document.body, '#canned-response-shortcut', 'camon');
    await typeInto(document.body, '#canned-response-body', 'Nội dung giữ lại.');

    await clickButton(document.body, 'Lưu mẫu');

    expect(errorTextOf(document.body, '#canned-response-shortcut')).toBe(
      'Bạn đã có mẫu dùng phím tắt này.',
    );
    expect(errorTextOf(document.body, '#canned-response-body')).toBeNull();
    expect(valueOf(document.body, '#canned-response-body')).toBe('Nội dung giữ lại.');
    expect(document.body.querySelector('[role="dialog"]')).not.toBeNull();
  });

  it('400 VALIDATION_ERROR gắn lỗi theo key body, và lỗi khác hiện ErrorAlert trong hộp thoại', async () => {
    server.postCanned = () =>
      errorResponse(400, 'VALIDATION_ERROR', 'Nội dung quá dài.', [
        { key: 'body', message: 'Nội dung quá dài.' },
      ]);
    const root = await openPage();
    await clickButton(root, 'Thêm mẫu');
    await typeInto(document.body, '#canned-response-shortcut', 'camon');
    await typeInto(document.body, '#canned-response-body', 'Nội dung.');
    await clickButton(document.body, 'Lưu mẫu');
    expect(errorTextOf(document.body, '#canned-response-body')).toBe('Nội dung quá dài.');
    expect(errorTextOf(document.body, '#canned-response-shortcut')).toBeNull();

    server.postCanned = () => errorResponse(500, 'INTERNAL_ERROR', 'Máy chủ đang bận.');
    await clickButton(document.body, 'Lưu mẫu');
    expect(document.body.querySelector('.dialog [role="alert"]')?.textContent).toContain(
      'Máy chủ đang bận.',
    );
  });

  it('sửa mẫu chỉ gửi trường đã đổi bằng PATCH', async () => {
    const root = await openPage();
    await clickButton(root, 'Sửa mẫu /giaohang');
    await typeInto(document.body, '#canned-response-body', 'Giao trong 2 ngày.');

    await clickButton(document.body, 'Lưu mẫu');

    const [patch] = requestsOf(server, 'PATCH', `${CANNED_PATH}/${SHARED_EDITABLE.id}`);
    expect(patch?.body).toEqual({ body: 'Giao trong 2 ngày.' });
    expect(rowOf(root, 'giaohang')?.textContent).toContain('Giao trong 2 ngày.');
  });

  it('sửa mà không đổi gì thì đóng hộp thoại và không gọi PATCH', async () => {
    const root = await openPage();
    await clickButton(root, 'Sửa mẫu /giaohang');

    await clickButton(document.body, 'Lưu mẫu');

    expect(server.requests.filter((request) => request.method === 'PATCH')).toHaveLength(0);
    expect(document.body.querySelector('[role="dialog"]')).toBeNull();
  });

  it('xóa mẫu phải qua hộp thoại xác nhận, Hủy thì không gọi DELETE', async () => {
    const root = await openPage();

    await clickButton(root, 'Xóa mẫu /giaohang');

    const dialog = document.body.querySelector('[role="dialog"]');
    expect(dialog?.querySelector('h2')?.textContent).toBe('Xóa mẫu /giaohang?');
    expect(server.requests.filter((request) => request.method === 'DELETE')).toHaveLength(0);

    await clickButton(document.body, 'Hủy');
    expect(document.body.querySelector('[role="dialog"]')).toBeNull();
    expect(server.requests.filter((request) => request.method === 'DELETE')).toHaveLength(0);
    expect(rowOf(root, 'giaohang')).not.toBeNull();
  });

  it('xác nhận xóa thì gọi DELETE, bỏ dòng khỏi danh sách và đưa focus về nút Thêm mẫu', async () => {
    const root = await openPage();
    await clickButton(root, 'Xóa mẫu /giaohang');

    await clickButton(document.body, 'Xóa mẫu');

    expect(requestsOf(server, 'DELETE', `${CANNED_PATH}/${SHARED_EDITABLE.id}`)).toHaveLength(1);
    expect(rowOf(root, 'giaohang')).toBeNull();
    expect(document.activeElement?.textContent).toBe('Thêm mẫu');
  });

  it('404 CANNED_RESPONSE_NOT_FOUND khi xóa thì hiện thông báo của API và đọc lại danh sách', async () => {
    server.deleteCanned = () =>
      errorResponse(404, 'CANNED_RESPONSE_NOT_FOUND', 'Mẫu này không còn nữa.');
    const root = await openPage();
    const readsBefore = requestsOf(server, 'GET', CANNED_PATH).length;
    await clickButton(root, 'Xóa mẫu /giaohang');

    await clickButton(document.body, 'Xóa mẫu');

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Mẫu này không còn nữa.');
    expect(requestsOf(server, 'GET', CANNED_PATH).length).toBeGreaterThan(readsBefore);
    expect(document.body.querySelector('[role="dialog"]')).toBeNull();
  });
});

describe('SupportSettingsPage: thiếu quyền conversations:reply', () => {
  it('vai trò chỉ có support-settings:manage: form hiện, khối mẫu hiện câu giải thích và không gọi GET mẫu', async () => {
    const root = await openPage(actorWith(['support-settings:manage']));

    expect(valueOf(root, OPENS_AT)).toBe('08:00');
    expect(root.querySelector('.canned-panel')).toBeNull();
    expect(root.textContent).toContain(
      'Bạn cần quyền conversations:reply để xem và sửa mẫu trả lời.',
    );
    expect(findButtonByName(root, 'Thêm mẫu')).toBeNull();
    expect(requestsOf(server, 'GET', CANNED_PATH)).toHaveLength(0);
  });

  it('đủ hai quyền thì khối mẫu gọi GET và có nút Thêm mẫu', async () => {
    const root = await openPage();

    expect(requestsOf(server, 'GET', CANNED_PATH).length).toBeGreaterThan(0);
    expect(findButtonByName(root, 'Thêm mẫu')).not.toBeNull();
  });
});

describe('SupportSettingsPage: ngày đã chọn nhưng chưa thêm', () => {
  it('bấm Lưu khi ô ngày còn giá trị thì không gửi, hiện lỗi dưới ô ngày và focus về ô ngày', async () => {
    const root = await openPage();
    await typeInto(root, HOLIDAY_INPUT, '2027-03-01');

    await clickButton(root, 'Lưu cấu hình');

    expect(requestsOf(server, 'PUT', SETTINGS_PATH)).toHaveLength(0);
    expect(errorTextOf(root, HOLIDAY_INPUT)).toBe(
      'Bấm Thêm ngày nghỉ để thêm ngày này, hoặc xóa ô ngày.',
    );
    expect(document.activeElement?.id).toBe('support-settings-holiday-input');
  });

  it('thêm ngày xong rồi lưu thì request đi và có ngày đó', async () => {
    const root = await openPage();
    await typeInto(root, HOLIDAY_INPUT, '2027-03-01');
    await clickButton(root, 'Thêm ngày nghỉ');

    await clickButton(root, 'Lưu cấu hình');

    expect(requestsOf(server, 'PUT', SETTINGS_PATH)[0]?.body?.holidays).toContain('2027-03-01');
  });

  it('xóa ô ngày thì lưu được và lỗi biến mất', async () => {
    const root = await openPage();
    await typeInto(root, HOLIDAY_INPUT, '2027-03-01');
    await clickButton(root, 'Lưu cấu hình');

    await typeInto(root, HOLIDAY_INPUT, '');
    await clickButton(root, 'Lưu cấu hình');

    expect(errorTextOf(root, HOLIDAY_INPUT)).toBeNull();
    expect(requestsOf(server, 'PUT', SETTINGS_PATH)).toHaveLength(1);
  });
});

describe('SupportSettingsPage: hộp thoại mẫu', () => {
  async function pressEscape() {
    await act(async () => {
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    });
  }

  const SHIP_ENTRY = {
    id: 'ca9',
    shortcut: 'ship',
    body: 'Phí ship 30.000 đ.',
    shared: true,
    editable: true,
  };

  it('Esc khi đang lưu không đóng hộp thêm mẫu, và lỗi 409 vẫn hiện khi request xong', async () => {
    const gate = createGate();
    server.postCanned = async () => {
      await gate.promise;
      return errorResponse(409, 'CANNED_SHORTCUT_TAKEN', 'Bạn đã có mẫu dùng phím tắt này.');
    };
    const root = await openPage();
    await clickButton(root, 'Thêm mẫu');
    await typeInto(document.body, '#canned-response-shortcut', 'camon');
    await typeInto(document.body, '#canned-response-body', 'Nội dung.');
    await clickButton(document.body, 'Lưu mẫu');

    await pressEscape();
    expect(document.body.querySelector('[role="dialog"]')).not.toBeNull();

    gate.open();
    await settle();
    expect(errorTextOf(document.body, '#canned-response-shortcut')).toBe(
      'Bạn đã có mẫu dùng phím tắt này.',
    );
  });

  it('Esc khi đang xóa không đóng hộp xác nhận', async () => {
    const gate = createGate();
    server.deleteCanned = async () => {
      await gate.promise;
      return new Response(null, { status: 204 });
    };
    const root = await openPage();
    await clickButton(root, 'Xóa mẫu /giaohang');
    await clickButton(document.body, 'Xóa mẫu');

    await pressEscape();
    expect(document.body.querySelector('[role="dialog"]')).not.toBeNull();

    gate.open();
    await settle();
    expect(document.body.querySelector('[role="dialog"]')).toBeNull();
  });

  it('PATCH trả 404: hiện thông báo của API, đọc lại danh sách và focus về nút Thêm mẫu', async () => {
    server.canned = [...server.canned, SHIP_ENTRY];
    server.patchCanned = () =>
      errorResponse(404, 'CANNED_RESPONSE_NOT_FOUND', 'Mẫu này không còn nữa.');
    const root = await openPage();
    const readsBefore = requestsOf(server, 'GET', CANNED_PATH).length;
    await clickButton(root, 'Sửa mẫu /giaohang');
    await typeInto(document.body, '#canned-response-body', 'Nội dung mới.');
    // Mẫu biến mất ở máy chủ, nên lần đọc lại không còn dòng này.
    server.canned = server.canned.filter((entry) => entry.id !== SHARED_EDITABLE.id);

    await clickButton(document.body, 'Lưu mẫu');

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Mẫu này không còn nữa.');
    expect(requestsOf(server, 'GET', CANNED_PATH).length).toBeGreaterThan(readsBefore);
    expect(document.body.querySelector('[role="dialog"]')).toBeNull();
    expect(document.activeElement?.textContent).toBe('Thêm mẫu');
  });

  it('Hủy xóa sau một lần 404 khi sửa trả focus về nút Xóa mẫu, không về Thêm mẫu', async () => {
    server.canned = [...server.canned, SHIP_ENTRY];
    server.patchCanned = () =>
      errorResponse(404, 'CANNED_RESPONSE_NOT_FOUND', 'Mẫu này không còn nữa.');
    const root = await openPage();
    await clickButton(root, 'Sửa mẫu /giaohang');
    await typeInto(document.body, '#canned-response-body', 'Nội dung mới.');
    server.canned = server.canned.filter((entry) => entry.id !== SHARED_EDITABLE.id);
    await clickButton(document.body, 'Lưu mẫu');

    // Bấm chuột thật đưa focus vào nút trước. `button.click()` của jsdom không làm vậy, nên focus nút bằng tay.
    const deleteButton = findButtonByName(root, 'Xóa mẫu /ship');
    await act(async () => deleteButton?.focus());
    await clickButton(root, 'Xóa mẫu /ship');
    await clickButton(document.body, 'Hủy');

    expect(document.activeElement).toBe(findButtonByName(root, 'Xóa mẫu /ship'));
  });

  it('ô phím tắt và ô nội dung nối với câu gợi ý và bộ đếm bằng aria-describedby', async () => {
    const root = await openPage();
    await clickButton(root, 'Thêm mẫu');
    const shortcut = document.body.querySelector('#canned-response-shortcut');
    const body = document.body.querySelector('#canned-response-body');

    expect(shortcut?.getAttribute('aria-describedby')).toBe('canned-response-shortcut-hint');
    expect(body?.getAttribute('aria-describedby')).toBe('canned-response-body-counter');

    await clickButton(document.body, 'Lưu mẫu');
    expect(shortcut?.getAttribute('aria-describedby')).toBe(
      'canned-response-shortcut-error canned-response-shortcut-hint',
    );
    expect(body?.getAttribute('aria-describedby')).toBe(
      'canned-response-body-error canned-response-body-counter',
    );
  });

  it('phần giới thiệu nói rõ mẫu lưu ngay khi bấm Lưu mẫu', async () => {
    const root = await openPage();

    expect(root.querySelector('.canned-intro')?.textContent).toContain(
      'Mẫu lưu ngay khi bấm Lưu mẫu, không cần bấm Lưu cấu hình.',
    );
  });
});
