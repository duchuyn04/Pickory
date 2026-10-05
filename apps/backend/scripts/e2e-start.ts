import { execSync } from 'node:child_process';
import { mkdir, readFile, readdir, realpath, unlink } from 'node:fs/promises';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import pg from 'pg';
import { createApp } from '../src/app';
import { DevOutboxEmailSender, setEmailSender } from '../src/modules/identity/data/email-sender';
import { seedIdentityRoles } from '../src/modules/identity/data/seed';
import { hashPassword } from '../src/modules/identity/security/password';
import { loadConfig } from '../src/shared/config';
import { db } from '../src/shared/db';
import { resetDatabase } from '../test/db';

const backendDirectory = fileURLToPath(new URL('../', import.meta.url));
const outboxDirectory = join(backendDirectory, '.dev-outbox', 'e2e-identity');

async function clearOwnedOutboxMessages() {
  // Tách thư của suite để giữ nguyên outbox dev.
  await mkdir(outboxDirectory, { recursive: true });
  const backendRoot = await realpath(backendDirectory);
  const outboxRoot = await realpath(outboxDirectory);
  // Từ chối symlink ra ngoài cây backend trước khi xóa file.
  if (relative(backendRoot, outboxRoot) !== join('.dev-outbox', 'e2e-identity')) {
    throw new Error('Từ chối dọn outbox E2E nằm ngoài thư mục được phép.');
  }
  const entries = await readdir(outboxRoot, { withFileTypes: true });
  for (const entry of entries) {
    // Bỏ qua thư mục và symlink để giới hạn file được xóa.
    if (!entry.isFile() || !entry.name.endsWith('.json')) continue;
    const filename = join(outboxRoot, entry.name);
    const message: unknown = JSON.parse(await readFile(filename, 'utf8'));
    // Chỉ xóa thư có recipient của suite. Giữ thư dev khác.
    if (
      typeof message === 'object' &&
      message !== null &&
      'to' in message &&
      typeof message.to === 'string' &&
      /^e2e-identity-(admin|customer|sales)@pickory\.local$/.test(message.to)
    ) {
      await unlink(filename);
    }
  }
}

async function startE2eBackend() {
  const config = loadConfig();
  const databaseUrl = new URL(config.DATABASE_URL);
  // Kiểm tra lại connection để chạy script trực tiếp không reset nhầm DB.
  if (
    databaseUrl.pathname !== '/pickory_e2e' ||
    process.env.E2E_DATABASE_URL !== config.DATABASE_URL
  ) {
    throw new Error('E2E chỉ được migrate và reset DB pickory_e2e đã được cấu hình riêng.');
  }
  const adminEmail = process.env.E2E_ADMIN_EMAIL;
  const adminPassword = process.env.E2E_ADMIN_PASSWORD;
  // Chỉ dùng credential fixture từ Playwright, không dùng admin dev.
  if (!adminEmail || !adminPassword || adminPassword.length < 8) {
    throw new Error('Thiếu email hoặc mật khẩu admin fixture E2E hợp lệ.');
  }

  // Dùng DB hệ thống để tạo DB E2E trên volume PostgreSQL cũ.
  databaseUrl.pathname = '/postgres';
  const postgres = new pg.Client({ connectionString: databaseUrl.toString() });
  try {
    await postgres.connect();
    const database = await postgres.query('SELECT 1 FROM pg_database WHERE datname = $1', [
      'pickory_e2e',
    ]);
    if (database.rowCount === 0) {
      // Dùng tên DB cố định để tránh ghép identifier từ env vào SQL.
      await postgres.query('CREATE DATABASE "pickory_e2e"');
    }
  } finally {
    // Đóng connection quản trị cả khi tạo DB thất bại.
    await postgres.end();
  }

  // Migrate deploy giữ nguyên migration đã commit.
  execSync('pnpm db:migrate:deploy', { cwd: backendDirectory, stdio: 'inherit' });
  // Xóa dữ liệu cũ để có thể chạy lại cùng email. Giữ schema và migration.
  await resetDatabase(db);
  // Dùng seed identity để fixture có đúng quyền mặc định.
  await seedIdentityRoles(db);
  const adminRole = await db.role.findUniqueOrThrow({ where: { code: 'admin' } });
  // Chỉ seed admin. Test phải tạo customer và sales qua UI thật.
  await db.user.create({
    data: {
      email: adminEmail,
      name: 'Quản trị E2E',
      kind: 'staff',
      emailVerified: true,
      passwordHash: await hashPassword(adminPassword),
      roles: { create: { roleId: adminRole.id } },
    },
  });

  await clearOwnedOutboxMessages();
  // Dùng adapter dev thật với outbox riêng để giữ thư của người phát triển.
  setEmailSender(new DevOutboxEmailSender(outboxDirectory));
  // Chỉ báo ready sau khi chuẩn bị xong DB và sender. Bind loopback để tránh truy cập ngoài máy.
  createApp().listen(config.PORT, '127.0.0.1', () => {
    process.stdout.write('E2E API ready: pickory_e2e migrated, reset and seeded.\n');
  });
}

startE2eBackend().catch(async (error: unknown) => {
  // Dừng startup khi chuẩn bị lỗi để không chạy test trên dữ liệu cũ.
  process.stderr.write(
    `Khởi động E2E thất bại: ${error instanceof Error ? error.message : 'Lỗi không xác định'}\n`,
  );
  await db.$disconnect();
  process.exitCode = 1;
});
