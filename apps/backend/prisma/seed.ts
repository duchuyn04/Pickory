import { db } from '../src/shared/db';
import { seedIdentityRoles } from '../src/modules/identity/data/seed';

// Seed khởi tạo các bảng của hệ thống.
async function main(): Promise<void> {
  await seedIdentityRoles(db);
}

main()
  .catch((err) => {
    // Script CLI chạy ngoài app nên ghi thẳng ra stderr.
    process.stderr.write(`Seed thất bại: ${String(err)}\n`);
    process.exit(1);
  })
  .finally(async () => {
    await db.$disconnect();
  });
