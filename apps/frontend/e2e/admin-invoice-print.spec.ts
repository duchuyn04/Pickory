import { expect, test, type Page } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { inflateSync } from 'node:zlib';
import type { OrderInvoicesResponse } from '../src/features/commerce/admin/orders/order-invoices-schema';
import { collectConsoleIssues, loginAdmin } from './catalog-admin-flow';
import { installFixtureRoute } from './fixture-route';

const EVIDENCE_DIRECTORY = resolve(process.cwd(), '../../evidence/exports-printing/EXP-UI5');
const ORDER_INVOICES_API_PATH = '/api/admin/order-invoices';

declare global {
  interface Window {
    __printCallCount?: number;
  }
}

// Chặn hộp thoại in thật của trình duyệt và đếm số lần trang tự gọi `window.print`.
async function stubWindowPrint(page: Page): Promise<void> {
  await page.addInitScript(() => {
    window.__printCallCount = 0;
    window.print = () => {
      window.__printCallCount = (window.__printCallCount ?? 0) + 1;
    };
  });
}

async function readPrintCallCount(page: Page): Promise<number> {
  return page.evaluate(() => window.__printCallCount ?? 0);
}

// Đếm số trang trong tệp PDF (Portable Document Format) theo quyết định kiến trúc của đợt việc.
function countPdfPages(pdfBuffer: Buffer): number {
  const matches = pdfBuffer.toString('latin1').match(/\/Type\s*\/Page(?![s\w])/g);
  return matches?.length ?? 0;
}

// Đọc khung cắt lề trang đầu tiên (đơn vị 0.24 pt) trong luồng PDF để kiểm lề `@page` 12 mm.
function readFirstPdfPageClipMargin(pdfBuffer: Buffer): { x: number; y: number } | null {
  const latin1 = pdfBuffer.toString('latin1');
  const firstPage = /\/Type\s*\/Page(?![s\w])[\s\S]*?\/Contents\s+(\d+)\s+0\s+R/.exec(latin1);
  if (!firstPage) {
    return null;
  }
  const objPattern = new RegExp(
    `${firstPage[1]}\\s+0\\s+obj[\\s\\S]*?stream\\r?\\n([\\s\\S]*?)\\r?\\nendstream`,
  );
  const objMatch = objPattern.exec(latin1);
  if (!objMatch) {
    return null;
  }
  const decoded = inflateSync(Buffer.from(objMatch[1]!, 'latin1')).toString('latin1');
  const rectMatch = /([\d.]+)\s+([\d.]+)\s+[\d.]+\s+[\d.]+\s+re\s*\r?\n\s*W\*\s+n/.exec(decoded);
  if (!rectMatch) {
    return null;
  }
  return { x: Number.parseFloat(rectMatch[1]!), y: Number.parseFloat(rectMatch[2]!) };
}

// Giải nén luồng nội dung PDF và giải mã chữ từng trang qua bảng ToUnicode của font.
function extractPdfPageTexts(pdfBuffer: Buffer): string[] {
  const latin1 = pdfBuffer.toString('latin1');
  const objects = new Map<string, string>();
  const objectPattern = /(\d+)\s+0\s+obj\b([\s\S]*?)endobj/g;
  let objectMatch: RegExpExecArray | null;
  while ((objectMatch = objectPattern.exec(latin1)) !== null) {
    objects.set(objectMatch[1]!, objectMatch[2]!);
  }

  const readObjectStream = (objectId: string): string => {
    const body = objects.get(objectId) ?? '';
    const streamMatch = /stream\r?\n([\s\S]*?)\r?\nendstream/.exec(body);
    if (!streamMatch) {
      return '';
    }
    const raw = Buffer.from(streamMatch[1]!, 'latin1');
    try {
      return inflateSync(raw).toString('latin1');
    } catch {
      return streamMatch[1]!;
    }
  };

  const parseToUnicodeMap = (cmapText: string): Map<string, string> => {
    const glyphToChar = new Map<string, string>();
    for (const block of cmapText.matchAll(/beginbfchar([\s\S]*?)endbfchar/g)) {
      for (const pair of block[1]!.matchAll(/<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>/g)) {
        glyphToChar.set(
          pair[1]!.toUpperCase(),
          String.fromCodePoint(Number.parseInt(pair[2]!, 16)),
        );
      }
    }
    for (const block of cmapText.matchAll(/beginbfrange([\s\S]*?)endbfrange/g)) {
      for (const range of block[1]!.matchAll(
        /<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>/g,
      )) {
        const startGlyph = Number.parseInt(range[1]!, 16);
        const endGlyph = Number.parseInt(range[2]!, 16);
        const startUnicode = Number.parseInt(range[3]!, 16);
        const hexWidth = range[1]!.length;
        for (let glyph = startGlyph; glyph <= endGlyph; glyph += 1) {
          glyphToChar.set(
            glyph.toString(16).toUpperCase().padStart(hexWidth, '0'),
            String.fromCodePoint(startUnicode + (glyph - startGlyph)),
          );
        }
      }
    }
    return glyphToChar;
  };

  const pageObjects = [...objects.values()].filter((body) => /\/Type\s*\/Page(?![s\w])/.test(body));

  return pageObjects.map((pageBody) => {
    const fontMaps = new Map<string, Map<string, string>>();
    for (const fontRef of pageBody.matchAll(/\/(F\d+)\s+(\d+)\s+0\s+R/g)) {
      const fontBody = objects.get(fontRef[2]!) ?? '';
      const toUnicodeRef = /\/ToUnicode\s+(\d+)\s+0\s+R/.exec(fontBody);
      if (toUnicodeRef) {
        fontMaps.set(fontRef[1]!, parseToUnicodeMap(readObjectStream(toUnicodeRef[1]!)));
      }
    }

    const contentIds: string[] = [];
    const singleContent = /\/Contents\s+(\d+)\s+0\s+R/.exec(pageBody);
    if (singleContent) {
      contentIds.push(singleContent[1]!);
    } else {
      const arrayContent = /\/Contents\s*\[([^\]]+)\]/.exec(pageBody);
      if (arrayContent) {
        for (const item of arrayContent[1]!.matchAll(/(\d+)\s+0\s+R/g)) {
          contentIds.push(item[1]!);
        }
      }
    }

    const contentStream = contentIds.map(readObjectStream).join('\n');
    let currentFont = '';
    let decodedText = '';
    const tokenPattern = /\/(F\d+)\s+[\d.]+\s+Tf|<([0-9A-Fa-f]+)>\s*Tj|\[([^\]]+)\]\s*TJ/g;
    for (const token of contentStream.matchAll(tokenPattern)) {
      if (token[1]) {
        currentFont = token[1];
        continue;
      }
      const cmap = fontMaps.get(currentFont);
      if (!cmap) {
        continue;
      }
      const hexChunks = token[2]
        ? [token[2]]
        : [...token[3]!.matchAll(/<([0-9A-Fa-f]+)>/g)].map((item) => item[1]!);
      for (const hex of hexChunks) {
        for (let offset = 0; offset + 4 <= hex.length; offset += 4) {
          decodedText += cmap.get(hex.slice(offset, offset + 4).toUpperCase()) ?? '';
        }
      }
    }
    return decodedText;
  });
}

// Tạo dữ liệu giả có đúng `lineCount` dòng sản phẩm từ ví dụ gốc để kiểm phân trang A4.
function expandInvoiceLines(
  response: unknown,
  lineCount: number,
  options: { forceCancelled?: boolean } = {},
): OrderInvoicesResponse {
  const cloned = response as OrderInvoicesResponse;
  const invoice = cloned.data.invoices[0]!;
  if (options.forceCancelled) {
    invoice.status = 'cancelled';
  }
  cloned.data.invoices = [invoice];
  const templateLine = invoice.lines[0]!;
  const lines = Array.from({ length: lineCount }, (_, index) => {
    const orderNumber = String(index + 1).padStart(2, '0');
    return {
      ...templateLine,
      skuCode: `SKU-TEST-${orderNumber}`,
      name: `Sản phẩm thử ${orderNumber}`,
      optionText: index % 2 === 0 ? 'Đen, 256 GB' : null,
      quantity: 1,
      unitPrice: 100000,
      lineTotal: 100000,
    };
  });
  const subtotal = lines.reduce((sum, line) => sum + line.lineTotal, 0);
  invoice.lines = lines;
  invoice.subtotal = subtotal;
  invoice.total = subtotal + invoice.shippingFee - invoice.shippingDiscount;
  return cloned;
}

test.describe('trang in hóa đơn bán hàng', () => {
  test.beforeAll(() => {
    mkdirSync(EVIDENCE_DIRECTORY, { recursive: true });
  });

  test('hiển thị hai hóa đơn ngoài khung admin, in đen trắng một lần và không tràn ngang ở 375px', async ({
    page,
  }) => {
    const consoleIssues = collectConsoleIssues(page);
    await stubWindowPrint(page);
    await loginAdmin(page);

    await installFixtureRoute(
      page,
      'GET',
      ORDER_INVOICES_API_PATH,
      'order-invoices.json',
      'order invoices two orders with vat and cancelled',
    );

    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/admin/orders/print?codes=PK7KQ2M9XA,PK3HN8RT2B');

    const firstSheet = page.getByTestId('invoice-PK7KQ2M9XA');
    const secondSheet = page.getByTestId('invoice-PK3HN8RT2B');
    await expect(firstSheet).toBeVisible();
    await expect(secondSheet).toBeVisible();

    // Route tĩnh không bị bắt bởi trang chi tiết đơn và không dựng thanh bên quản trị.
    await expect(page.locator('#admin-sidebar')).toHaveCount(0);
    await expect(page.getByRole('heading', { name: /Đơn hàng/ })).toHaveCount(0);
    await expect(secondSheet).toContainText('ĐƠN ĐÃ HỦY');

    await expect.poll(() => readPrintCallCount(page)).toBe(1);

    await page.screenshot({
      path: resolve(EVIDENCE_DIRECTORY, '01-man-hinh-hai-hoa-don.png'),
      fullPage: true,
    });

    // Kiểm tra giao diện ở độ rộng 375 px không tràn ngang ngoài khung giấy.
    await page.setViewportSize({ width: 375, height: 812 });
    const overflow375 = await page.evaluate(
      () => document.documentElement.scrollWidth > window.innerWidth,
    );
    expect(overflow375).toBe(false);
    await page.screenshot({
      path: resolve(EVIDENCE_DIRECTORY, '07-man-hinh-375.png'),
      fullPage: true,
    });

    // Chuyển sang chế độ in để kiểm ẩn thanh công cụ và màu sắc đen trắng thuần.
    await page.setViewportSize({ width: 1280, height: 1120 });
    await page.emulateMedia({ media: 'print' });

    await expect(page.locator('.invoice-print-toolbar')).toBeHidden();
    await expect(firstSheet).toBeVisible();

    const printStyleCheck = await page.evaluate(() => {
      const sheets = Array.from(document.querySelectorAll('.invoice-sheet'));
      const nonWhiteBackgrounds: string[] = [];
      const nonBlackTexts: string[] = [];
      const shadowElements: string[] = [];
      const nonBlackHeaderBorders: string[] = [];

      for (const sheet of sheets) {
        const elements = [sheet, ...Array.from(sheet.querySelectorAll('*'))];
        for (const element of elements) {
          const style = window.getComputedStyle(element);
          const bg = style.backgroundColor;
          if (bg !== 'rgba(0, 0, 0, 0)' && bg !== 'transparent' && bg !== 'rgb(255, 255, 255)') {
            nonWhiteBackgrounds.push(`${element.tagName}.${element.className}:${bg}`);
          }
          if (style.color !== 'rgb(0, 0, 0)') {
            nonBlackTexts.push(`${element.tagName}.${element.className}:${style.color}`);
          }
          if (style.boxShadow !== 'none') {
            shadowElements.push(`${element.tagName}.${element.className}:${style.boxShadow}`);
          }
        }
        for (const headerCell of sheet.querySelectorAll('.invoice-items-table th')) {
          const borderTop = window.getComputedStyle(headerCell).borderTopColor;
          if (borderTop !== 'rgb(0, 0, 0)') {
            nonBlackHeaderBorders.push(borderTop);
          }
        }
      }
      const logoFilters = Array.from(document.querySelectorAll('.invoice-logo')).map(
        (logo) => window.getComputedStyle(logo).filter,
      );
      return {
        nonWhiteBackgrounds,
        nonBlackTexts,
        shadowElements,
        nonBlackHeaderBorders,
        logoFilters,
      };
    });

    expect(printStyleCheck.nonWhiteBackgrounds).toEqual([]);
    expect(printStyleCheck.nonBlackTexts).toEqual([]);
    expect(printStyleCheck.shadowElements).toEqual([]);
    expect(printStyleCheck.nonBlackHeaderBorders).toEqual([]);
    expect(printStyleCheck.logoFilters.every((value) => value.includes('grayscale'))).toBe(true);

    // Chụp đúng phần tử hóa đơn 1 theo chiều cao nguyên để tránh dính 1 px viền đầu của tờ 2.
    const firstBox = await firstSheet.boundingBox();
    expect(firstBox).not.toBeNull();
    await page.screenshot({
      path: resolve(EVIDENCE_DIRECTORY, '02-ban-in-hoa-don-vat.png'),
      clip: {
        x: Math.ceil(firstBox!.x),
        y: Math.ceil(firstBox!.y),
        width: Math.floor(firstBox!.width),
        height: Math.floor(firstBox!.height) - 1,
      },
    });
    await secondSheet.screenshot({
      path: resolve(EVIDENCE_DIRECTORY, '03-ban-in-don-da-huy.png'),
    });

    expect(consoleIssues).toEqual([]);
  });

  test('in tên Pickory và ẩn dòng trống khi shop chưa nhập thông tin', async ({ page }) => {
    const consoleIssues = collectConsoleIssues(page);
    await stubWindowPrint(page);
    await loginAdmin(page);

    await installFixtureRoute(
      page,
      'GET',
      ORDER_INVOICES_API_PATH,
      'order-invoices.json',
      'order invoices shop profile not set',
    );

    await page.setViewportSize({ width: 1280, height: 1000 });
    await page.goto('/admin/orders/print?codes=PK3HN8RT2B');

    const shopInfo = page.locator('.invoice-shop-info');
    await expect(shopInfo).toBeVisible();
    await expect(shopInfo).toContainText('Pickory');
    await expect(shopInfo).not.toContainText('Địa chỉ:');
    await expect(shopInfo).not.toContainText('Điện thoại:');
    await expect(shopInfo).not.toContainText('Mã số thuế:');

    await page.emulateMedia({ media: 'print' });
    await page.screenshot({
      path: resolve(EVIDENCE_DIRECTORY, '04-ban-in-shop-trong.png'),
      fullPage: true,
    });

    expect(consoleIssues).toEqual([]);
  });

  test('lỗi 404 hiện mã đơn thiếu và không gọi window.print', async ({ page }) => {
    const consoleIssues = collectConsoleIssues(page, [
      { urlPattern: /\/api\/admin\/order-invoices/, status: 404 },
    ]);
    await stubWindowPrint(page);
    await loginAdmin(page);

    await installFixtureRoute(
      page,
      'GET',
      ORDER_INVOICES_API_PATH,
      'order-invoices.json',
      'order invoices unknown code',
    );

    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/admin/orders/print?codes=PK7KQ2M9XA,PKZZZZZZZZ');

    const alert = page.getByRole('alert');
    await expect(alert).toBeVisible();
    await expect(alert).toContainText('Không tìm thấy một số đơn hàng.');
    await expect(alert).toContainText('PKZZZZZZZZ');
    expect(await readPrintCallCount(page)).toBe(0);

    await page.screenshot({
      path: resolve(EVIDENCE_DIRECTORY, '05-loi-404.png'),
      fullPage: true,
    });

    expect(consoleIssues).toEqual([]);
  });

  test('lỗi 403 hiện ForbiddenNotice và không gọi window.print', async ({ page }) => {
    const consoleIssues = collectConsoleIssues(page, [
      { urlPattern: /\/api\/admin\/order-invoices/, status: 403 },
    ]);
    await stubWindowPrint(page);
    await loginAdmin(page);

    await installFixtureRoute(
      page,
      'GET',
      ORDER_INVOICES_API_PATH,
      'order-invoices.json',
      'order invoices missing permission',
    );

    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/admin/orders/print?codes=PK7KQ2M9XA');

    const alert = page.getByRole('alert');
    await expect(alert).toBeVisible();
    await expect(alert).toContainText('403 - Không có quyền truy cập');
    expect(await readPrintCallCount(page)).toBe(0);

    await page.screenshot({
      path: resolve(EVIDENCE_DIRECTORY, '06-loi-403.png'),
      fullPage: true,
    });

    expect(consoleIssues).toEqual([]);
  });

  test('chưa đăng nhập chuyển hướng về trang đăng nhập quản trị', async ({ page }) => {
    const consoleIssues = collectConsoleIssues(page);
    await page.goto('/admin/orders/print?codes=PK7KQ2M9XA');
    await expect(page).toHaveURL(/\/admin\/login$/);
    expect(consoleIssues).toEqual([]);
  });

  test('xuất PDF A4: 15 dòng biến thể dài nhất vừa 1 trang, 40 dòng ra 2 trang lặp tiêu đề và 2 đơn ra 2 trang', async ({
    page,
  }) => {
    test.setTimeout(60000);
    const consoleIssues = collectConsoleIssues(page);
    await stubWindowPrint(page);
    await loginAdmin(page);

    // Ca 15 dòng dùng biến thể dài nhất: shop đủ 4 dòng, có khối VAT và dấu ĐƠN ĐÃ HỦY.
    await installFixtureRoute(
      page,
      'GET',
      ORDER_INVOICES_API_PATH,
      'order-invoices.json',
      'order invoices two orders with vat and cancelled',
      {
        transformResponse: (response) => expandInvoiceLines(response, 15, { forceCancelled: true }),
      },
    );
    await page.goto('/admin/orders/print?codes=PK7KQ2M9XA');
    await expect(page.locator('tbody tr')).toHaveCount(15);
    await expect(page.getByTestId('invoice-cancelled-stamp')).toBeVisible();
    await page.addStyleTag({
      content: '.invoice-items-table thead th { font-family: monospace !important; }',
    });
    await page.emulateMedia({ media: 'print' });
    const pdf15 = await page.pdf({ preferCSSPageSize: true });
    expect(countPdfPages(pdf15)).toBe(1);
    const pdf15Margin = readFirstPdfPageClipMargin(pdf15);
    expect(pdf15Margin).not.toBeNull();
    // Lề 12 mm trong hệ tọa độ 0.24 pt của Chromium tương đương ~140.6 đơn vị.
    expect(pdf15Margin!.x).toBeGreaterThan(130);
    expect(pdf15Margin!.y).toBeGreaterThan(130);
    const pdf15Pages = extractPdfPageTexts(pdf15);
    expect(pdf15Pages).toHaveLength(1);
    expect(pdf15Pages.filter((text) => text.includes('STT'))).toHaveLength(1);

    await page.unrouteAll({ behavior: 'ignoreErrors' });
    await installFixtureRoute(
      page,
      'GET',
      ORDER_INVOICES_API_PATH,
      'order-invoices.json',
      'order invoices shop profile not set',
      { transformResponse: (response) => expandInvoiceLines(response, 40) },
    );
    await page.emulateMedia({ media: 'screen' });
    await page.goto('/admin/orders/print?codes=PK3HN8RT2B');
    await expect(page.locator('tbody tr')).toHaveCount(40);
    await page.addStyleTag({
      content: '.invoice-items-table thead th { font-family: monospace !important; }',
    });
    await page.emulateMedia({ media: 'print' });

    const tablePrintRules = await page.evaluate(() => {
      const thead = document.querySelector('.invoice-items-table thead');
      const rows = Array.from(document.querySelectorAll('.invoice-items-table tbody tr'));
      return {
        theadDisplay: thead ? window.getComputedStyle(thead).display : null,
        allRowsAvoidBreak: rows.every(
          (row) => window.getComputedStyle(row).breakInside === 'avoid',
        ),
      };
    });
    expect(tablePrintRules.theadDisplay).toBe('table-header-group');
    expect(tablePrintRules.allRowsAvoidBreak).toBe(true);

    const pdf40 = await page.pdf({ preferCSSPageSize: true });
    expect(countPdfPages(pdf40)).toBe(2);
    const pdf40Pages = extractPdfPageTexts(pdf40);
    expect(pdf40Pages).toHaveLength(2);
    // Cả trang 1 và trang 2 đều phải chứa tiêu đề `STT` của `thead`.
    expect(pdf40Pages.filter((text) => text.includes('STT'))).toHaveLength(2);

    // Ca đối chứng: tắt lặp tiêu đề bảng thì chữ `STT` chỉ còn xuất hiện ở trang đầu tiên.
    await page.addStyleTag({
      content: '.invoice-items-table thead { display: table-row-group !important; }',
    });
    const pdf40NoRepeat = await page.pdf({ preferCSSPageSize: true });
    expect(extractPdfPageTexts(pdf40NoRepeat).filter((text) => text.includes('STT'))).toHaveLength(
      1,
    );

    await page.unrouteAll({ behavior: 'ignoreErrors' });
    await installFixtureRoute(
      page,
      'GET',
      ORDER_INVOICES_API_PATH,
      'order-invoices.json',
      'order invoices two orders with vat and cancelled',
    );
    await page.emulateMedia({ media: 'screen' });
    await page.goto('/admin/orders/print?codes=PK7KQ2M9XA,PK3HN8RT2B');
    await expect(page.locator('article.invoice-sheet')).toHaveCount(2);
    await page.addStyleTag({
      content: '.invoice-items-table thead th { font-family: monospace !important; }',
    });
    await page.emulateMedia({ media: 'print' });
    const pdfTwoOrders = await page.pdf({ preferCSSPageSize: true });
    expect(countPdfPages(pdfTwoOrders)).toBe(2);
    const pdfTwoOrderPages = extractPdfPageTexts(pdfTwoOrders);
    expect(pdfTwoOrderPages).toHaveLength(2);
    expect(pdfTwoOrderPages.filter((text) => text.includes('STT'))).toHaveLength(2);

    expect(consoleIssues).toEqual([]);
  });
});
