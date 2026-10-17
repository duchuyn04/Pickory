import { expect } from '@playwright/test';
import { readFile, readdir } from 'node:fs/promises';
import { join } from 'node:path';
import { e2eEnvironment } from './environment';

// Đọc link trong thư mới nhất gửi tới `recipient` với `purpose` và đường dẫn trang đã cho.
async function readOutboxLink(recipient: string, purpose: string, pathname: string) {
  let setupUrl: string | undefined;
  // Chờ đúng thư xuất hiện trên đĩa. Không dùng sleep cố định.
  await expect
    .poll(
      async () => {
        const rawEntries = await readdir(e2eEnvironment.outboxDirectory, { withFileTypes: true });
        // Sắp xếp theo tên giảm dần để luôn đọc thư mới nhất theo tiền tố thời gian.
        const entries = rawEntries.sort((a, b) => b.name.localeCompare(a.name));
        for (const entry of entries) {
          // Bỏ qua symlink để không đọc file ngoài outbox.
          if (!entry.isFile() || !entry.name.endsWith('.json')) continue;
          const message: unknown = JSON.parse(
            await readFile(join(e2eEnvironment.outboxDirectory, entry.name), 'utf8'),
          );
          // Chỉ dùng thư đúng mục đích của recipient đang được kiểm thử.
          if (
            typeof message !== 'object' ||
            message === null ||
            !('to' in message) ||
            message.to !== recipient ||
            !('text' in message) ||
            typeof message.text !== 'string' ||
            !('metadata' in message) ||
            typeof message.metadata !== 'object' ||
            message.metadata === null ||
            !('purpose' in message.metadata) ||
            message.metadata.purpose !== purpose
          ) {
            continue;
          }
          // Thư xác thực email ghi đường dẫn tương đối. Thư mời và đặt lại ghi link đầy đủ.
          const link =
            message.text.match(/https?:\/\/[^\s]+/)?.[0] ??
            message.text.match(new RegExp(pathname + '\\?\\S+'))?.[0];
          if (!link) throw new Error(`Email ${purpose} không có link.`);
          const url = new URL(link, e2eEnvironment.webOrigin);
          // Chỉ mở link của web E2E để tránh điều hướng ra ngoài.
          if (
            url.origin !== e2eEnvironment.webOrigin ||
            url.pathname !== pathname ||
            !url.searchParams.get('token')
          ) {
            throw new Error(`Link ${purpose} trong outbox sai origin, đường dẫn hoặc thiếu token.`);
          }
          setupUrl = url.toString();
          return true;
        }
        return false;
      },
      { message: `Chờ email ${purpose} của ${recipient} trong outbox dev`, timeout: 10000 },
    )
    .toBe(true);
  // Không dựng link giả khi outbox thiếu thư.
  if (!setupUrl) throw new Error(`Không đọc được link ${purpose} từ outbox dev.`);
  return setupUrl;
}

export function readPasswordSetupLink(recipient: string) {
  return readOutboxLink(recipient, 'invite', '/reset-password');
}

export function readPasswordResetLink(recipient: string) {
  return readOutboxLink(recipient, 'reset', '/reset-password');
}

export function readEmailVerificationLink(recipient: string) {
  return readOutboxLink(recipient, 'verify', '/verify-email');
}

// Thư xác nhận đơn trong outbox dev. `link` là liên kết xem đơn đầy đủ trong thân thư.
export interface OrderConfirmationEmail {
  link: string;
  orderCode: string;
  text: string;
}

// Đọc thư xác nhận của một đơn gửi tới `recipient`. Lọc theo `purpose` và `orderCode` nên không lẫn thư đơn khác.
export async function readOrderConfirmation(
  recipient: string,
  orderCode: string,
): Promise<OrderConfirmationEmail> {
  let confirmation: OrderConfirmationEmail | undefined;
  // Chờ đúng thư xuất hiện trên đĩa. Không dùng sleep cố định.
  await expect
    .poll(
      async () => {
        const rawEntries = await readdir(e2eEnvironment.outboxDirectory, { withFileTypes: true });
        for (const entry of rawEntries) {
          // Bỏ qua symlink và file không phải JSON để không đọc ngoài outbox.
          if (!entry.isFile() || !entry.name.endsWith('.json')) continue;
          const message: unknown = JSON.parse(
            await readFile(join(e2eEnvironment.outboxDirectory, entry.name), 'utf8'),
          );
          if (
            typeof message !== 'object' ||
            message === null ||
            !('to' in message) ||
            message.to !== recipient ||
            !('text' in message) ||
            typeof message.text !== 'string' ||
            !('metadata' in message) ||
            typeof message.metadata !== 'object' ||
            message.metadata === null ||
            !('purpose' in message.metadata) ||
            message.metadata.purpose !== 'order-confirmation' ||
            !('orderCode' in message.metadata) ||
            message.metadata.orderCode !== orderCode
          ) {
            continue;
          }
          const link = message.text.match(/https?:\/\/[^\s]+\/orders\/[^\s]+/)?.[0];
          if (!link) throw new Error('Thư xác nhận đơn không có liên kết xem đơn.');
          confirmation = { link, orderCode, text: message.text };
          return true;
        }
        return false;
      },
      {
        message: `Chờ thư xác nhận đơn ${orderCode} của ${recipient} trong outbox dev`,
        timeout: 10000,
      },
    )
    .toBe(true);
  // Không dựng liên kết giả khi outbox thiếu thư.
  if (!confirmation) throw new Error('Không đọc được thư xác nhận đơn từ outbox dev.');
  return confirmation;
}

// Đọc thư tra cứu của một đơn gửi tới `recipient`. Lọc theo `purpose` và `orderCode` nên không lẫn thư xác nhận hay đơn khác.
export async function readOrderLookupLink(recipient: string, orderCode: string): Promise<string> {
  let lookupLink: string | undefined;
  // Chờ đúng thư xuất hiện trên đĩa. Không dùng sleep cố định.
  await expect
    .poll(
      async () => {
        const rawEntries = await readdir(e2eEnvironment.outboxDirectory, { withFileTypes: true });
        for (const entry of rawEntries) {
          // Bỏ qua symlink và file không phải JSON để không đọc ngoài outbox.
          if (!entry.isFile() || !entry.name.endsWith('.json')) continue;
          const message: unknown = JSON.parse(
            await readFile(join(e2eEnvironment.outboxDirectory, entry.name), 'utf8'),
          );
          if (
            typeof message !== 'object' ||
            message === null ||
            !('to' in message) ||
            message.to !== recipient ||
            !('text' in message) ||
            typeof message.text !== 'string' ||
            !('metadata' in message) ||
            typeof message.metadata !== 'object' ||
            message.metadata === null ||
            !('purpose' in message.metadata) ||
            message.metadata.purpose !== 'order-lookup' ||
            !('orderCode' in message.metadata) ||
            message.metadata.orderCode !== orderCode
          ) {
            continue;
          }
          const link = message.text.match(/https?:\/\/[^\s]+/)?.[0];
          if (!link) throw new Error('Thư tra cứu đơn không có liên kết xem đơn.');
          const url = new URL(link);
          // Chỉ mở link của web E2E, đúng trang đơn cần xem, để tránh điều hướng ra ngoài.
          if (url.origin !== e2eEnvironment.webOrigin || url.pathname !== `/orders/${orderCode}`) {
            throw new Error('Link trong thư tra cứu đơn sai origin hoặc sai đường dẫn.');
          }
          lookupLink = url.toString();
          return true;
        }
        return false;
      },
      {
        message: `Chờ thư tra cứu đơn ${orderCode} của ${recipient} trong outbox dev`,
        timeout: 10000,
      },
    )
    .toBe(true);
  // Không dựng liên kết giả khi outbox thiếu thư.
  if (!lookupLink) throw new Error('Không đọc được thư tra cứu đơn từ outbox dev.');
  return lookupLink;
}
