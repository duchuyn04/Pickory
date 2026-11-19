import { expect, test, type Page } from '@playwright/test';
import {
  expectNoHorizontalOverflow,
  prepareShipping,
  seedProduct,
  type SeededProduct,
} from './admin-order-fixtures';
import { loginAdmin } from './catalog-admin-flow';
import {
  loginSeededCustomer,
  openIsolatedSession,
  placeCodOrderByApi,
  readAdminOrderDetail,
  type IsolatedSession,
} from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';
import { activateStaffAccount, inviteStaff } from './staff-fixtures';

const webOriginHeaders = { Origin: e2eEnvironment.webOrigin };

const DESKTOP_VIEWPORT = { width: 1280, height: 800 };
const MOBILE_VIEWPORT = { width: 375, height: 812 };

const MANUAL_SHIPPING_NOTICE = 'Shop đang giao hàng thủ công. Không có mã vận đơn GHN.';
// Ba nút này chỉ có ở đơn `shipping`. Nút Giao hàng thủ công chỉ có ở đơn `confirmed`, nên được kiểm riêng ở bước 2.
const FULFILLMENT_BUTTON_NAMES = ['Quay về đã xác nhận', 'Xác nhận đã giao', 'Ghi nhận hoàn hàng'];

// Mở chi tiết đơn của nhân viên. Chờ tiêu đề đơn hiện, vì trang tải dữ liệu bất đồng bộ.
async function openAdminOrder(page: Page, orderCode: string) {
  await page.goto(`/admin/orders/${orderCode}`);
  await expect(
    page.getByRole('heading', { level: 1, name: `Đơn hàng ${orderCode}` }),
  ).toBeVisible();
}

// Menu chỉ hiện sau khi hồ sơ quyền tải xong. Chờ menu trước khi kiểm một nút vắng mặt, để kiểm tra không xanh giả.
async function waitForAdminNavigation(page: Page) {
  const navigation = page.getByRole('navigation', { name: 'Quản trị', exact: true });
  await expect(navigation.getByRole('link', { name: 'Đơn hàng', exact: true })).toBeVisible();
}

// Gửi thao tác giao hàng qua hộp thoại với lý do bắt buộc. Hộp thoại chỉ đóng khi API đã nhận thao tác.
async function submitFulfillmentDialog(
  page: Page,
  buttonName: string,
  submitName: string,
  reason: string,
) {
  await page.getByRole('button', { name: buttonName, exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Lý do', { exact: true }).fill(reason);
  await dialog.getByRole('button', { name: submitName, exact: true }).click();
  await expect(dialog).toBeHidden();
}

// Mọi kiểm tra về đánh giá đọc trong vùng có tên "Đánh giá sản phẩm".
function reviewsRegion(page: Page) {
  return page.getByRole('region', { name: 'Đánh giá sản phẩm' });
}

// Mở trang sản phẩm của khách. Chờ vùng đánh giá hiện trước khi kiểm phần tử bên trong.
async function openProductReviews(page: Page, product: SeededProduct) {
  await page.goto(`/p/${product.slug}`);
  const region = reviewsRegion(page);
  await expect(region).toBeVisible();
  return region;
}

// Kiểm trang không tràn ngang ở khung 375 px và 1280 px. Hàm trả khung nhìn về 1280 px.
async function expectNoOverflowOnBothViewports(page: Page) {
  await page.setViewportSize(MOBILE_VIEWPORT);
  await expectNoHorizontalOverflow(page);
  await page.setViewportSize(DESKTOP_VIEWPORT);
  await expectNoHorizontalOverflow(page);
}

// Mỗi thao tác tối đa 30 giây. Thao tác kẹt báo lỗi ngay tại dòng kẹt thay vì chờ hết thời gian của cả test.
test.use({ actionTimeout: 30000, viewport: DESKTOP_VIEWPORT });
test.describe('COM-E2E6: giao hàng thủ công rồi khách viết đánh giá', () => {
  test('sales giao thủ công và xác nhận đã giao, support bị chặn, khách chỉ đánh giá được sau khi đã giao', async ({
    browser,
  }) => {
    // Test dựng hai nhân viên qua thư mời và đưa một đơn đi hết vòng đời, nên cần nhiều thời gian hơn mặc định.
    test.setTimeout(300000);

    const admin = await openIsolatedSession(browser);
    const sales = await openIsolatedSession(browser);
    const support = await openIsolatedSession(browser);
    const customer = await openIsolatedSession(browser);
    const namedSessions: [string, IsolatedSession][] = [
      ['admin', admin],
      ['sales', sales],
      ['support', support],
      ['khách', customer],
    ];

    // Chuỗi duy nhất của lần chạy. Lý do và nhận xét chứa chuỗi này nên kiểm tra không nhầm với dữ liệu lần khác.
    const runId = Date.now().toString(36);
    const startReason = `Shop tự giao vì GHN từ chối ${runId}`;
    const deliveredReason = `Khách đã nhận hàng tại cửa ${runId}`;
    const reviewBody = `Giao thủ công nhưng hàng tốt ${runId}`;

    let product: SeededProduct;
    let orderCode = '';

    try {
      await test.step('Admin dựng sản phẩm, kho gửi, mời sales và support, rồi khách đặt đơn COD', async () => {
        await loginAdmin(admin.page);
        product = await seedProduct(admin.page);
        await prepareShipping(admin.page);

        await inviteStaff(admin.page, {
          email: e2eEnvironment.manualFulfillmentSalesEmail,
          fullName: 'Sales Giao Thủ Công E2E',
          roleCode: 'sales',
        });
        await inviteStaff(admin.page, {
          email: e2eEnvironment.manualFulfillmentSupportEmail,
          fullName: 'Support Giao Thủ Công E2E',
          roleCode: 'support',
        });
        await activateStaffAccount(sales.page, e2eEnvironment.manualFulfillmentSalesEmail);
        await activateStaffAccount(support.page, e2eEnvironment.manualFulfillmentSupportEmail);

        // Đơn gắn với tài khoản khách đang đăng nhập, vì chỉ chủ đơn mới được đánh giá sau khi đơn đã giao.
        await loginSeededCustomer(customer.page, e2eEnvironment.reviewsVisibleCustomerEmail);
        orderCode = await placeCodOrderByApi(customer.page, product);
        expect(orderCode).toMatch(/^PK/);
      });

      await test.step('Sales xác nhận đơn rồi bấm Giao hàng thủ công thì đơn sang Đang giao hàng, không có thẻ Vận đơn', async () => {
        await openAdminOrder(sales.page, orderCode);
        await expect(sales.page.getByLabel('Tổng quan đơn')).toContainText('Chờ xác nhận');

        await sales.page.getByRole('button', { name: 'Xác nhận đơn', exact: true }).click();
        const confirmDialog = sales.page.getByRole('dialog');
        await confirmDialog.getByRole('button', { name: 'Xác nhận', exact: true }).click();
        await expect(confirmDialog).toBeHidden();
        await expect(sales.page.getByLabel('Tổng quan đơn')).toContainText('Đã xác nhận');

        // Support mở cùng đơn lúc này. Nút Giao hàng thủ công chỉ có ở đơn đã xác nhận, nên đây là lúc duy nhất kiểm được nó vắng mặt.
        await expect(
          sales.page.getByRole('button', { name: 'Giao hàng thủ công', exact: true }),
        ).toBeVisible();
        await openAdminOrder(support.page, orderCode);
        await expect(support.page.getByLabel('Tổng quan đơn')).toContainText('Đã xác nhận');
        await waitForAdminNavigation(support.page);
        await expect(
          support.page.getByRole('button', { name: 'Giao hàng thủ công', exact: true }),
        ).toHaveCount(0);

        await submitFulfillmentDialog(
          sales.page,
          'Giao hàng thủ công',
          'Bắt đầu giao thủ công',
          startReason,
        );

        const overview = sales.page.getByLabel('Tổng quan đơn');
        await expect(overview).toContainText('Đang giao hàng');
        await expect(overview.locator('dl > div').filter({ hasText: 'Ghi chú' })).toContainText(
          'Giao hàng thủ công',
        );
        // Đơn giao thủ công không có vận đơn GHN, nên thẻ Vận đơn không được hiện.
        await expect(sales.page.getByRole('heading', { name: 'Vận đơn', exact: true })).toHaveCount(
          0,
        );

        const order = await readAdminOrderDetail(sales.page, orderCode);
        expect(order.status).toBe('shipping');
        expect(order.flags).toContain('manual_shipping');
        expect(order.shipment).toBeNull();
      });

      await test.step('Khách mở trang đơn thì thấy chữ giao thủ công, không thấy Mã vận đơn, và không tràn ngang', async () => {
        await customer.page.goto(`/orders/${orderCode}`);
        await expect(
          customer.page.getByRole('heading', { level: 1, name: `Đơn hàng #${orderCode}` }),
        ).toBeVisible();
        await expect(
          customer.page.getByText(MANUAL_SHIPPING_NOTICE, { exact: true }),
        ).toBeVisible();
        // Chữ giao thủ công đã hiện, nên trang đã tải xong và kiểm vắng mặt bên dưới không xanh giả.
        await expect(customer.page.getByText('Mã vận đơn', { exact: true })).toHaveCount(0);

        await expectNoOverflowOnBothViewports(customer.page);
      });

      await test.step('Trước khi đã giao, khách không thấy form đánh giá và API đánh giá trả 403', async () => {
        const region = await openProductReviews(customer.page, product);

        // Chờ lý do hiện ra. Lúc đó trạng thái khối viết đánh giá đã chốt, nên kiểm vắng mặt bên dưới không xanh giả.
        await expect(
          region.getByText('Chỉ khách đã nhận sản phẩm này mới viết được đánh giá.'),
        ).toBeVisible();
        await expect(region.getByRole('heading', { name: 'Viết đánh giá của bạn' })).toHaveCount(0);
        await expect(region.getByRole('button', { name: 'Gửi đánh giá' })).toHaveCount(0);

        // Gọi thẳng API để chứng minh server tự chặn, không chỉ giao diện ẩn form.
        const attempt = await customer.page.request.post(`/api/products/${product.id}/reviews`, {
          headers: webOriginHeaders,
          data: { rating: 5 },
        });
        expect(attempt.status(), await attempt.text()).toBe(403);
        expect((await attempt.json()).error.code).toBe('REVIEW_NOT_ALLOWED');

        const mine = await customer.page.request.get(`/api/products/${product.id}/reviews/mine`);
        expect(mine.status(), await mine.text()).toBe(200);
        const mineStatus = (await mine.json()).data;
        expect(mineStatus.eligible).toBe(false);
        // Lý do NOT_PURCHASED nghĩa là chưa có đơn đã giao. Một lỗi khác cũng làm `eligible` là false.
        expect(mineStatus.reason).toBe('NOT_PURCHASED');
      });

      await test.step('Support thấy chi tiết đơn nhưng không có nút giao hàng, và API fulfillment trả 403, đơn không đổi', async () => {
        // Sales là đối chứng: cùng đơn `shipping`, sales thấy ba nút còn support thì không.
        await openAdminOrder(sales.page, orderCode);
        for (const buttonName of FULFILLMENT_BUTTON_NAMES) {
          await expect(
            sales.page.getByRole('button', { name: buttonName, exact: true }),
          ).toBeVisible();
        }

        await openAdminOrder(support.page, orderCode);
        await expect(support.page.getByLabel('Tổng quan đơn')).toContainText('Đang giao hàng');
        // Chờ dữ liệu đơn tải xong trước khi kiểm vắng nút, để kiểm vắng không xanh giả.
        await expect(support.page.getByText('Lê Văn Đánh')).toBeVisible();
        await waitForAdminNavigation(support.page);
        for (const buttonName of FULFILLMENT_BUTTON_NAMES) {
          await expect(
            support.page.getByRole('button', { name: buttonName, exact: true }),
          ).toHaveCount(0);
        }

        const forbidden = await support.page.request.put(
          `/api/admin/orders/${orderCode}/fulfillment`,
          {
            headers: webOriginHeaders,
            data: { toStatus: 'delivered', reason: 'Support thử xác nhận giao' },
          },
        );
        expect(forbidden.status(), await forbidden.text()).toBe(403);
        // Mã FORBIDDEN chứng minh API chặn vì thiếu quyền. Một 403 do Origin hay phiên sai có mã khác.
        expect((await forbidden.json()).error.code).toBe('FORBIDDEN');

        // Đọc lại bằng quyền của sales. Lệnh bị từ chối không được làm đổi trạng thái hay thêm cờ giao tay.
        const order = await readAdminOrderDetail(sales.page, orderCode);
        expect(order.status).toBe('shipping');
        expect(order.flags).not.toContain('manual_delivery');
      });

      await test.step('Sales bấm Xác nhận đã giao thì đơn sang Đã giao hàng với hai cờ giao thủ công', async () => {
        await openAdminOrder(sales.page, orderCode);
        await sales.page.getByRole('button', { name: 'Xác nhận đã giao', exact: true }).click();
        const dialog = sales.page.getByRole('dialog');
        await expect(dialog).toContainText('Chỉ tiếp tục khi khách đã nhận hàng');
        await dialog.getByLabel('Lý do', { exact: true }).fill(deliveredReason);
        await dialog.getByRole('button', { name: 'Xác nhận đã giao', exact: true }).click();
        await expect(dialog).toBeHidden();

        const deliveredOverview = sales.page.getByLabel('Tổng quan đơn');
        await expect(deliveredOverview).toContainText('Đã giao hàng');
        await expect(deliveredOverview).toContainText('Giao hàng thủ công');
        await expect(deliveredOverview).toContainText('Xác nhận giao thủ công');

        const order = await readAdminOrderDetail(sales.page, orderCode);
        expect(order.status).toBe('delivered');
        expect(order.flags).toEqual(expect.arrayContaining(['manual_shipping', 'manual_delivery']));
        // Mục lịch sử còn có người làm và thời điểm, nên chỉ so ba trường cần kiểm.
        expect(order.statusHistory).toContainEqual(
          expect.objectContaining({
            fromStatus: 'shipping',
            toStatus: 'delivered',
            reason: deliveredReason,
          }),
        );
      });

      await test.step('Khách mở lại trang sản phẩm, viết đánh giá 5 sao thì đánh giá hiện trong danh sách', async () => {
        const region = await openProductReviews(customer.page, product);

        await expect(region.getByRole('heading', { name: 'Viết đánh giá của bạn' })).toBeVisible();
        await expectNoOverflowOnBothViewports(customer.page);

        await region.getByRole('radio', { name: '5 sao' }).check();
        await region.getByLabel(/^Nhận xét/).fill(reviewBody);
        await region.getByRole('button', { name: 'Gửi đánh giá' }).click();

        await expect(region.getByRole('heading', { name: 'Đánh giá của bạn' })).toBeVisible();
        await expect(region.locator('ul.review-list').getByText(reviewBody)).toBeVisible();
        await expectNoOverflowOnBothViewports(customer.page);

        const mine = await customer.page.request.get(`/api/products/${product.id}/reviews/mine`);
        expect(mine.status(), await mine.text()).toBe(200);
        const status = (await mine.json()).data;
        expect(status.eligible).toBe(false);
        expect(status.reason).toBe('ALREADY_REVIEWED');
        expect(status.review.body).toBe(reviewBody);
      });

      for (const [label, session] of namedSessions) {
        expect(session.consoleIssues, `Console của ${label} phải sạch`).toEqual([]);
      }
    } finally {
      for (const [, session] of namedSessions) {
        await session.context.close();
      }
    }
  });
});
