import { copyFile, mkdir, mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, test, type Download, type Page } from '@playwright/test';
import { collectConsoleIssues, loginAdmin } from './catalog-admin-flow';
import { stubWebFonts } from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';
import { activateStaffAccount, inviteStaff } from './staff-fixtures';

const EXPORT_LABEL = 'Xuất Excel';
const XLSX_CONTENT_TYPE = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
const STAFF_EVIDENCE_VIDEO = '01-nhan-vien-khong-thay-nut-xuat.webm';
// Tên file làm tròn xuống phút và sinh ra ngay trước lúc kiểm tra, nên ngưỡng 10 phút đủ cho thời gian tải.
const MAX_FILE_AGE_MS = 10 * 60 * 1000;
const VIETNAM_OFFSET_MS = 7 * 60 * 60 * 1000;

// Video bằng chứng nằm ngoài thư mục ứng dụng frontend theo cấu trúc lưu trữ của dự án.
const EVIDENCE_DIRECTORY = fileURLToPath(
  new URL('../../../evidence/exports-printing/EXP-E2E1', import.meta.url),
);

interface ExportPageTarget {
  label: string;
  url: string;
  heading: string;
  exportPathname: string;
  filePrefix: string;
  filterQuery: string;
  selectUiFilter?: (page: Page) => Promise<void>;
  expectedQuery: Record<string, string>;
}

const EXPORT_PAGES: readonly ExportPageTarget[] = [
  {
    label: 'đơn hàng',
    url: '/admin/orders',
    heading: 'Quản lý đơn hàng',
    exportPathname: '/api/admin/exports/orders',
    filePrefix: 'don-hang',
    filterQuery: '?status=delivered',
    expectedQuery: { status: 'delivered' },
  },
  {
    label: 'sản phẩm',
    url: '/admin/products',
    heading: 'Sản phẩm',
    exportPathname: '/api/admin/exports/products',
    filePrefix: 'san-pham',
    filterQuery: '?lowStock=true',
    expectedQuery: { lowStock: 'true' },
  },
  {
    label: 'khách hàng',
    url: '/admin/customers',
    heading: 'Quản lý Khách hàng',
    exportPathname: '/api/admin/exports/customers',
    filePrefix: 'khach-hang',
    filterQuery: '?search=an',
    expectedQuery: { search: 'an' },
  },
  {
    label: 'giao dịch',
    url: '/admin/payments',
    heading: 'Đối soát thanh toán',
    exportPathname: '/api/admin/exports/payment-transactions',
    filePrefix: 'giao-dich-sepay',
    filterQuery: '',
    selectUiFilter: async (page: Page) => {
      // Màn mặc định lọc chưa khớp, nên chọn Đã khớp để kiểm tra tham số thật đi vào request xuất.
      await page.getByLabel('Trạng thái khớp', { exact: true }).selectOption('matched');
      await expect(page).toHaveURL(/matchStatus=matched/);
    },
    // Request xuất phải mang đúng giá trị `matched` vừa chọn trên giao diện.
    expectedQuery: { matchStatus: 'matched' },
  },
];

function exportButton(page: Page) {
  return page.getByRole('button', { name: EXPORT_LABEL, exact: true });
}

// Đọc toàn bộ byte từ luồng tải về để kiểm tra chữ ký định dạng file độc lập với đường dẫn tạm.
async function readDownloadedBytes(download: Download): Promise<Buffer> {
  const stream = await download.createReadStream();
  const chunks: Buffer[] = [];
  for await (const chunk of stream) {
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
  }
  return Buffer.concat(chunks);
}

// Kiểm tra tên file khớp tiền tố riêng và phần thời gian là ngày giờ có thật theo giờ Việt Nam.
function expectValidExportFileName(fileName: string, expectedPrefix: string) {
  const pattern = new RegExp(
    `^${expectedPrefix}-(\\d{4})(\\d{2})(\\d{2})-(\\d{2})(\\d{2})\\.xlsx$`,
  );
  const match = pattern.exec(fileName);
  expect(
    match,
    `Tên file "${fileName}" không khớp mẫu của tiền tố "${expectedPrefix}".`,
  ).not.toBeNull();

  const [, yearText, monthText, dayText, hourText, minuteText] = match!;
  const year = Number(yearText);
  const month = Number(monthText);
  const day = Number(dayText);
  const hour = Number(hourText);
  const minute = Number(minuteText);

  // Dựng lại ngày giờ rồi đối chiếu từng thành phần để bắt lỗi ngày không tồn tại hoặc giờ phút sai.
  const vietnamAsUtc = new Date(Date.UTC(year, month - 1, day, hour, minute));
  expect(vietnamAsUtc.getUTCFullYear()).toBe(year);
  expect(vietnamAsUtc.getUTCMonth()).toBe(month - 1);
  expect(vietnamAsUtc.getUTCDate()).toBe(day);
  expect(vietnamAsUtc.getUTCHours()).toBe(hour);
  expect(vietnamAsUtc.getUTCMinutes()).toBe(minute);

  // Quy đổi giờ Việt Nam về giờ chuẩn quốc tế (UTC) để đối chiếu với đồng hồ hiện tại.
  const parsedUtcMs = vietnamAsUtc.getTime() - VIETNAM_OFFSET_MS;
  expect(Math.abs(parsedUtcMs - Date.now())).toBeLessThanOrEqual(MAX_FILE_AGE_MS);
}

for (const target of EXPORT_PAGES) {
  test(`trang ${target.label}: xuất Excel theo bộ lọc với backend thật`, async ({ page }) => {
    // Cho phép tối đa hai phút vì ca phải đăng nhập admin và tải file từ API thật.
    test.setTimeout(120000);
    const consoleIssues = collectConsoleIssues(page);
    // Chặn tải font ngoài để lỗi mạng không tạo cảnh báo giả trong console.
    await stubWebFonts(page);
    await loginAdmin(page);

    await test.step('Mở trang với bộ lọc và chờ nút xuất hiển thị', async () => {
      // Đặt khung nhìn 1280 px khớp bố cục máy tính nơi thanh đầu trang hiện nút `Xuất Excel`.
      await page.setViewportSize({ width: 1280, height: 800 });
      await page.goto(`${target.url}${target.filterQuery}`);
      await expect(page.getByRole('heading', { level: 1, name: target.heading })).toBeVisible();
      // Chọn bộ lọc trên UI khi màn cần thao tác trực tiếp thay vì truyền tham số qua URL.
      if (target.selectUiFilter) {
        await target.selectUiFilter(page);
      }
      // Chờ nút hiện nhằm xác nhận phiên admin đã nạp xong và có đủ quyền.
      await expect(exportButton(page)).toBeVisible();
    });

    await test.step('Bấm nút xuất và kiểm tra phản hồi, tên file cùng chữ ký PK', async () => {
      // Đăng ký chờ sự kiện trước khi bấm để không bỏ lỡ phản hồi nhanh từ server cục bộ.
      const downloadPromise = page.waitForEvent('download');
      const responsePromise = page.waitForResponse(
        (response) => new URL(response.url()).pathname === target.exportPathname,
      );

      await exportButton(page).click();
      const [download, response] = await Promise.all([downloadPromise, responsePromise]);

      // Chỉ mã 200 kèm kiểu nội dung của bảng tính mới tạo ra file tải về hợp lệ.
      expect(response.status()).toBe(200);
      expect(response.headers()['content-type']).toBe(XLSX_CONTENT_TYPE);

      // Đối chiếu tham số truy vấn của request thật với bộ lọc đang chọn trên UI.
      const requestUrl = new URL(response.request().url());
      expect(Object.fromEntries(requestUrl.searchParams.entries())).toEqual(target.expectedQuery);

      const suggestedFileName = download.suggestedFilename();
      expectValidExportFileName(suggestedFileName, target.filePrefix);

      // Header phản hồi phải mang cùng tên file để chứng minh frontend lấy tên từ server.
      const contentDisposition = response.headers()['content-disposition'] ?? '';
      expect(contentDisposition).toContain(`filename="${suggestedFileName}"`);
      expect(contentDisposition).toContain(`filename*=UTF-8''${suggestedFileName}`);

      // Mọi file Excel hợp lệ đều là file nén ZIP (định dạng nén có chữ ký `PK`) dài hơn hai byte.
      const fileBytes = await readDownloadedBytes(download);
      expect(fileBytes.length).toBeGreaterThan(2);
      expect(fileBytes[0]).toBe(0x50);
      expect(fileBytes[1]).toBe(0x4b);
    });

    await test.step('Nút xuất mở lại sau khi tải xong và console sạch', async () => {
      await expect(exportButton(page)).toBeEnabled();
      expect(consoleIssues).toEqual([]);
    });
  });
}

test('nhân viên thiếu reports:export mở được bốn danh sách nhưng không thấy nút xuất', async ({
  browser,
}) => {
  // Cho phép tối đa ba phút vì ca phải tạo nhân viên, kích hoạt qua thư và quay bốn trang.
  test.setTimeout(180000);
  // Ghi video tạm ngoài kho mã nguồn để không để lại file thừa trong thư mục kết quả test.
  const videoTempDir = await mkdtemp(path.join(os.tmpdir(), 'pickory-export-video-'));

  // Tách hai ngữ cảnh trình duyệt nhằm giữ cookie phiên của admin và nhân viên độc lập.
  const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  const adminPage = await adminContext.newPage();
  const adminIssues = collectConsoleIssues(adminPage);
  await stubWebFonts(adminPage);

  // Tạo email duy nhất theo dấu thời gian để lần chạy sau không đụng tài khoản cũ trong DB.
  const staffEmail = `e2e-exports-${Date.now().toString(36)}@pickory.local`;
  // Chỉ quay video ở ngữ cảnh nhân viên để giữ đúng một file bằng chứng cho tiêu chí ẩn nút.
  const staffContext = await browser.newContext({
    baseURL: e2eEnvironment.webOrigin,
    viewport: { width: 1280, height: 720 },
    recordVideo: {
      dir: videoTempDir,
      size: { width: 1280, height: 720 },
    },
  });
  let staffContextClosed = false;
  const staffPage = await staffContext.newPage();
  const staffIssues = collectConsoleIssues(staffPage);
  await stubWebFonts(staffPage);

  try {
    await loginAdmin(adminPage);
    // Vai trò `sales` đọc được bốn danh sách nhưng thiếu `reports:export`, nên phép thử kiểm tra đúng một quyền thiếu.
    await inviteStaff(adminPage, {
      email: staffEmail,
      fullName: 'Nhân Viên Bán Hàng',
      roleCode: 'sales',
    });
    await activateStaffAccount(staffPage, staffEmail);
    // Chờ nút cookie hiện rồi bấm đóng để khung hình video không bị che phần cuối trang.
    const consentButton = staffPage.getByRole('button', { name: 'Chỉ cần thiết' });
    await consentButton.click();
    await expect(consentButton).toBeHidden();

    for (const target of EXPORT_PAGES) {
      await test.step(`Kiểm tra trang ${target.label} ẩn nút xuất với nhân viên`, async () => {
        await staffPage.goto(target.url);
        await expect(
          staffPage.getByRole('heading', { level: 1, name: target.heading }),
        ).toBeVisible();

        if (target.exportPathname === '/api/admin/exports/payment-transactions') {
          // Đảm bảo đang đứng ở tab `SePay` nơi nút xuất tồn tại với admin.
          await expect(staffPage.getByRole('tab', { name: 'SePay', exact: true })).toHaveAttribute(
            'aria-selected',
            'true',
          );
        }

        // Chờ danh sách hoặc trạng thái rỗng hiện xong để kiểm tra ẩn nút không cho kết quả đạt sai.
        await expect(
          staffPage.locator('table, .empty-state:not([role="status"])').first(),
        ).toBeVisible();
        await expect(exportButton(staffPage)).toHaveCount(0);

        // Dừng hai giây sau khi nạp xong để người xem video quan sát rõ tiêu đề và thanh công cụ.
        await staffPage.waitForTimeout(2000);
      });
    }

    // Kiểm tra cả hai ngữ cảnh đều không phát sinh lỗi hay cảnh báo console trong suốt luồng.
    expect(adminIssues).toEqual([]);
    expect(staffIssues).toEqual([]);

    const staffVideo = staffPage.video();
    // Đóng ngữ cảnh nhân viên trước khi đọc đường dẫn vì Playwright chỉ hoàn tất ghi file khi đóng.
    await staffContext.close();
    staffContextClosed = true;

    // Khẳng định trình quay đã gắn video cho ngữ cảnh nhân viên thay vì bỏ qua im lặng nếu thiếu.
    expect(staffVideo).not.toBeNull();
    // Sao chép video sang thư mục bằng chứng của dự án để lưu lại kết quả chạy thật.
    await mkdir(EVIDENCE_DIRECTORY, { recursive: true });
    const recordedPath = await staffVideo!.path();
    await copyFile(recordedPath, path.join(EVIDENCE_DIRECTORY, STAFF_EVIDENCE_VIDEO));
  } finally {
    // Đóng ngữ cảnh nhân viên nếu ca lỗi trước bước lưu video để không rò rỉ tiến trình.
    if (!staffContextClosed) {
      await staffContext.close();
    }
    // Đóng ngữ cảnh admin sau khi xong nhằm giải phóng bộ nhớ trình duyệt.
    await adminContext.close();
    // Xóa thư mục video tạm ngoài kho mã nguồn để không để lại file rác trên máy.
    await rm(videoTempDir, { recursive: true, force: true });
  }
});
