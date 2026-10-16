// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import {
  actorWith,
  clickButton,
  findButton,
  queryOf,
  createHarness,
  waitFor,
  type AdminListHarness,
} from '../../../identity/admin/admin-list-test-kit';

let harness: AdminListHarness;

afterEach(async () => {
  await harness.cleanup();
});

const REVIEWS_PATH = '/api/admin/reviews';

function review(id: string, overrides: Record<string, unknown> = {}) {
  return {
    id,
    productId: `product-${id}`,
    productName: `Chuột ${id}`,
    rating: 4,
    body: `Nhận xét ${id}`,
    authorName: `Người ${id}`,
    reportCount: 0,
    hidden: false,
    hiddenReason: null,
    hiddenAt: null,
    createdAt: '2026-10-08T03:30:00Z',
    updatedAt: '2026-10-08T03:30:00Z',
    ...overrides,
  };
}

function hiddenReview(id: string) {
  return review(id, {
    hidden: true,
    hiddenReason: 'spam',
    hiddenAt: '2026-10-09T03:30:00Z',
  });
}

function page(items: unknown[], total = items.length) {
  return { items, total, page: 1, limit: 20 };
}

interface Scenario {
  url?: string;
  permissions?: string[];
  reviews?: unknown[];
  // Response cho `PATCH`. Mặc định trả dòng đã ẩn.
  onPatch?: (init?: RequestInit) => unknown;
}

const MODERATOR = ['reviews:moderate'];

function open(scenario: Scenario = {}) {
  const {
    url = '/admin/reviews',
    permissions = MODERATOR,
    reviews = [review('a')],
    onPatch,
  } = scenario;
  harness = createHarness(actorWith(permissions), (requested, init) => {
    const method = init?.method ?? 'GET';
    const path = new URL(requested, 'http://localhost').pathname;
    if (path === REVIEWS_PATH && method === 'GET') return page(reviews);
    if (path.startsWith(`${REVIEWS_PATH}/`) && method === 'PATCH') {
      return onPatch ? onPatch(init) : hiddenReview('a');
    }
    return [];
  });
  return harness.render(url);
}

function text() {
  return harness.container.textContent ?? '';
}

function reviewCalls() {
  return harness.requestedUrls.filter((url) => url.startsWith(REVIEWS_PATH));
}

function tab(label: string) {
  return [...harness.container.querySelectorAll<HTMLButtonElement>('[role="tab"]')].find(
    (element) => element.textContent === label,
  );
}

function dialog() {
  return document.querySelector<HTMLElement>('[role="dialog"]');
}

function radioLabels() {
  return [...(dialog()?.querySelectorAll('input[type="radio"]') ?? [])].map(
    (input) => input.closest('label')?.textContent ?? '',
  );
}

async function openRowDialog(buttonText: string) {
  const button = [...harness.container.querySelectorAll('button')].find(
    (element) => element.textContent === buttonText,
  );
  if (!button) throw new Error(`Thiếu nút ${buttonText}.`);
  await act(async () => button.click());
}

async function chooseReason(label: string) {
  const radio = [
    ...(dialog()?.querySelectorAll<HTMLInputElement>('input[type="radio"]') ?? []),
  ].find((input) => input.closest('label')?.textContent === label);
  if (!radio) throw new Error(`Thiếu lý do ${label}.`);
  await act(async () => radio.click());
}

async function submitDialog(label: string) {
  const button = [...(dialog()?.querySelectorAll('button') ?? [])].find(
    (element) => element.textContent === label,
  );
  if (!button) throw new Error(`Thiếu nút ${label}.`);
  await act(async () => button.click());
}

describe('AdminReviewsPage', () => {
  it('không có reviews:moderate thì hiện ForbiddenNotice, không gọi API và không có mục menu', async () => {
    await open({ permissions: ['orders:read'] });

    await waitFor(() => text().includes('403'), 'thông báo thiếu quyền');
    expect(text()).toContain('reviews:moderate');
    expect(reviewCalls()).toEqual([]);
    const menuLinks = [...harness.container.querySelectorAll('nav a')].map((a) => a.textContent);
    expect(menuLinks).not.toContain('Đánh giá');
  });

  it('có quyền thì thấy mục Đánh giá trong menu và gọi API hàng chờ', async () => {
    await open();

    await waitFor(() => text().includes('Nhận xét a'), 'danh sách đánh giá');
    const menuLinks = [...harness.container.querySelectorAll('nav a')].map((a) => a.textContent);
    expect(menuLinks).toContain('Đánh giá');
    expect(reviewCalls()).toEqual([REVIEWS_PATH]);
  });

  it('hiện số báo cáo, badge Ưu tiên từ 3 báo cáo, trạng thái và nhãn lý do ẩn', async () => {
    await open({
      reviews: [
        review('two', { reportCount: 2 }),
        review('three', { reportCount: 3 }),
        hiddenReview('gone'),
        review('orphan', { productName: null }),
      ],
    });

    await waitFor(() => text().includes('Nhận xét two'), 'danh sách đánh giá');
    const rows = [...harness.container.querySelectorAll('tbody tr')];
    const rowOf = (content: string) => rows.find((row) => row.textContent?.includes(content))!;
    expect(rowOf('Nhận xét two').textContent).not.toContain('Ưu tiên');
    expect(rowOf('Nhận xét three').textContent).toContain('Ưu tiên');
    expect(rowOf('Nhận xét three').textContent).toContain('3');
    expect(rowOf('Nhận xét gone').textContent).toContain('Đã ẩn');
    expect(rowOf('Nhận xét gone').textContent).toContain('Spam hoặc quảng cáo');
    expect(rowOf('Nhận xét two').textContent).toContain('Đang hiện');
    expect(rowOf('Nhận xét orphan').textContent).toContain('Sản phẩm không còn');
    expect(rowOf('Nhận xét two').querySelector('[role="img"]')?.getAttribute('aria-label')).toBe(
      '4 trên 5 sao',
    );
  });

  it('nhận xét chứa HTML hiện nguyên văn, không dựng phần tử', async () => {
    await open({ reviews: [review('x', { body: '<img src=x onerror=alert(1)><b>đậm</b>' })] });

    await waitFor(() => text().includes('onerror=alert(1)'), 'nhận xét');
    expect(harness.container.querySelector('tbody img')).toBeNull();
    expect(harness.container.querySelector('tbody b')).toBeNull();
    expect(text()).toContain('<b>đậm</b>');
  });

  it('đổi tab gửi đúng status, và URL ban đầu giữ tab cùng trang', async () => {
    await open({ url: '/admin/reviews?status=hidden&page=2' });

    await waitFor(() => reviewCalls().length > 0, 'lần tải đầu');
    expect(queryOf(reviewCalls()[0]!)).toEqual({ status: 'hidden', page: '2' });
    expect(tab('Đã ẩn')?.getAttribute('aria-selected')).toBe('true');

    await act(async () => tab('Đang hiện')!.click());
    await waitFor(
      () => reviewCalls().some((url) => queryOf(url).status === 'visible'),
      'tab Đang hiện',
    );
    expect(queryOf(reviewCalls().at(-1)!)).toEqual({ status: 'visible' });
  });

  it('mỗi tab có trạng thái rỗng riêng', async () => {
    await open({ url: '/admin/reviews?status=hidden', reviews: [] });

    await waitFor(() => text().includes('Không có đánh giá nào đã ẩn.'), 'trạng thái rỗng');
  });

  it('ẩn: hộp thoại chỉ có 5 lý do ẩn, chưa chọn mà xác nhận thì báo Chọn lý do và không gửi', async () => {
    await open();
    await waitFor(() => text().includes('Nhận xét a'), 'danh sách đánh giá');

    await openRowDialog('Ẩn');
    expect(radioLabels()).toEqual([
      'Spam hoặc quảng cáo',
      'Ngôn từ xúc phạm',
      'Chứa thông tin cá nhân',
      'Không liên quan đến sản phẩm',
      'Sai sự thật, gây hiểu lầm',
    ]);
    // Hộp thoại trích nhận xét để nhân viên biết đang thao tác trên đánh giá nào.
    expect(dialog()?.textContent).toContain('Nhận xét a');

    await submitDialog('Ẩn đánh giá');
    expect(dialog()?.textContent).toContain('Chọn lý do');
    expect(harness.writes).toEqual([]);
  });

  it('ẩn: chọn lý do rồi xác nhận gửi PATCH đúng body và tải lại danh sách', async () => {
    await open();
    await waitFor(() => text().includes('Nhận xét a'), 'danh sách đánh giá');
    const loadsBefore = reviewCalls().length;

    await openRowDialog('Ẩn');
    await chooseReason('Ngôn từ xúc phạm');
    await submitDialog('Ẩn đánh giá');

    await waitFor(() => harness.writes.length === 1, 'request PATCH');
    expect(harness.writes[0]).toEqual({
      method: 'PATCH',
      url: '/api/admin/reviews/a',
      body: { hidden: true, reason: 'offensive' },
    });
    await waitFor(() => dialog() === null, 'hộp thoại đóng');
    await waitFor(() => reviewCalls().length > loadsBefore, 'danh sách tải lại');
    expect(harness.container.querySelector('[role="status"]')?.textContent).toContain('Đã ẩn');
  });

  it('hiện lại: hộp thoại chỉ có 2 lý do hiện và PATCH có hidden false', async () => {
    await open({ reviews: [hiddenReview('a')] });
    await waitFor(() => text().includes('Nhận xét a'), 'danh sách đánh giá');

    await openRowDialog('Hiện lại');
    expect(radioLabels()).toEqual(['Báo cáo không đúng', 'Ẩn nhầm']);
    await chooseReason('Ẩn nhầm');
    await submitDialog('Hiện lại đánh giá');

    await waitFor(() => harness.writes.length === 1, 'request PATCH');
    expect(harness.writes[0]?.body).toEqual({ hidden: false, reason: 'hidden_by_mistake' });
  });

  it('409 REVIEW_STATE_UNCHANGED hiện thông báo của server trong hộp thoại và tải lại danh sách', async () => {
    await open({
      onPatch: () =>
        new Response(
          JSON.stringify({
            error: { code: 'REVIEW_STATE_UNCHANGED', message: 'Đánh giá đã ở trạng thái này.' },
          }),
          { status: 409 },
        ),
    });
    await waitFor(() => text().includes('Nhận xét a'), 'danh sách đánh giá');
    const loadsBefore = reviewCalls().length;

    await openRowDialog('Ẩn');
    await chooseReason('Spam hoặc quảng cáo');
    await submitDialog('Ẩn đánh giá');

    await waitFor(
      () => dialog()?.textContent?.includes('Đánh giá đã ở trạng thái này.') ?? false,
      'thông báo 409',
    );
    await waitFor(() => reviewCalls().length > loadsBefore, 'danh sách tải lại sau 409');
    expect(findButton(document.body, 'Ẩn đánh giá')).toBeDefined();
  });

  it('lỗi tải danh sách hiện thông báo và nút Thử lại', async () => {
    harness = createHarness(
      actorWith(MODERATOR),
      () =>
        new Response(JSON.stringify({ error: { code: 'INTERNAL', message: 'Máy chủ gặp lỗi.' } }), {
          status: 500,
        }),
    );
    await harness.render('/admin/reviews');

    await waitFor(() => text().includes('Máy chủ gặp lỗi.'), 'thông báo lỗi tải');
    const before = reviewCalls().length;
    await clickButton(harness.container, 'Thử lại');
    await waitFor(() => reviewCalls().length > before, 'tải lại sau Thử lại');
  });
});
