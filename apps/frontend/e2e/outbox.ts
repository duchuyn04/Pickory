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
