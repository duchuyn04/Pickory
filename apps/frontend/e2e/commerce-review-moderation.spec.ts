import { expect, test, type Page } from '@playwright/test';
import { prepareShipping, seedProduct, type SeededProduct } from './admin-order-fixtures';
import { dismissConsent, loginAdmin } from './catalog-admin-flow';
import {
  confirmAndShipOrderByApi,
  deliverShipmentByWebhook,
  loginSeededCustomer,
  openIsolatedSession,
  placeCodOrderByApi,
  readAdminOrder,
  type IsolatedSession,
} from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';
import { activateStaffAccount, inviteStaff } from './staff-fixtures';

const webOriginHeaders = { Origin: e2eEnvironment.webOrigin };

// Khối đánh giá nằm trong vùng có tên "Đánh giá sản phẩm". Mọi kiểm tra về đánh giá đọc trong vùng này.
function reviewsRegion(page: Page) {
  return page.getByRole('region', { name: 'Đánh giá sản phẩm' });
}

// Mở trang sản phẩm trong phiên đã cho và trả vùng đánh giá. Phiên của khách vãng lai không phụ thuộc người viết.
async function openProductReviews(session: IsolatedSession, product: SeededProduct) {
  await session.page.goto(`/p/${product.slug}`);
  await dismissConsent(session.page);
  return reviewsRegion(session.page);
}

test.use({ actionTimeout: 30000 });
test.describe('COM-E2E4: đánh giá và kiểm duyệt', () => {
  test('khách chưa nhận hàng không viết được đánh giá, và đánh giá bị support ẩn thì mất khỏi trang sản phẩm', async ({
    browser,
  }) => {
    // Test dựng ba đơn, mời một nhân viên và đi qua nhiều màn hình, nên cần nhiều thời gian hơn mặc định.
    test.setTimeout(300000);

    const admin = await openIsolatedSession(browser);
    const support = await openIsolatedSession(browser);
    const visibleCustomer = await openIsolatedSession(browser);
    const hiddenCustomer = await openIsolatedSession(browser);
    const unreceivedCustomer = await openIsolatedSession(browser);
    // Khách vãng lai được mở thêm ở các bước sau. Mảng này gom chúng để đóng và kiểm console cùng các phiên khác.
    const guests: IsolatedSession[] = [];
    const namedSessions = [
      ['admin', admin],
      ['support', support],
      ['khách đã nhận hàng viết 5 sao', visibleCustomer],
      ['khách đã nhận hàng viết 2 sao', hiddenCustomer],
      ['khách chưa nhận hàng', unreceivedCustomer],
    ] as const;

    // Chuỗi duy nhất của lần chạy. Nhận xét chứa chuỗi này nên kiểm vắng mặt không nhầm với đánh giá của lần chạy khác.
    const runId = Date.now().toString(36);
    const visibleBody = `Chuột êm, pin bền ${runId}`;
    const hiddenBody = `Chuột trễ, giao chậm ${runId}`;

    let product: SeededProduct;

    try {
      await test.step('Admin dựng sản phẩm, mời support, ba khách đặt đơn và hai đơn được giao', async () => {
        await loginAdmin(admin.page);
        product = await seedProduct(admin.page);
        await prepareShipping(admin.page);

        await inviteStaff(admin.page, {
          email: e2eEnvironment.reviewsSupportEmail,
          fullName: 'Support Đánh Giá E2E',
          roleCode: 'support',
        });
        await activateStaffAccount(support.page, e2eEnvironment.reviewsSupportEmail);

        // Mỗi khách đặt trong context riêng, vì giỏ hàng và đơn gắn với tài khoản đang đăng nhập.
        const customers = [
          { session: visibleCustomer, email: e2eEnvironment.reviewsVisibleCustomerEmail },
          { session: hiddenCustomer, email: e2eEnvironment.reviewsHiddenCustomerEmail },
          { session: unreceivedCustomer, email: e2eEnvironment.reviewsUnreceivedCustomerEmail },
        ];
        const orders: { code: string; trackingCode: string }[] = [];
        for (const customer of customers) {
          await loginSeededCustomer(customer.session.page, customer.email);
          const code = await placeCodOrderByApi(customer.session.page, product);
          const trackingCode = await confirmAndShipOrderByApi(admin.page, code);
          orders.push({ code, trackingCode });
        }

        // Hai khách đầu nhận hàng. Khách thứ ba dừng ở `shipping`, trạng thái sát `delivered` nhất.
        const [visibleOrder, hiddenOrder, unreceivedOrder] = orders;
        await deliverShipmentByWebhook(admin.page, visibleOrder!.trackingCode);
        await deliverShipmentByWebhook(admin.page, hiddenOrder!.trackingCode);

        expect((await readAdminOrder(admin.page, visibleOrder!.code)).status).toBe('delivered');
        expect((await readAdminOrder(admin.page, hiddenOrder!.code)).status).toBe('delivered');
        expect((await readAdminOrder(admin.page, unreceivedOrder!.code)).status).toBe('shipping');
      });

      await test.step('Khách chưa nhận hàng không thấy form đánh giá và API từ chối 403', async () => {
        const region = await openProductReviews(unreceivedCustomer, product);

        // Chờ lý do hiện ra. Lúc đó trạng thái khối viết đánh giá đã chốt, nên kiểm vắng mặt bên dưới không xanh giả.
        await expect(
          region.getByText('Chỉ khách đã nhận sản phẩm này mới viết được đánh giá.'),
        ).toBeVisible();
        await expect(region.getByRole('heading', { name: 'Viết đánh giá của bạn' })).toHaveCount(0);
        await expect(region.getByRole('button', { name: 'Gửi đánh giá' })).toHaveCount(0);

        // Gọi thẳng API để chứng minh server tự chặn, không chỉ giao diện ẩn form.
        const attempt = await unreceivedCustomer.page.request.post(
          `/api/products/${product.id}/reviews`,
          { headers: webOriginHeaders, data: { rating: 5, body: 'Đánh giá khi chưa nhận hàng' } },
        );
        expect(attempt.status(), await attempt.text()).toBe(403);
        expect((await attempt.json()).error.code).toBe('REVIEW_NOT_ALLOWED');
      });

      await test.step('Hai khách đã nhận hàng viết đánh giá 5 sao và 2 sao', async () => {
        // Đánh giá 5 sao tạo bằng API cho nhanh. Đánh giá 2 sao viết qua form như khách thật.
        const created = await visibleCustomer.page.request.post(
          `/api/products/${product.id}/reviews`,
          { headers: webOriginHeaders, data: { rating: 5, body: visibleBody } },
        );
        expect(created.status(), await created.text()).toBe(201);

        const region = await openProductReviews(hiddenCustomer, product);
        await expect(region.getByRole('heading', { name: 'Viết đánh giá của bạn' })).toBeVisible();
        await region.getByRole('radio', { name: '2 sao' }).check();
        await region.getByLabel(/^Nhận xét/).fill(hiddenBody);
        await region.getByRole('button', { name: 'Gửi đánh giá' }).click();
        await expect(region.getByRole('heading', { name: 'Đánh giá của bạn' })).toBeVisible();
      });

      await test.step('Khách vãng lai thấy cả hai đánh giá, điểm trung bình 3,5 và 2 đánh giá', async () => {
        const guest = await openIsolatedSession(browser);
        guests.push(guest);
        const region = await openProductReviews(guest, product);

        await expect(region.getByText(visibleBody)).toBeVisible();
        await expect(region.getByText(hiddenBody)).toBeVisible();
        await expect(
          region.getByRole('img', { name: 'Điểm trung bình 3,5 trên 5 sao' }),
        ).toBeVisible();
        await expect(region.getByText('2 đánh giá', { exact: true })).toBeVisible();
      });

      await test.step('Support ẩn đánh giá 2 sao ở hàng chờ kiểm duyệt', async () => {
        await support.page.goto('/admin/reviews');
        await expect(
          support.page.getByRole('heading', { level: 1, name: 'Kiểm duyệt đánh giá' }),
        ).toBeVisible();

        // Hàng chờ có đánh giá của mọi sản phẩm. Lọc theo chuỗi duy nhất để chỉ thao tác đúng dòng.
        const row = support.page.getByRole('row').filter({ hasText: hiddenBody });
        await expect(row).toContainText('Đang hiện');
        await row.getByRole('button', { name: /^Ẩn đánh giá của / }).click();

        const dialog = support.page.getByRole('dialog', { name: 'Ẩn đánh giá' });
        await dialog.getByRole('radio', { name: 'Spam hoặc quảng cáo' }).check();
        await dialog.getByRole('button', { name: 'Ẩn đánh giá', exact: true }).click();
        await expect(dialog).toBeHidden();

        await expect(
          support.page.getByRole('status').filter({ hasText: 'Đã ẩn đánh giá.' }),
        ).toBeVisible();
        await expect(row).toContainText('Đã ẩn');
        await expect(row).toContainText('Spam hoặc quảng cáo');
      });

      await test.step('Khách vãng lai mở lại: đánh giá bị ẩn biến mất, điểm là 5,0 và còn 1 đánh giá', async () => {
        const guest = await openIsolatedSession(browser);
        guests.push(guest);
        const region = await openProductReviews(guest, product);

        // Chờ đánh giá còn lại hiện ra. Lúc đó danh sách đã tải xong, nên kiểm vắng mặt bên dưới không xanh giả.
        await expect(region.getByText(visibleBody)).toBeVisible();
        await expect(region.getByText(hiddenBody)).toHaveCount(0);
        await expect(
          region.getByRole('img', { name: 'Điểm trung bình 5,0 trên 5 sao' }),
        ).toBeVisible();
        await expect(region.getByText('1 đánh giá', { exact: true })).toBeVisible();
      });

      const allSessions = [
        ...namedSessions,
        ...guests.map((guest, index) => [`khách vãng lai ${index + 1}`, guest] as const),
      ];
      for (const [label, session] of allSessions) {
        expect(session.consoleIssues, `Console của ${label} phải sạch`).toEqual([]);
      }
    } finally {
      for (const [, session] of namedSessions) {
        await session.context.close();
      }
      for (const guest of guests) {
        await guest.context.close();
      }
    }
  });
});
