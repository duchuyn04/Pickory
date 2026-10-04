import { createApp } from './app';
import { validateStoredPermissions } from './modules/identity/permissions';
import { loadConfig, type Config } from './shared/config';
import { db } from './shared/db';
import { logger } from './shared/logger';

// Điểm vào của API: đọc cấu hình, nếu sai thì dừng ngay và báo rõ biến nào.
let config: Config;
try {
  config = loadConfig();
} catch (err) {
  logger.fatal((err as Error).message);
  process.exit(1);
}

async function start() {
  try {
    await validateStoredPermissions(db);
  } catch (err) {
    logger.warn({ err }, 'Không thể kiểm tra role_permissions khi khởi động');
  }
  createApp().listen(config.PORT, () => {
    logger.info(`api listening on :${config.PORT}`);
  });
}

void start();
