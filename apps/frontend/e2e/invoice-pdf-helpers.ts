import type { BrowserContext, Page } from '@playwright/test';
import { inflateSync } from 'node:zlib';

declare global {
  interface Window {
    __printCallCount?: number;
  }
}

// Thay `window.print` bằng bộ đếm trên ngữ cảnh trình duyệt để tab mới không mở hộp thoại chặn luồng.
export async function stubWindowPrintOnContext(context: BrowserContext): Promise<void> {
  await context.addInitScript(() => {
    window.__printCallCount = 0;
    window.print = () => {
      window.__printCallCount = (window.__printCallCount ?? 0) + 1;
    };
  });
}

// Đọc số lần trang tự gọi `window.print` nhằm xác nhận trang chỉ kích hoạt in đúng một lần.
export async function readPrintCallCount(page: Page): Promise<number> {
  return page.evaluate(() => window.__printCallCount ?? 0);
}

// Đếm số trang PDF (Portable Document Format) bằng biểu thức chính quy khớp đối tượng `/Type /Page`.
export function countPdfPages(pdfBuffer: Buffer): number {
  const matches = pdfBuffer.toString('latin1').match(/\/Type\s*\/Page(?![s\w])/g);
  return matches?.length ?? 0;
}

// Giải nén luồng nội dung PDF và giải mã chữ từng trang qua bảng `ToUnicode` của font.
export function extractPdfPageTexts(pdfBuffer: Buffer): string[] {
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
    // Một số luồng dữ liệu không dùng nén zlib nên trả nguyên chuỗi thô thay vì ném lỗi.
    try {
      return inflateSync(raw).toString('latin1');
    } catch {
      return streamMatch[1]!;
    }
  };

  // Ghép bảng ánh xạ mã ký tự sang Unicode từ cả khối đơn lẻ và khối dải liên tiếp.
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

  // Lọc các đối tượng trang thực để đếm đúng số trang thay vì đếm nhầm nút gốc `Pages`.
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
