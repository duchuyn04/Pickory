import { expect, test, type Page, type Route, type TestInfo } from '@playwright/test';
import {
  expectNoHorizontalOverflow,
  seedProduct,
  type SeededProduct,
} from './admin-order-fixtures';
import { collectConsoleIssues, loginAdmin, tinyPng } from './catalog-admin-flow';
import { e2eEnvironment } from './environment';

// Thư mục scratchpad nhận ảnh chụp cho reviewer. Không đặt biến này thì ảnh nằm trong thư mục kết quả của Playwright.
const screenshotDirectory = process.env.COM_UI9_SHOTS_DIR;

const DAY_MS = 24 * 60 * 60 * 1000;

interface MockReview {
  id: string;
  rating: number;
  body: string | null;
  authorName: string;
  createdAt: string;
  updatedAt: string;
}

interface MockMine {
  eligible: boolean;
  reason: 'NOT_PURCHASED' | 'ALREADY_REVIEWED' | null;
  review: {
    id: string;
    rating: number;
    body: string | null;
    hidden: boolean;
    createdAt: string;
    updatedAt: string;
    editableUntil: string;
  } | null;
}

interface MockError {
  status: number;
  code: string;
  message: string;
  details?: { key: string; message: string }[];
}

function errorBody(error: MockError) {
  return { error: { code: error.code, message: error.message, details: error.details } };
}

// Backend giả của các route đánh giá. Đơn `delivered` thật thuộc COM-E2E4, nên spec này giả lập riêng các route đó.
class ReviewsApiMock {
  reviews: MockReview[];
  mine: MockMine;
  postBodies: unknown[] = [];
  patchBodies: unknown[] = [];
  reportedIds: string[] = [];
  mineRequestCount = 0;
  // Lỗi giả lập cho từng nhóm route. Đặt `null` để route trả lại kết quả thành công.
  listError: MockError | null = null;
  writeError: MockError | null = null;
  reportError: MockError | null = null;
  // Danh sách chia trang. Trang đầu trả `nextCursor`, các trang sau tra theo `cursor` của request.
  pages: { cursor: string; items: MockReview[]; nextCursor: string | null }[] | null = null;
  failCursor: string | null = null;
  listCursors: (string | null)[] = [];

  constructor(reviews: MockReview[], mine: MockMine) {
    this.reviews = reviews;
    this.mine = mine;
  }

  private summary() {
    const distribution: Record<string, number> = { '1': 0, '2': 0, '3': 0, '4': 0, '5': 0 };
    for (const review of this.reviews)
      distribution[String(review.rating)] = (distribution[String(review.rating)] ?? 0) + 1;
    const count = this.reviews.length;
    const total = this.reviews.reduce((sum, review) => sum + review.rating, 0);
    return { average: count === 0 ? null : total / count, count, distribution };
  }

  async install(page: Page) {
    await page.route(/\/api\/products\/[^/?]+\/reviews(\/mine)?(\?.*)?$/, (route) =>
      this.handleProductRoute(route),
    );
    await page.route(/\/api\/reviews\/[^/?]+\/reports$/, (route) => this.handleReport(route));
  }

  private async handleProductRoute(route: Route) {
    const request = route.request();
    const isMine = new URL(request.url()).pathname.endsWith('/mine');
    const method = request.method();

    if (!isMine && method === 'GET') {
      const cursor = new URL(request.url()).searchParams.get('cursor');
      this.listCursors.push(cursor);
      if (this.listError) {
        return route.fulfill({ status: this.listError.status, json: errorBody(this.listError) });
      }
      if (cursor !== null && cursor === this.failCursor) {
        return route.fulfill({
          status: 500,
          json: errorBody({ status: 500, code: 'INTERNAL', message: 'Không tải được trang sau.' }),
        });
      }
      if (this.pages) {
        const page = this.pages.find((candidate) => candidate.cursor === (cursor ?? ''));
        return route.fulfill({
          json: {
            data: {
              summary: this.summary(),
              items: page?.items ?? [],
              nextCursor: page?.nextCursor ?? null,
            },
          },
        });
      }
      return route.fulfill({
        json: { data: { summary: this.summary(), items: this.reviews, nextCursor: null } },
      });
    }
    if (isMine && method === 'GET') {
      this.mineRequestCount += 1;
      return route.fulfill({ json: { data: this.mine } });
    }
    if (!isMine && method === 'POST' && this.writeError) {
      this.postBodies.push(request.postDataJSON());
      return route.fulfill({ status: this.writeError.status, json: errorBody(this.writeError) });
    }
    if (isMine && method === 'PATCH' && this.writeError) {
      this.patchBodies.push(request.postDataJSON());
      return route.fulfill({ status: this.writeError.status, json: errorBody(this.writeError) });
    }
    if (!isMine && method === 'POST') {
      const body = request.postDataJSON() as { rating: number; body: string | null };
      this.postBodies.push(body);
      const now = new Date().toISOString();
      const created = {
        id: 'review-mine',
        rating: body.rating,
        body: body.body,
        hidden: false,
        createdAt: now,
        updatedAt: now,
        editableUntil: new Date(Date.now() + 30 * DAY_MS).toISOString(),
      };
      const { id, rating, body: text, createdAt, updatedAt } = created;
      this.reviews = [
        { id, rating, body: text, createdAt, updatedAt, authorName: 'Khách E2E' },
        ...this.reviews,
      ];
      this.mine = { eligible: false, reason: 'ALREADY_REVIEWED', review: created };
      return route.fulfill({ status: 201, json: { data: created } });
    }
    if (isMine && method === 'PATCH') {
      this.patchBodies.push(request.postDataJSON());
      return route.fulfill({ json: { data: this.mine.review } });
    }
    return route.fallback();
  }

  private async handleReport(route: Route) {
    const reviewId = route.request().url().split('/').at(-2)!;
    this.reportedIds.push(reviewId);
    if (this.reportError) {
      return route.fulfill({ status: this.reportError.status, json: errorBody(this.reportError) });
    }
    return route.fulfill({
      status: 201,
      json: { data: { reviewId, createdAt: new Date().toISOString() } },
    });
  }
}

function buildReview(overrides: Partial<MockReview> & { id: string }): MockReview {
  return {
    rating: 5,
    body: 'Sản phẩm tốt.',
    authorName: 'Trần A.',
    createdAt: '2026-10-01T03:00:00Z',
    updatedAt: '2026-10-01T03:00:00Z',
    ...overrides,
  };
}

const defaultReviews = [
  buildReview({ id: 'review-a', authorName: 'Trần A.' }),
  buildReview({ id: 'review-b', authorName: 'Lê B.', body: 'Dùng êm tay.' }),
];

const notPurchased: MockMine = { eligible: false, reason: 'NOT_PURCHASED', review: null };
const eligible: MockMine = { eligible: true, reason: null, review: null };

function alreadyReviewed(editableUntil: Date, hidden = false): MockMine {
  return {
    eligible: false,
    reason: 'ALREADY_REVIEWED',
    review: {
      id: 'review-mine',
      rating: 3,
      body: 'Tạm ổn.',
      hidden,
      createdAt: '2026-10-02T03:00:00Z',
      updatedAt: '2026-10-02T03:00:00Z',
      editableUntil: editableUntil.toISOString(),
    },
  };
}

// Chromium ghi "Failed to load resource" cho mọi phản hồi 4xx và 5xx. Chỉ bỏ qua đúng thông báo của URL đã giả lập lỗi,
// các lỗi và cảnh báo khác vẫn được ghi lại.
function collectConsoleIssuesAllowingMockedFailures(
  page: Page,
  allowed: { status: number; urlPart: string }[],
): string[] {
  const issues: string[] = [];
  page.on('console', (message) => {
    if (message.location().url.endsWith('/favicon.ico')) return;
    if (message.type() !== 'error' && message.type() !== 'warning') return;
    const isMockedFailure = allowed.some(
      (failure) =>
        message.text().includes(`status of ${failure.status}`) &&
        message.location().url.includes(failure.urlPart),
    );
    if (isMockedFailure) return;
    issues.push(`${message.type()}: ${message.text()}`);
  });
  page.on('pageerror', (error) => issues.push(`pageerror: ${error.message}`));
  return issues;
}

async function dismissConsent(page: Page) {
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết', exact: true });
  if (await consent.isVisible()) await consent.click();
}

async function savePageShot(page: Page, testInfo: TestInfo, name: string, fullPage = true) {
  const path = screenshotDirectory
    ? `${screenshotDirectory}/${name}.png`
    : testInfo.outputPath(`${name}.png`);
  await page.screenshot({ path, fullPage });
}

async function loginCustomer(page: Page) {
  await page.goto('/login');
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết', exact: true });
  if (await consent.isVisible()) await consent.click();
  await page.getByLabel(/^Email/).fill(e2eEnvironment.screensCustomerEmail);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.accountPassword!);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Tài khoản', exact: true })).toBeVisible();
}

async function prepare(page: Page, mock: ReviewsApiMock) {
  await page.route('https://res.cloudinary.com/**', (route) =>
    route.fulfill({ body: tinyPng, contentType: 'image/png' }),
  );
  await mock.install(page);
}

function reviewsRegion(page: Page) {
  return page.getByRole('region', { name: 'Đánh giá sản phẩm' });
}

test.describe('COM-UI9: khối đánh giá trên trang sản phẩm', () => {
  let product: SeededProduct;

  test.beforeAll(async ({ browser }) => {
    const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    const adminPage = await adminContext.newPage();
    await loginAdmin(adminPage);
    product = await seedProduct(adminPage);
    await adminContext.close();
  });

  test('khách vãng lai thấy lý do và link đăng nhập, không thấy form và không gọi /mine', async ({
    page,
  }) => {
    const consoleIssues = collectConsoleIssues(page);
    const mock = new ReviewsApiMock(defaultReviews, notPurchased);
    await prepare(page, mock);

    await page.goto(`/p/${product.slug}`);
    const region = reviewsRegion(page);
    await expect(region.getByText('Trần A.')).toBeVisible();

    const signIn = region.getByRole('link', { name: 'Đăng nhập để viết đánh giá' });
    await expect(signIn).toHaveAttribute('href', '/login');
    await expect(region.getByRole('radio')).toHaveCount(0);
    await expect(region.getByRole('button', { name: 'Báo cáo' })).toHaveCount(0);
    expect(mock.mineRequestCount).toBe(0);
    expect(consoleIssues).toEqual([]);
  });

  test('khách chưa nhận hàng thấy lý do, không thấy form', async ({ page }) => {
    const consoleIssues = collectConsoleIssues(page);
    const mock = new ReviewsApiMock(defaultReviews, notPurchased);
    await prepare(page, mock);
    await loginCustomer(page);

    await page.goto(`/p/${product.slug}`);
    const region = reviewsRegion(page);
    await expect(
      region.getByText('Chỉ khách đã nhận sản phẩm này mới viết được đánh giá.'),
    ).toBeVisible();
    await expect(region.getByRole('radio')).toHaveCount(0);
    await expect(region.getByRole('textbox')).toHaveCount(0);
    expect(consoleIssues).toEqual([]);
  });

  test('khách đủ điều kiện viết đánh giá chỉ bằng bàn phím, tóm tắt đổi theo phản hồi mới', async ({
    page,
  }) => {
    const consoleIssues = collectConsoleIssues(page);
    const mock = new ReviewsApiMock(defaultReviews, eligible);
    await prepare(page, mock);
    await loginCustomer(page);

    await page.goto(`/p/${product.slug}`);
    const region = reviewsRegion(page);
    await expect(region.getByText('5,0')).toBeVisible();

    // Bấm vào tiêu đề để đặt điểm bắt đầu của phím Tab ngay trước nhóm sao.
    await region.getByRole('heading', { name: 'Viết đánh giá của bạn' }).click();
    await page.keyboard.press('Tab');
    await expect(region.getByRole('radio', { name: '1 sao' })).toBeFocused();
    for (let step = 0; step < 3; step += 1) await page.keyboard.press('ArrowRight');
    await expect(region.getByRole('radio', { name: '4 sao' })).toBeChecked();

    await page.keyboard.press('Tab');
    await expect(region.getByLabel(/^Nhận xét/)).toBeFocused();
    await page.keyboard.type('Màn hình đẹp.');
    await page.keyboard.press('Tab');
    const submit = region.getByRole('button', { name: 'Gửi đánh giá' });
    await expect(submit).toBeFocused();
    await page.keyboard.press('Enter');

    await expect(region.getByRole('heading', { name: 'Đánh giá của bạn' })).toBeVisible();
    expect(mock.postBodies).toEqual([{ rating: 4, body: 'Màn hình đẹp.' }]);
    // Điểm trung bình mới là (5 + 5 + 4) / 3, lấy từ phản hồi GET sau khi gửi.
    await expect(region.getByText('4,7')).toBeVisible();
    await expect(region.getByText('3 đánh giá')).toBeVisible();
    expect(consoleIssues).toEqual([]);
  });

  test('thiếu số sao thì báo lỗi gắn vào nhóm sao và đưa focus về radio đầu', async ({ page }) => {
    const consoleIssues = collectConsoleIssues(page);
    const mock = new ReviewsApiMock(defaultReviews, eligible);
    await prepare(page, mock);
    await loginCustomer(page);

    await page.goto(`/p/${product.slug}`);
    const region = reviewsRegion(page);
    await region.getByRole('button', { name: 'Gửi đánh giá' }).click();

    const group = region.getByRole('group', { name: 'Số sao' });
    await expect(group).toHaveAccessibleDescription('Chọn số sao');
    await expect(region.getByRole('radio', { name: '1 sao' })).toBeFocused();
    expect(mock.postBodies).toEqual([]);
    expect(consoleIssues).toEqual([]);
  });

  test('nhận xét chứa HTML hiện nguyên văn, không dựng phần tử và không bật hộp thoại alert', async ({
    page,
  }, testInfo) => {
    const consoleIssues = collectConsoleIssues(page);
    const dialogs: string[] = [];
    page.on('dialog', async (dialog) => {
      dialogs.push(dialog.message());
      await dialog.dismiss();
    });
    const hostileBody = '<img src=x onerror=alert(1)>\n<b>đậm</b>';
    const longWord = 'x'.repeat(300);
    const mock = new ReviewsApiMock(
      [
        buildReview({ id: 'review-hostile', body: hostileBody, authorName: '<i>Kẻ phá</i>' }),
        buildReview({ id: 'review-long', body: longWord }),
        ...defaultReviews,
      ],
      notPurchased,
    );
    await prepare(page, mock);

    await page.goto(`/p/${product.slug}`);
    const region = reviewsRegion(page);
    // Banner đồng ý cookie dính dưới màn hình sẽ che ảnh chụp, nên đóng trước.
    const consent = page.getByRole('button', { name: 'Chỉ cần thiết', exact: true });
    if (await consent.isVisible()) await consent.click();
    await expect(region.getByText('<img src=x onerror=alert(1)>', { exact: false })).toBeVisible();
    await expect(region.getByText('<b>đậm</b>', { exact: false })).toBeVisible();
    await expect(page.locator('img[src="x"]')).toHaveCount(0);
    await expect(region.locator('b')).toHaveCount(0);
    expect(dialogs).toEqual([]);

    // Header dính đầu trang che phần đầu khối trong ảnh chụp, nên bỏ dính trước khi chụp.
    await page.addStyleTag({ content: '.site-header { position: static !important; }' });
    for (const width of [1280, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await region.scrollIntoViewIfNeeded();
      await expectNoHorizontalOverflow(page);
      await savePageShot(page, testInfo, `COM-UI9-${width}`);
    }
    expect(consoleIssues).toEqual([]);
  });

  test('khách đã đánh giá còn hạn thấy nút sửa, sửa thì gửi PATCH', async ({ page }) => {
    const consoleIssues = collectConsoleIssues(page);
    const mock = new ReviewsApiMock(
      defaultReviews,
      alreadyReviewed(new Date(Date.now() + 10 * DAY_MS), true),
    );
    await prepare(page, mock);
    await loginCustomer(page);

    await page.goto(`/p/${product.slug}`);
    const region = reviewsRegion(page);
    await expect(region.getByText('Đánh giá của bạn đang bị ẩn.')).toBeVisible();
    await region.getByRole('button', { name: 'Sửa đánh giá' }).click();
    await expect(region.getByRole('radio', { name: '3 sao' })).toBeChecked();
    await expect(region.getByLabel(/^Nhận xét/)).toHaveValue('Tạm ổn.');
    await region.getByRole('radio', { name: '5 sao' }).check();
    await region.getByRole('button', { name: 'Lưu đánh giá' }).click();

    await expect(region.getByRole('button', { name: 'Sửa đánh giá' })).toBeVisible();
    expect(mock.patchBodies).toEqual([{ rating: 5, body: 'Tạm ổn.' }]);
    expect(consoleIssues).toEqual([]);
  });

  test('khách đã đánh giá hết hạn sửa thấy lý do, không thấy nút sửa', async ({ page }) => {
    const consoleIssues = collectConsoleIssues(page);
    const mock = new ReviewsApiMock(defaultReviews, alreadyReviewed(new Date(Date.now() - DAY_MS)));
    await prepare(page, mock);
    await loginCustomer(page);

    await page.goto(`/p/${product.slug}`);
    const region = reviewsRegion(page);
    await expect(region.getByText('Đã quá 30 ngày, không sửa được đánh giá.')).toBeVisible();
    await expect(region.getByRole('button', { name: 'Sửa đánh giá' })).toHaveCount(0);
    expect(consoleIssues).toEqual([]);
  });

  test('báo cáo: mở hộp thoại, xác nhận, nút đổi thành Đã báo cáo, đánh giá của mình không có nút', async ({
    page,
  }) => {
    const consoleIssues = collectConsoleIssues(page);
    const own = alreadyReviewed(new Date(Date.now() + 10 * DAY_MS));
    const mock = new ReviewsApiMock(
      [buildReview({ id: 'review-mine', authorName: 'Khách E2E', rating: 3 }), ...defaultReviews],
      own,
    );
    await prepare(page, mock);
    await loginCustomer(page);

    await page.goto(`/p/${product.slug}`);
    const region = reviewsRegion(page);
    const others = region.getByRole('listitem').filter({ hasText: 'Trần A.' });
    // Hai đánh giá của người khác có nút, đánh giá của chính khách thì không.
    await expect(region.getByRole('button', { name: 'Báo cáo' })).toHaveCount(2);
    await expect(
      region.getByRole('listitem').filter({ hasText: 'Khách E2E' }).getByRole('button'),
    ).toHaveCount(0);

    await others.getByRole('button', { name: 'Báo cáo' }).click();
    const dialog = page.getByRole('dialog', { name: 'Báo cáo đánh giá' });
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Gửi báo cáo' }).click();

    await expect(dialog).toBeHidden();
    const reported = others.getByRole('button', { name: 'Đã báo cáo' });
    await expect(reported).toBeDisabled();
    expect(mock.reportedIds).toEqual(['review-a']);
    expect(consoleIssues).toEqual([]);
  });

  test('nhân viên không gọi /mine, thấy lý do và không có form hay nút báo cáo', async ({
    page,
  }) => {
    const consoleIssues = collectConsoleIssues(page);
    const mock = new ReviewsApiMock(defaultReviews, eligible);
    await prepare(page, mock);
    await loginAdmin(page);

    await page.goto(`/p/${product.slug}`);
    const region = reviewsRegion(page);
    await expect(region.getByText('Trần A.')).toBeVisible();
    await expect(region.getByText('Tài khoản nhân viên không viết đánh giá.')).toBeVisible();
    await expect(region.getByRole('radio')).toHaveCount(0);
    await expect(region.getByRole('textbox')).toHaveCount(0);
    await expect(region.getByRole('button', { name: 'Báo cáo' })).toHaveCount(0);
    expect(mock.mineRequestCount).toBe(0);
    expect(consoleIssues).toEqual([]);
  });

  test('form viết: lỗi Chọn số sao và nhận xét quá dài không làm tràn ngang ở 1280 và 375', async ({
    page,
  }, testInfo) => {
    const consoleIssues = collectConsoleIssues(page);
    const mock = new ReviewsApiMock(defaultReviews, eligible);
    await prepare(page, mock);
    await loginCustomer(page);

    await page.goto(`/p/${product.slug}`);
    await dismissConsent(page);
    await page.addStyleTag({ content: '.site-header { position: static !important; }' });
    const region = reviewsRegion(page);
    await region.getByRole('button', { name: 'Gửi đánh giá' }).click();
    await region.getByLabel(/^Nhận xét/).fill('y'.repeat(2001));
    await region.getByRole('button', { name: 'Gửi đánh giá' }).click();
    await expect(region.getByText('Chọn số sao')).toBeVisible();
    await expect(region.getByText('Nhận xét tối đa 2.000 ký tự.')).toBeVisible();
    expect(mock.postBodies).toEqual([]);

    for (const width of [1280, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await expectNoHorizontalOverflow(page);
      await savePageShot(page, testInfo, `COM-UI9-form-${width}`);
    }
    expect(consoleIssues).toEqual([]);
  });

  test('đánh giá của chính khách, còn hạn sửa và đang bị ẩn, không tràn ngang ở 1280 và 375', async ({
    page,
  }, testInfo) => {
    const consoleIssues = collectConsoleIssues(page);
    const own = alreadyReviewed(new Date(Date.now() + 10 * DAY_MS), true);
    own.review!.body = 'z'.repeat(300);
    const mock = new ReviewsApiMock(defaultReviews, own);
    await prepare(page, mock);
    await loginCustomer(page);

    await page.goto(`/p/${product.slug}`);
    await dismissConsent(page);
    await page.addStyleTag({ content: '.site-header { position: static !important; }' });
    const region = reviewsRegion(page);
    await expect(region.getByText('Đánh giá của bạn đang bị ẩn.')).toBeVisible();
    await expect(region.getByRole('button', { name: 'Sửa đánh giá' })).toBeVisible();

    for (const width of [1280, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await expectNoHorizontalOverflow(page);
      await savePageShot(page, testInfo, `COM-UI9-own-${width}`);
    }
    expect(consoleIssues).toEqual([]);
  });

  test('hộp thoại báo cáo đang mở không làm tràn ngang ở 1280 và 375', async ({
    page,
  }, testInfo) => {
    const consoleIssues = collectConsoleIssues(page);
    const mock = new ReviewsApiMock(defaultReviews, eligible);
    await prepare(page, mock);
    await loginCustomer(page);

    await page.goto(`/p/${product.slug}`);
    await dismissConsent(page);
    const region = reviewsRegion(page);
    await region.getByRole('button', { name: 'Báo cáo' }).first().click();
    await expect(page.getByRole('dialog', { name: 'Báo cáo đánh giá' })).toBeVisible();

    for (const width of [1280, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await expectNoHorizontalOverflow(page);
      await savePageShot(page, testInfo, `COM-UI9-dialog-${width}`, false);
    }
    expect(consoleIssues).toEqual([]);
  });

  test('POST bị từ chối 403 thì hiện thông báo của server và giữ số sao, nhận xét', async ({
    page,
  }) => {
    const consoleIssues = collectConsoleIssuesAllowingMockedFailures(page, [
      { status: 403, urlPart: '/reviews' },
    ]);
    const mock = new ReviewsApiMock(defaultReviews, eligible);
    mock.writeError = {
      status: 403,
      code: 'REVIEW_NOT_ALLOWED',
      message: 'Bạn chỉ đánh giá được sản phẩm đã nhận.',
    };
    await prepare(page, mock);
    await loginCustomer(page);

    await page.goto(`/p/${product.slug}`);
    const region = reviewsRegion(page);
    await region.getByRole('radio', { name: '4 sao' }).check();
    await region.getByLabel(/^Nhận xét/).fill('Hơi nhỏ so với mong đợi.');
    await region.getByRole('button', { name: 'Gửi đánh giá' }).click();

    await expect(region.getByRole('alert')).toContainText(
      'Bạn chỉ đánh giá được sản phẩm đã nhận.',
    );
    await expect(region.getByRole('radio', { name: '4 sao' })).toBeChecked();
    await expect(region.getByLabel(/^Nhận xét/)).toHaveValue('Hơi nhỏ so với mong đợi.');
    await expect(region.getByRole('button', { name: 'Gửi đánh giá' })).toBeEnabled();
    expect(consoleIssues).toEqual([]);
  });

  test('PATCH bị từ chối 409 REVIEW_EDIT_EXPIRED thì hiện thông báo của server và giữ form', async ({
    page,
  }) => {
    const consoleIssues = collectConsoleIssuesAllowingMockedFailures(page, [
      { status: 409, urlPart: '/reviews/mine' },
    ]);
    const mock = new ReviewsApiMock(
      defaultReviews,
      alreadyReviewed(new Date(Date.now() + 10 * DAY_MS)),
    );
    mock.writeError = {
      status: 409,
      code: 'REVIEW_EDIT_EXPIRED',
      message: 'Đã quá 30 ngày, không sửa được đánh giá.',
    };
    await prepare(page, mock);
    await loginCustomer(page);

    await page.goto(`/p/${product.slug}`);
    const region = reviewsRegion(page);
    await region.getByRole('button', { name: 'Sửa đánh giá' }).click();
    await region.getByRole('radio', { name: '5 sao' }).check();
    await region.getByRole('button', { name: 'Lưu đánh giá' }).click();

    await expect(region.getByRole('alert')).toContainText(
      'Đã quá 30 ngày, không sửa được đánh giá.',
    );
    await expect(region.getByRole('radio', { name: '5 sao' })).toBeChecked();
    await expect(region.getByLabel(/^Nhận xét/)).toHaveValue('Tạm ổn.');
    expect(consoleIssues).toEqual([]);
  });

  test('POST trả 400 kèm details cho body thì lỗi hiện dưới ô nhận xét', async ({ page }) => {
    const consoleIssues = collectConsoleIssuesAllowingMockedFailures(page, [
      { status: 400, urlPart: '/reviews' },
    ]);
    const mock = new ReviewsApiMock(defaultReviews, eligible);
    mock.writeError = {
      status: 400,
      code: 'VALIDATION_ERROR',
      message: 'Dữ liệu không hợp lệ (body: Nhận xét là chuỗi tối đa 2.000 ký tự.).',
      details: [{ key: 'body', message: 'Nhận xét là chuỗi tối đa 2.000 ký tự.' }],
    };
    await prepare(page, mock);
    await loginCustomer(page);

    await page.goto(`/p/${product.slug}`);
    const region = reviewsRegion(page);
    await region.getByRole('radio', { name: '2 sao' }).check();
    await region.getByLabel(/^Nhận xét/).fill('Không ưng.');
    await region.getByRole('button', { name: 'Gửi đánh giá' }).click();

    const bodyField = region.getByLabel(/^Nhận xét/);
    await expect(bodyField).toHaveAttribute('aria-invalid', 'true');
    await expect(bodyField).toHaveAccessibleDescription(/Nhận xét là chuỗi tối đa 2.000 ký tự\./);
    await expect(region.locator('#review-body-error')).toHaveText(
      'Nhận xét là chuỗi tối đa 2.000 ký tự.',
    );
    // Lỗi đã gắn vào ô, nên không còn thông báo chung ở đầu form.
    await expect(region.getByRole('alert')).toHaveCount(0);
    expect(consoleIssues).toEqual([]);
  });

  test('tải danh sách lỗi 500 thì khối hiện lỗi và Thử lại, phần mua hàng vẫn dùng được', async ({
    page,
  }) => {
    const consoleIssues = collectConsoleIssuesAllowingMockedFailures(page, [
      { status: 500, urlPart: '/reviews' },
    ]);
    const mock = new ReviewsApiMock(defaultReviews, notPurchased);
    mock.listError = { status: 500, code: 'INTERNAL', message: 'Máy chủ gặp lỗi.' };
    await prepare(page, mock);

    await page.goto(`/p/${product.slug}`);
    const region = reviewsRegion(page);
    // Truy vấn thử lại ba lần với khoảng chờ tăng dần trước khi báo lỗi, nên chờ lâu hơn mặc định.
    await expect(region.getByRole('alert')).toContainText('Máy chủ gặp lỗi.', { timeout: 20000 });
    await expect(page.getByRole('button', { name: 'Thêm vào giỏ' })).toBeVisible();

    mock.listError = null;
    await region.getByRole('button', { name: 'Thử lại' }).click();
    await expect(region.getByText('5,0')).toBeVisible();
    await expect(region.getByRole('alert')).toHaveCount(0);
    expect(consoleIssues).toEqual([]);
  });

  test('Xem thêm đánh giá nối trang sau vào cuối và gửi đúng cursor', async ({ page }) => {
    const consoleIssues = collectConsoleIssues(page);
    const pageOne = [
      buildReview({ id: 'review-p1a', authorName: 'Người P1A' }),
      buildReview({ id: 'review-p1b', authorName: 'Người P1B' }),
    ];
    const pageTwo = [buildReview({ id: 'review-p2a', authorName: 'Người P2A' })];
    const mock = new ReviewsApiMock([...pageOne, ...pageTwo], notPurchased);
    mock.pages = [
      { cursor: '', items: pageOne, nextCursor: 'cursor-2' },
      { cursor: 'cursor-2', items: pageTwo, nextCursor: null },
    ];
    await prepare(page, mock);

    await page.goto(`/p/${product.slug}`);
    const region = reviewsRegion(page);
    await expect(region.getByRole('listitem').filter({ hasText: 'Người P1A' })).toBeVisible();
    await expect(region.getByText('Người P2A')).toHaveCount(0);

    await region.getByRole('button', { name: 'Xem thêm đánh giá' }).click();
    await expect(region.getByText('Người P2A')).toBeVisible();
    expect(mock.listCursors).toEqual([null, 'cursor-2']);
    const names = await region.locator('.review-author').allTextContents();
    expect(names).toEqual(['Người P1A', 'Người P1B', 'Người P2A']);
    await expect(region.getByRole('button', { name: 'Xem thêm đánh giá' })).toHaveCount(0);
    expect(consoleIssues).toEqual([]);
  });

  test('tải trang sau lỗi thì giữ tóm tắt và danh sách, lỗi hiện cạnh nút Xem thêm', async ({
    page,
  }) => {
    const consoleIssues = collectConsoleIssuesAllowingMockedFailures(page, [
      { status: 500, urlPart: '/reviews' },
    ]);
    const pageOne = [buildReview({ id: 'review-p1a', authorName: 'Người P1A' })];
    const mock = new ReviewsApiMock(pageOne, notPurchased);
    mock.pages = [{ cursor: '', items: pageOne, nextCursor: 'cursor-2' }];
    mock.failCursor = 'cursor-2';
    await prepare(page, mock);

    await page.goto(`/p/${product.slug}`);
    const region = reviewsRegion(page);
    await region.getByRole('button', { name: 'Xem thêm đánh giá' }).click();

    await expect(region.getByRole('alert')).toContainText('Không tải được trang sau.', {
      timeout: 20000,
    });
    await expect(region.getByText('5,0')).toBeVisible();
    await expect(region.getByText('Người P1A')).toBeVisible();
    await expect(region.getByRole('button', { name: 'Xem thêm đánh giá' })).toBeVisible();
    expect(consoleIssues).toEqual([]);
  });

  test('báo cáo bị lỗi 500 thì lỗi hiện trong hộp thoại và hộp thoại không đóng', async ({
    page,
  }) => {
    const consoleIssues = collectConsoleIssuesAllowingMockedFailures(page, [
      { status: 500, urlPart: '/reports' },
    ]);
    const mock = new ReviewsApiMock(defaultReviews, eligible);
    mock.reportError = { status: 500, code: 'INTERNAL', message: 'Không gửi được báo cáo.' };
    await prepare(page, mock);
    await loginCustomer(page);

    await page.goto(`/p/${product.slug}`);
    const region = reviewsRegion(page);
    await region.getByRole('button', { name: 'Báo cáo' }).first().click();
    const dialog = page.getByRole('dialog', { name: 'Báo cáo đánh giá' });
    await dialog.getByRole('button', { name: 'Gửi báo cáo' }).click();

    await expect(dialog.getByRole('alert')).toContainText('Không gửi được báo cáo.');
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Gửi báo cáo' })).toBeEnabled();
    await expect(region.getByRole('button', { name: 'Đã báo cáo' })).toHaveCount(0);
    expect(consoleIssues).toEqual([]);
  });
});
