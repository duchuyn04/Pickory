import { db } from '../src/shared/db';
import { hashPassword } from '../src/modules/identity/security/password';
import { seedIdentityRoles } from '../src/modules/identity/data/seed';

function parseArg(name: string): string | undefined {
  const prefix = `--${name}=`;
  for (let i = 0; i < process.argv.length; i++) {
    const arg = process.argv[i];
    if (arg === undefined) continue;
    if (arg === `--${name}` && process.argv[i + 1]) {
      return process.argv[i + 1];
    }
    if (arg.startsWith(prefix)) {
      return arg.slice(prefix.length);
    }
  }
  return undefined;
}

async function main(): Promise<void> {
  const isProduction = process.env.NODE_ENV === 'production';
  const email =
    parseArg('email') ??
    process.env.ADMIN_EMAIL ??
    (isProduction ? undefined : 'admin@pickory.local');
  const password =
    parseArg('password') ??
    process.env.ADMIN_PASSWORD ??
    (isProduction ? undefined : 'Admin@123456');
  const name = parseArg('name') ?? process.env.ADMIN_NAME ?? 'Quản trị viên';

  if (!email) {
    throw new Error('Vui lòng cung cấp email admin qua --email hoặc ADMIN_EMAIL');
  }

  if (!password || password.length < 8) {
    throw new Error(
      'Mật khẩu admin là bắt buộc và phải có tối thiểu 8 ký tự (qua biến môi trường ADMIN_PASSWORD hoặc --password).',
    );
  }

  const normalizedEmail = email.trim().toLowerCase();

  // Đảm bảo các role hệ thống đã tồn tại
  await seedIdentityRoles(db);

  const existing = await db.user.findUnique({
    where: { email: normalizedEmail },
  });

  if (existing) {
    if (existing.kind === 'customer') {
      throw new Error(
        `Email "${normalizedEmail}" đã thuộc tài khoản khách hàng, không thể tạo admin.`,
      );
    }
    process.stdout.write(`Admin với email "${normalizedEmail}" đã tồn tại.\n`);
    return;
  }

  const adminRole = await db.role.findUnique({
    where: { code: 'admin' },
  });

  if (!adminRole) {
    throw new Error('Role admin không tồn tại');
  }

  const passwordHash = await hashPassword(password);

  await db.user.create({
    data: {
      email: normalizedEmail,
      emailVerified: true,
      passwordHash,
      name,
      kind: 'staff',
      roles: {
        create: {
          roleId: adminRole.id,
        },
      },
    },
  });

  process.stdout.write(`Tạo admin thành công: ${normalizedEmail}\n`);
}

main()
  .catch((err) => {
    process.stderr.write(`Tạo admin thất bại: ${String(err)}\n`);
    process.exit(1);
  })
  .finally(async () => {
    await db.$disconnect();
  });
