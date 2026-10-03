import { createApp } from './app';
import { loadConfig, type Config } from './shared/config';
import { logger } from './shared/logger';

// Điểm vào của API: đọc cấu hình, nếu sai thì dừng ngay và báo rõ biến nào.
let config: Config;
try {
  config = loadConfig();
} catch (err) {
  logger.fatal((err as Error).message);
  process.exit(1);
}

createApp().listen(config.PORT, () => {
  logger.info(`api listening on :${config.PORT}`);
});
