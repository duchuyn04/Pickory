import { expect, test, type Page } from '@playwright/test';
import { placeCodOrder, prepareShipping, seedProduct } from './admin-order-fixtures';
import { collectConsoleIssues, loginAdmin } from './catalog-admin-flow';
import {
  openIsolatedSession,
  sendGhnWebhook,
  setFakeGhnStatus,
  stubProductImages,
  stubWebFonts,
} from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';
import { activateStaffAccount, inviteStaff } from './staff-fixtures';

const webOriginHeaders = { Origin: e2eEnvironment.webOrigin };

// Kiện hàng hợp lệ để `support` gọi thẳng API tạo vận đơn. Quyền được kiểm trước nội dung, nên giá trị chỉ cần đúng dạng.
const sampleParcel = { weightGrams: 500, lengthCm: 20, widthCm: 15, heightCm: 10 };

// Chi tiết đơn nhân viên thấy ở `/admin/orders/<mã>`. Chờ tiêu đề đơn rồi mới đọc, vì trang tải dữ liệu bất đồng bộ.
async function openAdminOrder(page: Page, orderCode: string) {
  await page.goto(`/admin/orders/${orderCode}`);
  await expect(
    page.getByRole('heading', { level: 1, name: `Đơn hàng ${orderCode}` }),
  ).toBeVisible();
}

// Khối "Vận đơn" ở cột bên của trang chi tiết đơn. Trả giá trị của một dòng theo nhãn, ví dụ "Mã vận đơn".
function shipmentValue(page: Page, label: string) {
  return page
    .locator('section.card')
    .filter({ has: page.getByRole('heading', { name: 'Vận đơn', exact: true }) })
    .locator('dl > div')
    .filter({ has: page.getByText(label, { exact: true }) })
    .locator('dd');
}

// Mỗi thao tác tối đa 30 giây. Thao tác kẹt báo lỗi ngay tại dòng kẹt thay vì chờ hết thời gian của cả test.
test.use({ actionTimeout: 30000 });
test.describe('COM-E2E3: sales xác nhận và tạo vận đơn', () => {
  test('sales xác nhận, tạo vận đơn, GHN giao hàng, và support không có quyền thao tác', async ({
    browser,
    page,
  }) => {
    // Test gồm tạo hai nhân viên qua thư mời và một đơn đi hết vòng đời, nên cần nhiều thời gian hơn mặc định.
    test.setTimeout(300000);

    const customerIssues = collectConsoleIssues(page);
    await stubWebFonts(page);

    const admin = await openIsolatedSession(browser);
    const sales = await openIsolatedSession(browser);
    const support = await openIsolatedSession(browser);
    const sessions = [admin, sales, support];

    let orderCode = '';
    let trackingCode = '';

    try {
      await test.step('Admin chuẩn bị sản phẩm, kho gửi, và mời sales cùng support', async () => {
        await loginAdmin(admin.page);
        const product = await seedProduct(admin.page);
        const address = await prepareShipping(admin.page);

        await inviteStaff(admin.page, {
          email: e2eEnvironment.fulfillmentSalesEmail,
          fullName: 'Sales Giao Hàng E2E',
          roleCode: 'sales',
        });
        await inviteStaff(admin.page, {
          email: e2eEnvironment.fulfillmentSupportEmail,
          fullName: 'Support Giao Hàng E2E',
          roleCode: 'support',
        });
        await activateStaffAccount(sales.page, e2eEnvironment.fulfillmentSalesEmail);
        await activateStaffAccount(support.page, e2eEnvironment.fulfillmentSupportEmail);

        // Khách đặt ở bước này vì sản phẩm và kho gửi vừa sẵn sàng. Đơn COD để `sales` xác nhận ngay, không chờ tiền.
        await stubProductImages(page);
        orderCode = await placeCodOrder(page, product, address);
        expect(orderCode).toMatch(/^PK/);
      });

      await test.step('Sales bấm mục "Đơn hàng", tìm thấy đơn và mở trang chi tiết thật', async () => {
        const navigation = sales.page.getByRole('navigation', { name: 'Quản trị', exact: true });
        await navigation.getByRole('link', { name: 'Đơn hàng', exact: true }).click();
        await expect(sales.page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/orders`);
        await expect(
          sales.page.getByRole('heading', { level: 1, name: 'Quản lý đơn hàng' }),
        ).toBeVisible();

        // Danh sách có đơn của nhiều spec. Tìm theo mã để chỉ còn đúng đơn vừa đặt.
        await sales.page
          .getByLabel('Tìm theo mã đơn, tên, số điện thoại hoặc email', { exact: true })
          .fill(orderCode);
        const rows = sales.page.locator('table.order-table tbody tr');
        await expect(rows).toHaveCount(1);
        await expect(rows.first()).toContainText('Trần Thị Bình');

        await rows.first().getByRole('link', { name: orderCode, exact: true }).click();
        await expect(sales.page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/orders/${orderCode}`);
        await expect(
          sales.page.getByRole('heading', { level: 1, name: `Đơn hàng ${orderCode}` }),
        ).toBeVisible();
        await expect(sales.page.getByLabel('Tổng quan đơn')).toContainText('Chờ xác nhận');
      });

      await test.step('Sales xác nhận đơn và thấy hai nút Tạo vận đơn, Hủy đơn', async () => {
        await sales.page.getByRole('button', { name: 'Xác nhận đơn', exact: true }).click();
        const dialog = sales.page.getByRole('dialog');
        await dialog.getByRole('button', { name: 'Xác nhận', exact: true }).click();
        await expect(dialog).toBeHidden();
        await expect(sales.page.getByLabel('Tổng quan đơn')).toContainText('Đã xác nhận');

        // Hai nút này là chuẩn so sánh cho bước của `support`. Nếu `sales` không thấy, bước đó không chứng minh gì về quyền.
        await expect(
          sales.page.getByRole('button', { name: 'Tạo vận đơn', exact: true }),
        ).toBeVisible();
        await expect(
          sales.page.getByRole('button', { name: 'Hủy đơn', exact: true }),
        ).toBeVisible();
      });

      await test.step('Support mở cùng đơn thì thấy chi tiết nhưng không thấy nút hủy và nút tạo vận đơn', async () => {
        await openAdminOrder(support.page, orderCode);
        await expect(support.page.getByLabel('Tổng quan đơn')).toContainText('Đã xác nhận');
        await expect(support.page.getByText('Trần Thị Bình')).toBeVisible();

        // Menu chỉ hiện sau khi hồ sơ quyền của `support` tải xong. Chờ menu trước, để bước kiểm vắng mặt bên dưới không xanh giả.
        const navigation = support.page.getByRole('navigation', { name: 'Quản trị', exact: true });
        await expect(navigation.getByRole('link', { name: 'Đơn hàng', exact: true })).toBeVisible();

        await expect(
          support.page.getByRole('button', { name: 'Tạo vận đơn', exact: true }),
        ).toHaveCount(0);
        await expect(
          support.page.getByRole('button', { name: 'Hủy đơn', exact: true }),
        ).toHaveCount(0);
      });

      await test.step('Support gọi API hủy đơn và API tạo vận đơn thì nhận 403, đơn không đổi', async () => {
        const cancellation = await support.page.request.post(
          `/api/admin/orders/${orderCode}/cancellation`,
          { headers: webOriginHeaders, data: { reason: 'Support thử hủy đơn' } },
        );
        expect(cancellation.status(), await cancellation.text()).toBe(403);

        const shipment = await support.page.request.post(
          `/api/admin/orders/${orderCode}/shipment`,
          {
            headers: webOriginHeaders,
            data: { parcel: sampleParcel },
          },
        );
        expect(shipment.status(), await shipment.text()).toBe(403);

        // Đọc lại đơn bằng quyền của `sales`. Hai lệnh bị từ chối không được làm đổi trạng thái hay tạo vận đơn.
        const detail = await sales.page.request.get(`/api/admin/orders/${orderCode}`);
        expect(detail.status(), await detail.text()).toBe(200);
        const order = (await detail.json()).data as { status: string; shipment: unknown };
        expect(order.status).toBe('confirmed');
        expect(order.shipment).toBeNull();
      });

      await test.step('Sales tạo vận đơn thì đơn sang Đang giao hàng và trang hiện mã vận đơn', async () => {
        await sales.page.reload();
        await sales.page.getByRole('button', { name: 'Tạo vận đơn', exact: true }).click();
        const dialog = sales.page.getByRole('dialog');
        // Kiện gợi ý do backend dựng từ dòng hàng, nên bốn ô đã có giá trị và sales bấm tạo ngay.
        await dialog.getByRole('button', { name: 'Tạo vận đơn', exact: true }).click();
        await expect(dialog).toBeHidden();
        await expect(sales.page.getByLabel('Tổng quan đơn')).toContainText('Đang giao hàng');

        const trackingValue = shipmentValue(sales.page, 'Mã vận đơn');
        await expect(trackingValue).toHaveText(/^FAKE\d{6}$/);
        trackingCode = (await trackingValue.innerText()).trim();
        await expect(shipmentValue(sales.page, 'Trạng thái')).toHaveText('Chờ lấy hàng');
      });

      await test.step('GHN giả báo đã giao, webhook thật tới thì đơn sang Đã giao hàng', async () => {
        // Đặt trạng thái ở phía GHN giả trước. Webhook chỉ mang mã vận đơn, API tự hỏi trạng thái thật từ client GHN.
        const statusResponse = await setFakeGhnStatus(admin.page, trackingCode, 'delivered');
        expect(statusResponse.status(), await statusResponse.text()).toBe(200);

        const webhookResponse = await sendGhnWebhook(admin.page, trackingCode);
        expect(webhookResponse.status(), await webhookResponse.text()).toBe(200);

        // Trang không tự tải lại sau webhook, nên tải lại để đọc trạng thái mới từ API.
        await openAdminOrder(sales.page, orderCode);
        await expect(sales.page.getByLabel('Tổng quan đơn')).toContainText('Đã giao hàng');
        await expect(shipmentValue(sales.page, 'Mã vận đơn')).toHaveText(trackingCode);
        await expect(shipmentValue(sales.page, 'Trạng thái')).toHaveText('Đã giao');
      });

      for (const [label, issues] of [
        ['admin', admin.consoleIssues],
        ['sales', sales.consoleIssues],
        ['support', support.consoleIssues],
        ['khách', customerIssues],
      ] as const) {
        expect(issues, `Console của ${label} phải sạch`).toEqual([]);
      }
    } finally {
      for (const session of sessions) {
        await session.context.close();
      }
    }
  });
});
