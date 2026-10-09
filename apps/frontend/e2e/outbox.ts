import { expect } from '@playwright/test';
import { readFile, readdir } from 'node:fs/promises';
import { join } from 'node:path';
import { e2eEnvironment } from './environment';

export async function readPasswordSetupLink(recipient: string) {
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
          // Chỉ dùng thư mời của recipient đang được kiểm thử.
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
            message.metadata.purpose !== 'invite'
          ) {
            continue;
          }
          const link = message.text.match(/https?:\/\/[^\s]+/)?.[0];
          if (!link) throw new Error('Email mời không có link đặt mật khẩu.');
          const url = new URL(link);
          // Chỉ mở link của web E2E để tránh điều hướng ra ngoài.
          if (
            url.origin !== e2eEnvironment.webOrigin ||
            url.pathname !== '/reset-password' ||
            !url.searchParams.get('token')
          ) {
            throw new Error(
              'Link đặt mật khẩu trong outbox sai origin, đường dẫn hoặc thiếu token.',
            );
          }
          setupUrl = url.toString();
          return true;
        }
        return false;
      },
      { message: `Chờ email mời của ${recipient} trong outbox dev`, timeout: 10000 },
    )
    .toBe(true);
  // Không dựng link giả khi outbox thiếu thư.
  if (!setupUrl) throw new Error('Không đọc được link đặt mật khẩu từ outbox dev.');
  return setupUrl;
}
