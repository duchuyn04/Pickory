import { expect, test, type Page, type Route, type TestInfo } from '@playwright/test';
import { expectNoHorizontalOverflow } from './admin-order-fixtures';
import { collectConsoleIssues, dismissConsent, loginAdmin } from './catalog-admin-flow';
import { e2eEnvironment } from './environment';
import { readPasswordSetupLink } from './outbox';

// Thư mục scratchpad nhận ảnh chụp cho reviewer. Không đặt biến này thì ảnh nằm trong thư mục kết quả của Playwright.
const screenshotDirectory = process.env.COM_UI10_SHOTS_DIR;

const PAGE_SIZE = 2;

interface MockRow {
  id: string;
  productId: string;
  productName: string | null;
  rating: number;
  body: string | null;
  authorName: string;
  reportCount: number;
  hidden: boolean;
  hiddenReason: string | null;
  hiddenAt: string | null;
  createdAt: string;
  updatedAt: string;
}

interface MockError {
  status: number;
  code: string;
  message: string;
}

function buildRow(overrides: Partial<MockRow> & { id: string }): MockRow {
  return {
    productId: `product-${overrides.id}`,
    productName: `Chuột ${overrides.id}`,
    rating: 4,
    body: `Nhận xét ${overrides.id}`,
    authorName: `Người ${overrides.id}`,
    reportCount: 0,
    hidden: false,
    hiddenReason: null,
    hiddenAt: null,
    createdAt: '2026-10-08T03:30:00Z',
    updatedAt: '2026-10-08T03:30:00Z',
    ...overrides,
  };
}

// Backend giả của hai route kiểm duyệt. Dữ liệu đánh giá thật cần đơn `delivered` của COM-E2E4, nên spec này giả lập riêng hai route đó.
class AdminReviewsMock {
  rows: MockRow[];
  requests: { status: string | null; page: string | null }[] = [];
  patches: { id: string; body: unknown }[] = [];
  patchError: MockError | null = null;

  constructor(rows: MockRow[]) {
    this.rows = rows;
  }

  async install(page: Page) {
    await page.route(/\/api\/admin\/reviews(\/[^/?]+)?(\?.*)?$/, (route) => this.handle(route));
  }

  private async handle(route: Route) {
    const request = route.request();
    const url = new URL(request.url());

    if (request.method() === 'GET') {
      const status = url.searchParams.get('status');
      const pageNumber = Number(url.searchParams.get('page') ?? '1');
      this.requests.push({ status, page: url.searchParams.get('page') });
      const filtered = this.rows.filter((row) =>
        status === 'visible' ? !row.hidden : status === 'hidden' ? row.hidden : true,
      );
      const start = (pageNumber - 1) * PAGE_SIZE;
      return route.fulfill({
        json: {
          data: {
            items: filtered.slice(start, start + PAGE_SIZE),
            total: filtered.length,
            page: pageNumber,
            limit: PAGE_SIZE,
          },
        },
      });
    }

    if (request.method() === 'PATCH') {
      const id = url.pathname.split('/').at(-1)!;
      const body = request.postDataJSON() as { hidden: boolean; reason: string };
      this.patches.push({ id, body });
      if (this.patchError) {
        return route.fulfill({
          status: this.patchError.status,
          json: { error: { code: this.patchError.code, message: this.patchError.message } },
        });
      }
      const row = this.rows.find((candidate) => candidate.id === id)!;
      row.hidden = body.hidden;
      row.hiddenReason = body.hidden ? body.reason : null;
      row.hiddenAt = body.hidden ? new Date().toISOString() : null;
      return route.fulfill({ json: { data: row } });
    }

    return route.fallback();
  }
}

// Chromium ghi "Failed to load resource" cho mọi phản hồi 4xx. Chỉ bỏ qua đúng thông báo của URL đã giả lập lỗi.
function collectIssuesAllowingMockedFailure(
  page: Page,
  allowed: { status: number; urlPart: string },
): string[] {
  const issues: string[] = [];
  page.on('console', (message) => {
    if (message.location().url.endsWith('/favicon.ico')) return;
    if (message.type() !== 'error' && message.type() !== 'warning') return;
    const isMockedFailure =
      message.text().includes(`status of ${allowed.status}`) &&
      message.location().url.includes(allowed.urlPart);
    if (!isMockedFailure) issues.push(`${message.type()}: ${message.text()}`);
  });
  page.on('pageerror', (error) => issues.push(`pageerror: ${error.message}`));
  return issues;
}

async function savePageShot(page: Page, testInfo: TestInfo, name: string, fullPage: boolean) {
  const path = screenshotDirectory
    ? `${screenshotDirectory}/${name}.png`
    : testInfo.outputPath(`${name}.png`);
  await page.screenshot({ path, fullPage });
}

async function openAdminMenuIfCollapsed(page: Page) {
  const menuToggle = page.getByRole('button', { name: 'Menu', exact: true });
  const logoutButton = page.getByRole('button', { name: 'Đăng xuất', exact: true });
  await expect(menuToggle.or(logoutButton).first()).toBeVisible();
  if (!(await menuToggle.isVisible())) return;
  if ((await menuToggle.getAttribute('aria-expanded')) === 'true') return;
  await menuToggle.click();
}

// Tạo nhân viên `sales` bằng luồng mời thật, rồi đăng nhập. `sales` không có quyền `reviews:moderate`.
async function loginAsNewSales(page: Page, email: string) {
  await loginAdmin(page);
  await page.goto('/admin/staff');
  await page.getByRole('button', { name: '+ Mời nhân viên', exact: true }).click();
  const invitation = page.getByRole('dialog', { name: 'Mời nhân viên mới', exact: true });
  await invitation.getByLabel(/^Email nhân viên/).fill(email);
  await invitation.getByLabel(/^Họ và tên/).fill('Sales Đánh Giá E2E');
  await invitation.getByRole('checkbox', { name: /\(sales\)$/ }).check();
  await invitation.getByRole('button', { name: 'Gửi lời mời', exact: true }).click();
  await expect(invitation).toBeHidden();

  await openAdminMenuIfCollapsed(page);
  await page.getByRole('button', { name: 'Đăng xuất', exact: true }).click();
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/login`);

  await page.goto(await readPasswordSetupLink(email));
  await page
    .getByLabel('Mật khẩu mới (tối thiểu 8 ký tự)', { exact: true })
    .fill(e2eEnvironment.accountPassword!);
  await page
    .getByLabel('Xác nhận mật khẩu mới', { exact: true })
    .fill(e2eEnvironment.accountPassword!);
  await page.getByRole('button', { name: 'Cập nhật mật khẩu', exact: true }).click();
  await page.getByRole('link', { name: 'Đăng nhập trang quản trị', exact: true }).click();
  await page.getByLabel(/^Email/).fill(email);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.accountPassword!);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
}

function rowOf(page: Page, content: string) {
  return page.getByRole('row').filter({ hasText: content });
}

const HIDE_REASON_LABELS = [
  'Spam hoặc quảng cáo',
  'Ngôn từ xúc phạm',
  'Chứa thông tin cá nhân',
  'Không liên quan đến sản phẩm',
  'Sai sự thật, gây hiểu lầm',
];

test.describe('COM-UI10: hàng chờ kiểm duyệt đánh giá', () => {
  test('danh sách hiện số báo cáo, badge Ưu tiên, trạng thái, nhãn lý do và giữ tab, trang khi tải lại', async ({
    page,
  }) => {
    const consoleIssues = collectConsoleIssues(page);
    const mock = new AdminReviewsMock([
      buildRow({ id: 'a', reportCount: 3 }),
      buildRow({ id: 'b', reportCount: 2 }),
      buildRow({
        id: 'c',
        hidden: true,
        hiddenReason: 'off_topic',
        hiddenAt: '2026-10-09T03:30:00Z',
      }),
      buildRow({ id: 'd', productName: null }),
      buildRow({ id: 'e' }),
    ]);
    await mock.install(page);
    await loginAdmin(page);

    await page.goto('/admin/reviews');
    await expect(rowOf(page, 'Nhận xét a')).toContainText('Ưu tiên');
    await expect(rowOf(page, 'Nhận xét a')).toContainText('3');
    await expect(rowOf(page, 'Nhận xét b')).not.toContainText('Ưu tiên');
    await expect(rowOf(page, 'Nhận xét b')).toContainText('Đang hiện');
    await expect(
      rowOf(page, 'Nhận xét a').getByRole('img', { name: '4 trên 5 sao' }),
    ).toBeVisible();

    // Trang 2 chứa dòng đã ẩn và dòng mất sản phẩm.
    await page.getByRole('button', { name: 'Trang sau', exact: true }).click();
    await expect(page).toHaveURL(/page=2/);
    await expect(rowOf(page, 'Nhận xét c')).toContainText('Đã ẩn');
    await expect(rowOf(page, 'Nhận xét c')).toContainText('Không liên quan đến sản phẩm');
    await expect(rowOf(page, 'Nhận xét d')).toContainText('Sản phẩm không còn');
    expect(mock.requests.at(-1)).toEqual({ status: null, page: '2' });

    // Đổi tab về trang 1 và gửi đúng `status`.
    await page.getByRole('tab', { name: 'Đã ẩn', exact: true }).click();
    await expect(page).toHaveURL(/status=hidden/);
    await expect(page).not.toHaveURL(/page=/);
    await expect(rowOf(page, 'Nhận xét c')).toBeVisible();
    expect(mock.requests.at(-1)).toEqual({ status: 'hidden', page: null });

    // Tải lại trang giữ đúng tab.
    await page.reload();
    await expect(page.getByRole('tab', { name: 'Đã ẩn', exact: true })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    await expect(rowOf(page, 'Nhận xét c')).toBeVisible();

    // Tab Đang hiện gửi `status=visible`, và tải lại ở trang 2 giữ cả tab lẫn trang.
    await page.goto('/admin/reviews?status=visible&page=2');
    await expect(rowOf(page, 'Nhận xét e')).toBeVisible();
    expect(mock.requests.at(-1)).toEqual({ status: 'visible', page: '2' });
    expect(consoleIssues).toEqual([]);
  });

  test('ẩn: hộp thoại có đúng 5 lý do ẩn, chưa chọn thì báo lỗi và không gửi, chọn bằng bàn phím rồi gửi', async ({
    page,
  }) => {
    const consoleIssues = collectConsoleIssues(page);
    const mock = new AdminReviewsMock([buildRow({ id: 'a', reportCount: 4 })]);
    await mock.install(page);
    await loginAdmin(page);

    await page.goto('/admin/reviews');
    await page.getByRole('button', { name: /^Ẩn đánh giá của Người a/ }).click();
    const dialog = page.getByRole('dialog', { name: 'Ẩn đánh giá' });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByText('Nhận xét a')).toBeVisible();
    const group = dialog.getByRole('group', { name: 'Lý do' });
    await expect(group.getByRole('radio')).toHaveCount(5);
    for (const label of HIDE_REASON_LABELS) {
      await expect(group.getByRole('radio', { name: label })).toBeVisible();
    }

    // Chưa chọn lý do mà xác nhận: lỗi gắn vào nhóm radio và không có request.
    await dialog.getByRole('button', { name: 'Ẩn đánh giá', exact: true }).click();
    await expect(group).toHaveAccessibleDescription('Chọn lý do');
    expect(mock.patches).toEqual([]);

    // Focus đã nằm ở radio đầu khi mở hộp thoại. Mũi tên chọn lý do kế tiếp.
    await group.getByRole('radio', { name: 'Spam hoặc quảng cáo' }).focus();
    await page.keyboard.press('ArrowDown');
    await expect(group.getByRole('radio', { name: 'Ngôn từ xúc phạm' })).toBeChecked();
    await dialog.getByRole('button', { name: 'Ẩn đánh giá', exact: true }).click();

    await expect(dialog).toBeHidden();
    expect(mock.patches).toEqual([{ id: 'a', body: { hidden: true, reason: 'offensive' } }]);
    await expect(page.getByRole('status').filter({ hasText: 'Đã ẩn đánh giá.' })).toBeVisible();
    // Danh sách tải lại và dòng đổi sang đã ẩn.
    await expect(rowOf(page, 'Nhận xét a')).toContainText('Ngôn từ xúc phạm');
    expect(consoleIssues).toEqual([]);
  });

  test('hiện lại: hộp thoại có đúng 2 lý do hiện và PATCH có hidden false', async ({ page }) => {
    const consoleIssues = collectConsoleIssues(page);
    const mock = new AdminReviewsMock([
      buildRow({
        id: 'a',
        hidden: true,
        hiddenReason: 'spam',
        hiddenAt: '2026-10-09T03:30:00Z',
      }),
    ]);
    await mock.install(page);
    await loginAdmin(page);

    await page.goto('/admin/reviews');
    await page.getByRole('button', { name: /^Hiện lại đánh giá của Người a/ }).click();
    const dialog = page.getByRole('dialog', { name: 'Hiện lại đánh giá' });
    const group = dialog.getByRole('group', { name: 'Lý do' });
    await expect(group.getByRole('radio')).toHaveCount(2);
    await expect(group.getByRole('radio', { name: 'Báo cáo không đúng' })).toBeVisible();
    await expect(group.getByRole('radio', { name: 'Ẩn nhầm' })).toBeVisible();

    await group.getByRole('radio', { name: 'Ẩn nhầm' }).check();
    await dialog.getByRole('button', { name: 'Hiện lại đánh giá', exact: true }).click();

    await expect(dialog).toBeHidden();
    expect(mock.patches).toEqual([
      { id: 'a', body: { hidden: false, reason: 'hidden_by_mistake' } },
    ]);
    await expect(rowOf(page, 'Nhận xét a')).toContainText('Đang hiện');
    expect(consoleIssues).toEqual([]);
  });

  test('409 REVIEW_STATE_UNCHANGED hiện thông báo của server trong hộp thoại và tải lại danh sách', async ({
    page,
  }) => {
    const consoleIssues = collectIssuesAllowingMockedFailure(page, {
      status: 409,
      urlPart: '/api/admin/reviews/',
    });
    const mock = new AdminReviewsMock([buildRow({ id: 'a' })]);
    mock.patchError = {
      status: 409,
      code: 'REVIEW_STATE_UNCHANGED',
      message: 'Đánh giá đã ở trạng thái này.',
    };
    await mock.install(page);
    await loginAdmin(page);

    await page.goto('/admin/reviews');
    await page.getByRole('button', { name: /^Ẩn đánh giá của Người a/ }).click();
    const dialog = page.getByRole('dialog', { name: 'Ẩn đánh giá' });
    await dialog.getByRole('radio', { name: 'Spam hoặc quảng cáo' }).check();
    const requestsBefore = mock.requests.length;
    // Ở tab khác, đánh giá đã bị ẩn. Danh sách tải lại phải thấy trạng thái mới.
    mock.rows[0]!.hidden = true;
    mock.rows[0]!.hiddenReason = 'misleading';
    mock.rows[0]!.hiddenAt = '2026-10-09T03:30:00Z';
    await dialog.getByRole('button', { name: 'Ẩn đánh giá', exact: true }).click();

    await expect(dialog.getByRole('alert')).toContainText('Đánh giá đã ở trạng thái này.');
    await expect(dialog).toBeVisible();
    await expect.poll(() => mock.requests.length).toBeGreaterThan(requestsBefore);
    await expect(rowOf(page, 'Nhận xét a')).toContainText('Sai sự thật, gây hiểu lầm');
    expect(consoleIssues).toEqual([]);
  });

  test('nhận xét chứa HTML hiện nguyên văn, không dựng phần tử và không bật hộp thoại alert', async ({
    page,
  }) => {
    const consoleIssues = collectConsoleIssues(page);
    const dialogs: string[] = [];
    page.on('dialog', async (dialog) => {
      dialogs.push(dialog.message());
      await dialog.dismiss();
    });
    const mock = new AdminReviewsMock([
      buildRow({ id: 'x', body: '<img src=x onerror=alert(1)>\n<b>đậm</b>' }),
    ]);
    await mock.install(page);
    await loginAdmin(page);

    await page.goto('/admin/reviews');
    await expect(page.getByText('<img src=x onerror=alert(1)>', { exact: false })).toBeVisible();
    await expect(page.getByText('<b>đậm</b>', { exact: false })).toBeVisible();
    await expect(page.locator('img[src="x"]')).toHaveCount(0);
    await expect(page.locator('tbody b')).toHaveCount(0);
    expect(dialogs).toEqual([]);
    expect(consoleIssues).toEqual([]);
  });

  test('danh sách rỗng của tab và lỗi tải có nút Thử lại', async ({ page }) => {
    const consoleIssues = collectIssuesAllowingMockedFailure(page, {
      status: 500,
      urlPart: '/api/admin/reviews',
    });
    const mock = new AdminReviewsMock([buildRow({ id: 'a' })]);
    await mock.install(page);
    await loginAdmin(page);

    await page.goto('/admin/reviews?status=hidden');
    await expect(page.getByText('Không có đánh giá nào đã ẩn.')).toBeVisible();

    // Lần tải kế tiếp lỗi 500 rồi phục hồi. Truy vấn thử lại ba lần trước khi báo lỗi, nên chờ lâu hơn mặc định.
    await page.unroute(/\/api\/admin\/reviews(\/[^/?]+)?(\?.*)?$/);
    let failing = true;
    await page.route(/\/api\/admin\/reviews(\/[^/?]+)?(\?.*)?$/, (route) =>
      failing
        ? route.fulfill({
            status: 500,
            json: { error: { code: 'INTERNAL', message: 'Máy chủ gặp lỗi.' } },
          })
        : route.fulfill({
            json: { data: { items: [buildRow({ id: 'a' })], total: 1, page: 1, limit: PAGE_SIZE } },
          }),
    );
    await page.goto('/admin/reviews');
    await expect(page.getByRole('alert').filter({ hasText: 'Máy chủ gặp lỗi.' })).toBeVisible({
      timeout: 20000,
    });
    failing = false;
    await page.getByRole('button', { name: 'Thử lại', exact: true }).click();
    await expect(rowOf(page, 'Nhận xét a')).toBeVisible();
    expect(consoleIssues).toEqual([]);
  });

  test('nhân viên sales không có reviews:moderate thấy ForbiddenNotice, không có mục menu và không gọi API', async ({
    page,
  }) => {
    test.setTimeout(120000);
    const consoleIssues = collectConsoleIssues(page);
    const mock = new AdminReviewsMock([buildRow({ id: 'a' })]);
    await mock.install(page);
    await loginAsNewSales(page, e2eEnvironment.reviewsSalesEmail);

    await page.goto('/admin/reviews');
    await expect(page.getByRole('alert').filter({ hasText: 'reviews:moderate' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Đánh giá', exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: /^Ẩn đánh giá/ })).toHaveCount(0);
    expect(mock.requests).toEqual([]);
    expect(mock.patches).toEqual([]);
    expect(consoleIssues).toEqual([]);
  });

  test('admin thấy mục Đánh giá trong menu và mở được trang', async ({ page }) => {
    const consoleIssues = collectConsoleIssues(page);
    const mock = new AdminReviewsMock([buildRow({ id: 'a' })]);
    await mock.install(page);
    await loginAdmin(page);

    await page.getByRole('link', { name: 'Đánh giá', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/reviews`);
    await expect(page.getByRole('heading', { name: 'Kiểm duyệt đánh giá' })).toBeVisible();
    expect(consoleIssues).toEqual([]);
  });

  test('danh sách và hộp thoại không tràn ngang ở 1280 và 375', async ({ page }, testInfo) => {
    const consoleIssues = collectConsoleIssues(page);
    const mock = new AdminReviewsMock([
      buildRow({
        id: 'a',
        reportCount: 5,
        body: 'z'.repeat(250),
        authorName: 'Nguyễn Văn Rất Dài T.',
      }),
      buildRow({
        id: 'b',
        hidden: true,
        hiddenReason: 'personal_info',
        hiddenAt: '2026-10-09T03:30:00Z',
      }),
    ]);
    await mock.install(page);
    await loginAdmin(page);
    await dismissConsent(page);

    for (const width of [1280, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await page.goto('/admin/reviews');
      await expect(rowOf(page, 'Nhận xét b')).toContainText('Chứa thông tin cá nhân');
      await expectNoHorizontalOverflow(page);
      await savePageShot(page, testInfo, `COM-UI10-list-${width}`, true);

      await page.getByRole('button', { name: /^Ẩn đánh giá của Nguyễn Văn Rất Dài T./ }).click();
      await expect(page.getByRole('dialog', { name: 'Ẩn đánh giá' })).toBeVisible();
      await page.getByRole('radio', { name: 'Spam hoặc quảng cáo' }).check();
      await expectNoHorizontalOverflow(page);
      await savePageShot(page, testInfo, `COM-UI10-dialog-${width}`, false);
      await page.keyboard.press('Escape');
      await expect(page.getByRole('dialog')).toBeHidden();
    }
    expect(consoleIssues).toEqual([]);
  });
});
