import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';
import { trackEventRequests } from './activity-tracking-fixtures';
import {
  chooseOption,
  prepareShipping,
  seedProduct,
  type AddressChoice,
  type SeededProduct,
} from './admin-order-fixtures';
import { loginAdmin } from './catalog-admin-flow';
import { openIsolatedSession, stubProductImages } from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';

// Ba bước phễu theo thứ tự xem sản phẩm, thêm giỏ, đặt đơn. Số lượt của mỗi bước trong khoảng ngày.
interface FunnelCounts {
  productView: number;
  addToCart: number;
  orderPlaced: number;
}

interface FunnelStepBody {
  metric: string;
  count: number;
  conversionRate: number | null;
}

// Ngày lịch giờ `Asia/Ho_Chi_Minh` (UTC+7 cố định) cách hôm nay `offsetDays` ngày, dạng `yyyy-MM-dd`.
function vietnamDate(offsetDays: number): string {
  const vietnamOffsetMs = 7 * 60 * 60 * 1000;
  const dayMs = 24 * 60 * 60 * 1000;
  // Cộng 7 giờ rồi đọc ngày UTC, để ngày không phụ thuộc múi giờ của máy chạy test.
  return new Date(Date.now() + vietnamOffsetMs + offsetDays * dayMs).toISOString().slice(0, 10);
}

// Tính một lần ở đầu spec. Khoảng hôm qua đến ngày mai vẫn chứa bộ đếm khi luồng chạy qua nửa đêm.
const funnelRange = { from: vietnamDate(-1), to: vietnamDate(1) };

// Đọc phễu bằng phiên admin. API không đếm lượt xem của nhân viên, nên trang khách phải nằm ở context khác.
async function readFunnel(adminPage: Page): Promise<FunnelCounts> {
  const response = await adminPage.request.get(
    `/api/admin/analytics/funnel?from=${funnelRange.from}&to=${funnelRange.to}`,
  );
  expect(response.status(), await response.text()).toBe(200);
  const steps = (await response.json()).data.steps as FunnelStepBody[];

  // Thứ tự và tên ba bước cố định. Kiểm ngay để lỗi đổi dạng response báo đúng chỗ.
  expect(steps.map((step) => step.metric)).toEqual(['product_view', 'add_to_cart', 'order_placed']);
  return {
    productView: steps[0]!.count,
    addToCart: steps[1]!.count,
    orderPlaced: steps[2]!.count,
  };
}

// Cách khách đối xử với banner đồng ý: bấm "Đồng ý", hoặc để banner mở suốt luồng.
type BannerChoice = 'agree' | 'leave-open';

interface GuestFlowResult {
  before: FunnelCounts;
  after: FunnelCounts;
  eventStatuses: (number | null)[];
  consoleIssues: string[];
  bannerVisibleAtEnd: boolean;
}

// Hiệu số từng bước của phễu giữa hai lần đọc.
function funnelIncrease(result: GuestFlowResult): FunnelCounts {
  return {
    productView: result.after.productView - result.before.productView,
    addToCart: result.after.addToCart - result.before.addToCart,
    orderPlaced: result.after.orderPlaced - result.before.orderPlaced,
  };
}

// Điều mọi luồng đều phải đúng, dù khách chọn banner thế nào: mỗi bước tăng 1, request lượt xem trả 204 và console sạch.
function expectCountedOnce(result: GuestFlowResult) {
  expect(funnelIncrease(result)).toEqual({ productView: 1, addToCart: 1, orderPlaced: 1 });
  // Mọi request lượt xem của luồng phải trả 204. Lỗi 429 hay 403 không được bị bỏ sót.
  expect(result.eventStatuses).toEqual([204]);
  expect(result.consoleIssues).toEqual([]);
}

test.describe('ACT-E2E1: luồng đếm của activity-tracking', () => {
  let adminContext: BrowserContext;
  let adminPage: Page;
  let product: SeededProduct;
  let address: AddressChoice;

  test.beforeAll(async ({ browser }) => {
    // Tạo sản phẩm và cấu hình kho cần nhiều lệnh API, nên cho thời gian dài hơn mặc định.
    test.setTimeout(180000);
    adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    adminPage = await adminContext.newPage();
    // Trang sửa sản phẩm vẽ ảnh Cloudinary. Chặn để test không phụ thuộc mạng ngoài.
    await stubProductImages(adminPage);
    await loginAdmin(adminPage);
    product = await seedProduct(adminPage);
    address = await prepareShipping(adminPage);
  });

  test.afterAll(async () => {
    await adminContext?.close();
  });

  // Một khách vãng lai xem sản phẩm, thêm giỏ và đặt đơn COD qua giao diện thật. Phễu được đọc trước và sau luồng.
  async function runGuestPurchase(
    browser: Browser,
    banner: BannerChoice,
  ): Promise<GuestFlowResult> {
    // Mỗi luồng dùng context mới, nên cookie `pk_anon`, giỏ và banner không lẫn giữa hai luồng.
    const guest = await openIsolatedSession(browser);
    const { page } = guest;
    try {
      const events = trackEventRequests(page);
      const bannerRegion = page.getByRole('region', { name: 'Đồng ý cookie và quyền riêng tư' });
      const before = await readFunnel(adminPage);

      // Trang chủ không gửi lượt xem. Khách chọn banner ở đây, trước khi mở sản phẩm.
      await page.goto('/');
      await expect(bannerRegion).toBeVisible();
      if (banner === 'agree') {
        // Test đăng ký chờ response trước khi bấm, để không bỏ lỡ response về nhanh.
        // Banner chỉ ẩn sau khi server nhận lựa chọn.
        const consentSaved = page.waitForResponse(
          (response) =>
            new URL(response.url()).pathname === '/api/me/consent' &&
            response.request().method() === 'PUT',
        );
        await bannerRegion.getByRole('button', { name: 'Đồng ý', exact: true }).click();
        expect((await consentSaved).ok()).toBe(true);
        await expect(bannerRegion).toBeHidden();
      }

      // Mở trang sản phẩm đúng một lần. Mỗi lần mở lại sẽ cộng thêm một lượt xem.
      await page.goto(`/p/${product.slug}`);
      await expect(page.getByRole('heading', { level: 1 })).toHaveText(product.name);
      // Chờ request lượt xem có response thay vì chờ cố định. Bộ đếm đã cộng khi response về.
      await expect.poll(() => events.filter((event) => event.status !== null).length).toBe(1);

      await page.getByRole('button', { name: 'Thêm vào giỏ' }).click();
      await expect(page.getByRole('status').filter({ hasText: 'Đã thêm vào giỏ' })).toBeVisible();

      // Khách đi qua giỏ như khách thật. Trang giỏ và trang đặt hàng không gửi lượt xem, nên số lượt xem không đổi.
      await page.getByRole('button', { name: /^Giỏ hàng, / }).click();
      await page.getByRole('link', { name: 'Xem giỏ hàng' }).click();
      await page.getByRole('link', { name: 'Tiến hành đặt hàng' }).click();
      await expect(page.getByRole('heading', { level: 1, name: 'Đặt hàng' })).toBeVisible();

      // COD là cách thanh toán mặc định của trang đặt hàng. Khối này chỉ điền thông tin giao hàng tối thiểu để API nhận đơn.
      await chooseOption(page, 'checkout-address-province', address.provinceName);
      await chooseOption(page, 'checkout-address-ward', address.wardName);
      await page.locator('#checkout-address-line').fill('18 Đường Đếm Lượt');
      await page.getByLabel('Họ và tên', { exact: true }).fill('Phạm Thị Dung');
      await page.getByLabel('Số điện thoại', { exact: true }).fill('0987002233');
      await page
        .getByLabel('Email', { exact: true })
        .fill(`dem-luot-${Date.now().toString(36)}@example.com`);
      await expect(page.getByLabel('Thanh toán khi nhận hàng (COD)')).toBeChecked();
      // Chỉ bấm "Đặt hàng" một lần. Bấm nhiều lần có thể gặp lỗi race của COM-28.
      await page.getByRole('button', { name: /Đặt hàng$/ }).click();

      // Backend chờ handler đếm chạy xong trước khi trả response, nên bộ đếm đã cộng khi trang này hiện.
      await expect(
        page.getByRole('heading', { level: 1, name: 'Đặt hàng thành công' }),
      ).toBeVisible();
      const after = await readFunnel(adminPage);

      return {
        before,
        after,
        eventStatuses: events.map((event) => event.status),
        consoleIssues: [...guest.consoleIssues],
        bannerVisibleAtEnd: await bannerRegion.isVisible(),
      };
    } finally {
      await guest.context.close();
    }
  }

  test('khách bấm Đồng ý, xem sản phẩm, thêm giỏ, đặt COD thì phễu tăng đúng 1 ở mỗi bước', async ({
    browser,
  }) => {
    // Luồng đi qua trang chủ, sản phẩm, giỏ và đặt hàng, nên vượt mức mặc định 30 giây khi máy bận.
    test.setTimeout(180000);

    const result = await runGuestPurchase(browser, 'agree');

    expectCountedOnce(result);
    expect(result.bannerVisibleAtEnd).toBe(false);
  });

  test('khách chưa chọn banner làm cùng luồng thì phễu vẫn tăng đúng 1 ở mỗi bước', async ({
    browser,
  }) => {
    test.setTimeout(180000);

    const result = await runGuestPurchase(browser, 'leave-open');

    // Bộ đếm gộp theo ngày không cần đồng ý, vì bảng chỉ lưu số và không lưu định danh khách.
    expectCountedOnce(result);
    // Banner còn hiện ở cuối luồng, chứng minh khách chưa hề chọn.
    expect(result.bannerVisibleAtEnd).toBe(true);
  });
});
