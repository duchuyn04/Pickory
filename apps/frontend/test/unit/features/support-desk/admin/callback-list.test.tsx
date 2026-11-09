// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { clickButton, waitFor } from '../../identity/admin/admin-list-test-kit';
import {
  READ_AND_REPLY,
  READ_ONLY,
  activeEventSource,
  callbackRequest,
  findSwitch,
  openDesk,
  openDeskPage,
  supportRequests,
  textOf,
  type OpenedDesk,
} from './support-desk-test-kit';

// Số lần giao diện đã đọc danh sách yêu cầu gọi lại.
function callbackReads(): number {
  return supportRequests(desk.harness).filter((url) => url.endsWith('/callback-requests')).length;
}

let desk: OpenedDesk;

afterEach(async () => {
  await desk.harness.cleanup();
});

async function openCallbacksTab() {
  await openDeskPage(desk);
  await clickButton(desk.harness.container, 'Gọi lại');
  await waitFor(
    () => desk.harness.container.querySelector('[aria-label="Yêu cầu gọi lại"]') !== null,
    'danh sách yêu cầu gọi lại',
  );
}

function groupHeading(title: string): HTMLElement | null {
  return (
    [...desk.harness.container.querySelectorAll<HTMLElement>('h3')].find((heading) =>
      heading.textContent?.startsWith(title),
    ) ?? null
  );
}

function groupRows(title: string): string[] {
  const heading = groupHeading(title);
  const list = heading?.parentElement?.querySelector('ul');
  return [...(list?.querySelectorAll('li') ?? [])].map((row) => row.textContent ?? '');
}

function markButton(label: string): HTMLButtonElement | null {
  return desk.harness.container.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);
}

async function press(button: HTMLButtonElement | null) {
  if (button === null) throw new Error('Thiếu nút đánh dấu.');
  await act(async () => button.click());
}

function statusLine(): HTMLElement {
  const line = desk.harness.container.querySelector<HTMLElement>('.support-callbacks-status');
  if (line === null) throw new Error('Thiếu dòng trạng thái.');
  return line;
}

function patchWrites() {
  return desk.harness.writes.filter((write) => write.method === 'PATCH');
}

describe('tab Gọi lại: hiển thị', () => {
  it('đưa nhóm Cần gọi lại lên trước, dù dòng đã xử lý đứng đầu mảng API', async () => {
    desk = openDesk(READ_ONLY, {
      callbacks: [
        callbackRequest('2', 'done'),
        callbackRequest('1', 'pending'),
        callbackRequest('3', 'unreachable'),
      ],
    });

    await openCallbacksTab();

    const headings = [...desk.harness.container.querySelectorAll('h3')].map((h) => h.textContent);
    expect(headings).toEqual(['Cần gọi lại 1', 'Đã xử lý']);
    expect(groupRows('Cần gọi lại')).toHaveLength(1);
    expect(groupRows('Cần gọi lại')[0]).toContain('0900000001');
    expect(groupRows('Đã xử lý')).toHaveLength(2);
    expect(groupRows('Đã xử lý')[0]).toContain('Đã gọi');
    expect(groupRows('Đã xử lý')[1]).toContain('Không liên lạc được');
  });

  it('số điện thoại là link tel: chỉ giữ chữ số và dấu +, chữ hiện nguyên số API trả', async () => {
    desk = openDesk(READ_ONLY, {
      callbacks: [callbackRequest('1', 'pending', { phone: '+84 901.234-567' })],
    });

    await openCallbacksTab();

    const link = desk.harness.container.querySelector<HTMLAnchorElement>('a[href^="tel:"]');
    expect(link?.getAttribute('href')).toBe('tel:+84901234567');
    expect(link?.textContent).toBe('+84 901.234-567');
  });

  it('phone bằng null hiện Đã xóa số và không có link', async () => {
    desk = openDesk(READ_ONLY, {
      callbacks: [callbackRequest('1', 'done', { phone: null })],
    });

    await openCallbacksTab();

    expect(groupRows('Đã xử lý')[0]).toContain('Đã xóa số');
    expect(desk.harness.container.querySelector('a[href^="tel:"]')).toBeNull();
  });

  it('chuỗi không có chữ số hiện chữ thường, không tạo link', async () => {
    desk = openDesk(READ_ONLY, {
      callbacks: [callbackRequest('1', 'pending', { phone: '---' })],
    });

    await openCallbacksTab();

    expect(desk.harness.container.querySelector('a[href^="tel:"]')).toBeNull();
    expect(groupRows('Cần gọi lại')[0]).toContain('---');
  });

  it('ghi chú vẽ bằng text, không dựng HTML từ dữ liệu API', async () => {
    desk = openDesk(READ_ONLY, {
      callbacks: [callbackRequest('1', 'pending', { note: '<b>Gọi sau 18 giờ</b>' })],
    });

    await openCallbacksTab();

    expect(groupRows('Cần gọi lại')[0]).toContain('<b>Gọi sau 18 giờ</b>');
    expect(desk.harness.container.querySelector('.support-callback-note b')).toBeNull();
  });

  it('dòng đã xử lý có giờ xử lý, dòng cần gọi thì không', async () => {
    desk = openDesk(READ_ONLY, {
      callbacks: [callbackRequest('1', 'pending'), callbackRequest('2', 'done')],
    });

    await openCallbacksTab();

    expect(groupRows('Cần gọi lại')[0]).not.toContain('Xử lý lúc');
    expect(groupRows('Đã xử lý')[0]).toContain('Xử lý lúc');
  });

  it('không có dòng cần gọi nhưng còn dòng đã xử lý: hiện câu báo rồi nhóm Đã xử lý', async () => {
    desk = openDesk(READ_ONLY, { callbacks: [callbackRequest('2', 'done')] });

    await openCallbacksTab();

    expect(textOf(desk.harness.container)).toContain('Không có yêu cầu nào cần gọi lại.');
    expect(groupHeading('Cần gọi lại')).toBeNull();
    expect(groupRows('Đã xử lý')).toHaveLength(1);
  });

  it('không có dòng nào: giữ câu Chưa có yêu cầu gọi lại nào', async () => {
    desk = openDesk(READ_ONLY, { callbacks: [] });
    await openDeskPage(desk);

    await clickButton(desk.harness.container, 'Gọi lại');

    await waitFor(
      () => textOf(desk.harness.container).includes('Chưa có yêu cầu gọi lại nào.'),
      'danh sách rỗng',
    );
  });

  it('chỉ hiện 20 dòng đã xử lý đầu tiên và có dòng chú thích', async () => {
    desk = openDesk(READ_ONLY, {
      callbacks: Array.from({ length: 22 }, (_, index) =>
        callbackRequest(String(index + 1), 'done'),
      ),
    });

    await openCallbacksTab();

    expect(groupRows('Đã xử lý')).toHaveLength(20);
    expect(textOf(desk.harness.container)).toContain('Chỉ hiện 20 yêu cầu đã xử lý gần nhất.');
  });

  it('mỗi nhóm là một danh sách có tên lấy từ tiêu đề nhóm', async () => {
    desk = openDesk(READ_ONLY, {
      callbacks: [callbackRequest('1', 'pending'), callbackRequest('2', 'done')],
    });

    await openCallbacksTab();

    const lists = [...desk.harness.container.querySelectorAll('ul[aria-labelledby]')];
    const names = lists.map(
      (list) => document.getElementById(list.getAttribute('aria-labelledby') ?? '')?.textContent,
    );
    expect(names).toEqual(['Cần gọi lại 1', 'Đã xử lý']);
  });
});

describe('tab Gọi lại: quyền', () => {
  it('người thiếu conversations:reply thấy danh sách nhưng không thấy nút đánh dấu', async () => {
    desk = openDesk(READ_ONLY, { callbacks: [callbackRequest('1', 'pending')] });

    await openCallbacksTab();

    expect(groupRows('Cần gọi lại')).toHaveLength(1);
    expect(markButton('Đánh dấu đã gọi: 0900000001')).toBeNull();
    expect(markButton('Đánh dấu không liên lạc được: 0900000001')).toBeNull();
  });

  it('người có conversations:reply thấy nút ở dòng cần gọi, và không có nút ở dòng đã xử lý', async () => {
    desk = openDesk(READ_AND_REPLY, {
      callbacks: [callbackRequest('1', 'pending'), callbackRequest('2', 'done')],
    });

    await openCallbacksTab();

    expect(markButton('Đánh dấu đã gọi: 0900000001')).not.toBeNull();
    expect(markButton('Đánh dấu không liên lạc được: 0900000001')).not.toBeNull();
    expect(desk.harness.container.querySelectorAll('.support-callback-actions')).toHaveLength(1);
  });

  it('hai dòng có tên nút khác nhau, và dòng xóa số dùng chữ Đã xóa số', async () => {
    desk = openDesk(READ_AND_REPLY, {
      callbacks: [
        callbackRequest('1', 'pending'),
        callbackRequest('3', 'pending', { phone: null }),
      ],
    });

    await openCallbacksTab();

    expect(markButton('Đánh dấu đã gọi: 0900000001')).not.toBeNull();
    expect(markButton('Đánh dấu đã gọi: Đã xóa số')).not.toBeNull();
  });
});

describe('tab Gọi lại: đánh dấu một yêu cầu', () => {
  it('Đã gọi gửi PATCH body done, dòng sang nhóm Đã xử lý, tiêu điểm về tiêu đề nhóm còn lại', async () => {
    desk = openDesk(READ_AND_REPLY, {
      callbacks: [callbackRequest('1', 'pending'), callbackRequest('3', 'pending')],
    });
    await openCallbacksTab();

    await press(markButton('Đánh dấu đã gọi: 0900000001'));

    await waitFor(() => groupRows('Đã xử lý').length === 1, 'dòng sang nhóm Đã xử lý');
    expect(patchWrites()).toEqual([
      {
        method: 'PATCH',
        url: '/api/admin/support/callback-requests/1',
        body: { status: 'done' },
      },
    ]);
    expect(groupRows('Cần gọi lại')).toHaveLength(1);
    expect(groupRows('Đã xử lý')[0]).toContain('0900000001');
    expect(document.activeElement).toBe(groupHeading('Cần gọi lại'));
    expect(statusLine().textContent).toBe('Đã đánh dấu yêu cầu 0900000001 là Đã gọi.');
  });

  it('Không liên lạc được gửi PATCH body unreachable', async () => {
    desk = openDesk(READ_AND_REPLY, { callbacks: [callbackRequest('1', 'pending')] });
    await openCallbacksTab();

    await press(markButton('Đánh dấu không liên lạc được: 0900000001'));

    await waitFor(() => groupRows('Đã xử lý').length === 1, 'dòng sang nhóm Đã xử lý');
    expect(patchWrites().map((write) => write.body)).toEqual([{ status: 'unreachable' }]);
    expect(groupRows('Đã xử lý')[0]).toContain('Không liên lạc được');
    expect(statusLine().textContent).toBe('Đã đánh dấu yêu cầu 0900000001 là Không liên lạc được.');
  });

  it('dòng cuối cùng rời nhóm Cần gọi lại thì tiêu điểm về tiêu đề Đã xử lý', async () => {
    desk = openDesk(READ_AND_REPLY, { callbacks: [callbackRequest('1', 'pending')] });
    await openCallbacksTab();

    await press(markButton('Đánh dấu đã gọi: 0900000001'));

    await waitFor(() => groupHeading('Cần gọi lại') === null, 'nhóm Cần gọi lại biến mất');
    expect(document.activeElement).toBe(groupHeading('Đã xử lý'));
  });

  it('đọc lại danh sách sau khi đánh dấu thành công', async () => {
    desk = openDesk(READ_AND_REPLY, { callbacks: [callbackRequest('1', 'pending')] });
    await openCallbacksTab();
    const readsBefore = supportRequests(desk.harness).filter((url) =>
      url.endsWith('/callback-requests'),
    ).length;

    await press(markButton('Đánh dấu đã gọi: 0900000001'));

    await waitFor(
      () =>
        supportRequests(desk.harness).filter((url) => url.endsWith('/callback-requests')).length >
        readsBefore,
      'đọc lại danh sách',
    );
  });

  it('bấm hai nút gần nhau chỉ gọi API một lần, hai nút tắt và nút vừa bấm hiện Đang lưu...', async () => {
    let releaseRequest: () => void = () => {};
    desk = openDesk(READ_AND_REPLY, {
      callbacks: [callbackRequest('1', 'pending')],
      callbackUpdateGate: new Promise<void>((resolve) => {
        releaseRequest = resolve;
      }),
    });
    await openCallbacksTab();
    const doneButton = markButton('Đánh dấu đã gọi: 0900000001');
    const unreachableButton = markButton('Đánh dấu không liên lạc được: 0900000001');

    // Hai cú bấm trong cùng một lượt, trước khi React vẽ lại trạng thái tắt nút.
    await act(async () => {
      doneButton?.click();
      unreachableButton?.click();
    });

    expect(patchWrites()).toHaveLength(1);
    expect(doneButton?.disabled).toBe(true);
    expect(unreachableButton?.disabled).toBe(true);
    expect(doneButton?.textContent).toBe('Đang lưu...');
    expect(unreachableButton?.textContent).toBe('Không liên lạc được');

    await act(async () => releaseRequest());
    await waitFor(() => groupRows('Đã xử lý').length === 1, 'dòng sang nhóm Đã xử lý');
    expect(patchWrites()).toHaveLength(1);
  });

  it('dòng khác vẫn bấm được khi một dòng đang lưu', async () => {
    let releaseRequest: () => void = () => {};
    desk = openDesk(READ_AND_REPLY, {
      callbacks: [callbackRequest('1', 'pending'), callbackRequest('3', 'pending')],
      callbackUpdateGate: new Promise<void>((resolve) => {
        releaseRequest = resolve;
      }),
    });
    await openCallbacksTab();

    await press(markButton('Đánh dấu đã gọi: 0900000001'));

    expect(markButton('Đánh dấu đã gọi: 0900000003')?.disabled).toBe(false);
    await act(async () => releaseRequest());
    await waitFor(() => groupRows('Đã xử lý').length === 1, 'dòng sang nhóm Đã xử lý');
  });

  it('lỗi hiện thông báo của API ngay dưới dòng, dòng ở nhóm cũ và hai nút bật lại', async () => {
    desk = openDesk(READ_AND_REPLY, {
      callbacks: [callbackRequest('1', 'pending')],
      callbackUpdateFailure: 'server',
    });
    await openCallbacksTab();

    await press(markButton('Đánh dấu đã gọi: 0900000001'));

    await waitFor(
      () => textOf(desk.harness.container).includes('Máy chủ đang bận.'),
      'lỗi của API',
    );
    const row = desk.harness.container.querySelector('.support-callback');
    expect(row?.querySelector('[role="alert"]')?.textContent).toContain('Máy chủ đang bận.');
    expect(groupRows('Cần gọi lại')).toHaveLength(1);
    expect(markButton('Đánh dấu đã gọi: 0900000001')?.disabled).toBe(false);
    expect(markButton('Đánh dấu không liên lạc được: 0900000001')?.disabled).toBe(false);
  });

  it('lỗi 404: thông báo của API vẫn hiện sau khi lần đọc lại gỡ dòng, kể cả khi danh sách hết dòng', async () => {
    desk = openDesk(READ_AND_REPLY, {
      callbacks: [callbackRequest('1', 'pending')],
      callbackUpdateFailure: 'not-found',
    });
    await openCallbacksTab();
    const readsBefore = callbackReads();

    await press(markButton('Đánh dấu đã gọi: 0900000001'));

    await waitFor(() => callbackReads() > readsBefore, 'đọc lại danh sách sau lỗi 404');
    await waitFor(() => groupHeading('Cần gọi lại') === null, 'dòng đã biến mất');
    const alert = desk.harness.container.querySelector('.support-callbacks [role="alert"]');
    expect(alert?.textContent).toContain('Yêu cầu gọi lại không còn nữa.');
    // Thông báo nằm ngoài dòng, nên không bị gỡ cùng dòng.
    expect(alert?.closest('li')).toBeNull();
    expect(textOf(desk.harness.container)).toContain('Chưa có yêu cầu gọi lại nào.');
  });

  it('lỗi 404 được xóa khi nhân viên bắt đầu đánh dấu yêu cầu khác', async () => {
    desk = openDesk(READ_AND_REPLY, {
      callbacks: [callbackRequest('1', 'pending'), callbackRequest('3', 'pending')],
      callbackUpdateFailure: 'not-found',
    });
    await openCallbacksTab();
    await press(markButton('Đánh dấu đã gọi: 0900000001'));
    await waitFor(
      () => textOf(desk.harness.container).includes('Yêu cầu gọi lại không còn nữa.'),
      'lỗi 404',
    );

    desk.state.callbackUpdateFailure = 'none';
    await press(markButton('Đánh dấu đã gọi: 0900000003'));

    await waitFor(() => groupRows('Đã xử lý').length === 1, 'dòng 3 sang nhóm Đã xử lý');
    expect(textOf(desk.harness.container)).not.toContain('Yêu cầu gọi lại không còn nữa.');
  });

  it('dòng sang nhóm Đã xử lý ngay khi PATCH trả về, trước khi lần đọc lại xong', async () => {
    desk = openDesk(READ_AND_REPLY, { callbacks: [callbackRequest('1', 'pending')] });
    await openCallbacksTab();
    let releaseList: () => void = () => {};
    desk.state.callbackListGate = new Promise<void>((resolve) => {
      releaseList = resolve;
    });

    await press(markButton('Đánh dấu đã gọi: 0900000001'));

    // Lần đọc lại đang bị giữ, nên chỉ response của PATCH có thể đã đổi nhóm của dòng.
    await waitFor(() => groupRows('Đã xử lý').length === 1, 'dòng sang nhóm Đã xử lý');
    expect(groupHeading('Cần gọi lại')).toBeNull();
    await act(async () => releaseList());
  });

  it('không kéo tiêu điểm về tiêu đề nhóm khi nhân viên đã chuyển sang phần tử ngoài tab', async () => {
    let releaseUpdate: () => void = () => {};
    desk = openDesk(READ_AND_REPLY, {
      callbacks: [callbackRequest('1', 'pending'), callbackRequest('3', 'pending')],
      callbackUpdateGate: new Promise<void>((resolve) => {
        releaseUpdate = resolve;
      }),
    });
    await openCallbacksTab();
    await press(markButton('Đánh dấu đã gọi: 0900000001'));
    const soundSwitch = findSwitch(desk.harness.container, 'Âm báo');
    await act(async () => soundSwitch?.focus());
    expect(document.activeElement).toBe(soundSwitch);

    await act(async () => releaseUpdate());

    await waitFor(() => groupRows('Đã xử lý').length === 1, 'dòng sang nhóm Đã xử lý');
    expect(document.activeElement).toBe(soundSwitch);
  });

  it('không kéo tiêu điểm khi nhân viên đã Tab sang nút của dòng khác trong tab', async () => {
    let releaseUpdate: () => void = () => {};
    desk = openDesk(READ_AND_REPLY, {
      callbacks: [callbackRequest('1', 'pending'), callbackRequest('3', 'pending')],
      callbackUpdateGate: new Promise<void>((resolve) => {
        releaseUpdate = resolve;
      }),
    });
    await openCallbacksTab();
    await press(markButton('Đánh dấu đã gọi: 0900000001'));
    const otherRowButton = markButton('Đánh dấu đã gọi: 0900000003');
    await act(async () => otherRowButton?.focus());
    expect(document.activeElement).toBe(otherRowButton);

    await act(async () => releaseUpdate());

    await waitFor(() => groupRows('Đã xử lý').length === 1, 'dòng sang nhóm Đã xử lý');
    expect(document.activeElement).toBe(otherRowButton);
  });

  it('sau khi đã đưa tiêu điểm một lần, lần đọc lại sau không kéo tiêu điểm nữa', async () => {
    desk = openDesk(READ_AND_REPLY, {
      callbacks: [callbackRequest('1', 'pending'), callbackRequest('3', 'pending')],
    });
    await openCallbacksTab();
    await press(markButton('Đánh dấu đã gọi: 0900000001'));
    await waitFor(
      () => document.activeElement === groupHeading('Cần gọi lại'),
      'tiêu điểm về tiêu đề nhóm',
    );
    // Bỏ tiêu điểm khỏi tiêu đề. Tiêu điểm trống thì effect được phép kéo lại, nên chỉ mã đã xóa mới chặn được lần kéo thứ hai.
    await act(async () => (document.activeElement as HTMLElement).blur());
    expect(document.activeElement).toBe(document.body);

    desk.state.callbacks = [callbackRequest('4', 'pending'), ...desk.state.callbacks];
    await act(async () => activeEventSource().emit('callback.created', { callbackRequestId: '4' }));

    await waitFor(() => groupRows('Cần gọi lại').length === 2, 'danh sách đọc lại có dòng mới');
    expect(document.activeElement).toBe(document.body);
  });

  it('câu thông báo của lần đánh dấu trước được xóa khi bắt đầu lần kế tiếp', async () => {
    desk = openDesk(READ_AND_REPLY, {
      callbacks: [callbackRequest('1', 'pending'), callbackRequest('3', 'pending')],
    });
    await openCallbacksTab();
    await press(markButton('Đánh dấu đã gọi: 0900000001'));
    await waitFor(() => statusLine().textContent !== '', 'câu thông báo lần một');
    let releaseUpdate: () => void = () => {};
    desk.state.callbackUpdateGate = new Promise<void>((resolve) => {
      releaseUpdate = resolve;
    });

    await press(markButton('Đánh dấu đã gọi: 0900000003'));

    expect(statusLine().textContent).toBe('');
    await act(async () => releaseUpdate());
    await waitFor(() => statusLine().textContent !== '', 'câu thông báo lần hai');
  });

  it('đánh dấu thành công một dòng không xóa lỗi của dòng khác', async () => {
    desk = openDesk(READ_AND_REPLY, {
      callbacks: [callbackRequest('1', 'pending'), callbackRequest('3', 'pending')],
      callbackUpdateFailure: 'server',
    });
    await openCallbacksTab();
    await press(markButton('Đánh dấu đã gọi: 0900000001'));
    await waitFor(() => textOf(desk.harness.container).includes('Máy chủ đang bận.'), 'lỗi dòng 1');

    desk.state.callbackUpdateFailure = 'none';
    await press(markButton('Đánh dấu đã gọi: 0900000003'));

    await waitFor(() => groupRows('Đã xử lý').length === 1, 'dòng 3 sang nhóm Đã xử lý');
    expect(groupRows('Cần gọi lại')[0]).toContain('Máy chủ đang bận.');
  });

  it('thử lại sau lỗi thì xóa lỗi cũ của dòng', async () => {
    desk = openDesk(READ_AND_REPLY, {
      callbacks: [callbackRequest('1', 'pending')],
      callbackUpdateFailure: 'server',
    });
    await openCallbacksTab();
    await press(markButton('Đánh dấu đã gọi: 0900000001'));
    await waitFor(() => textOf(desk.harness.container).includes('Máy chủ đang bận.'), 'lỗi');

    desk.state.callbackUpdateFailure = 'none';
    await press(markButton('Đánh dấu đã gọi: 0900000001'));

    await waitFor(() => groupRows('Đã xử lý').length === 1, 'dòng sang nhóm Đã xử lý');
    expect(textOf(desk.harness.container)).not.toContain('Máy chủ đang bận.');
  });

  it('dòng role=status có mặt trong DOM ngay từ đầu và rỗng', async () => {
    desk = openDesk(READ_AND_REPLY, { callbacks: [callbackRequest('1', 'pending')] });

    await openCallbacksTab();

    expect(statusLine().getAttribute('role')).toBe('status');
    expect(statusLine().textContent).toBe('');
  });
});

describe('sự kiện callback.created', () => {
  it('đọc lại danh sách và hiện yêu cầu mới ngay trong tab, không tải lại trang', async () => {
    desk = openDesk(READ_ONLY, { callbacks: [callbackRequest('1', 'pending')] });
    await openCallbacksTab();
    expect(groupRows('Cần gọi lại')).toHaveLength(1);

    desk.state.callbacks = [callbackRequest('4', 'pending'), ...desk.state.callbacks];
    await act(async () => activeEventSource().emit('callback.created', { callbackRequestId: '4' }));

    await waitFor(() => groupRows('Cần gọi lại').length === 2, 'yêu cầu mới hiện trong tab');
    expect(groupRows('Cần gọi lại')[0]).toContain('0900000004');
  });

  it('sự kiện khác không đọc lại danh sách gọi lại', async () => {
    desk = openDesk(READ_ONLY, { callbacks: [callbackRequest('1', 'pending')] });
    await openCallbacksTab();
    const readsBefore = supportRequests(desk.harness).filter((url) =>
      url.endsWith('/callback-requests'),
    ).length;

    await act(async () => activeEventSource().emit('session.waiting', { sessionId: '1' }));

    expect(
      supportRequests(desk.harness).filter((url) => url.endsWith('/callback-requests')),
    ).toHaveLength(readsBefore);
  });
});
