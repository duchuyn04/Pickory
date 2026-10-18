import { defineConfig, devices } from '@playwright/test';
import { e2eEnvironment } from './e2e/environment';

export default defineConfig({
  testDir: './e2e',
  // Chạy tuần tự để tránh tranh chấp DB và hạn mức IP.
  fullyParallel: false,
  workers: 1,
  // Không retry case lỗi để tránh che flake. Lượt suite thứ hai phải độc lập.
  retries: 0,
  timeout: 30000,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: e2eEnvironment.webOrigin,
    // Giữ bằng chứng lỗi ngoài Git để kiểm tra bước và màn hình bị lỗi.
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      name: 'E2E API',
      // Lệnh `e2e:start` dựng lại DB `pickory_e2e`, tạo admin fixture rồi mới mở cổng API.
      command: 'dotnet run --project ../backend/src/Pickory.Api -- e2e:start',
      url: `${e2eEnvironment.apiOrigin}/api/health`,
      // Không tái sử dụng API đang chạy vì API đó có thể dùng DB dev.
      reuseExistingServer: false,
      timeout: 120000,
      stdout: 'pipe',
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        PORT: '3101',
        WEB_ORIGIN: e2eEnvironment.webOrigin,
        DATABASE_URL: e2eEnvironment.databaseUrl,
        E2E_DATABASE_URL: e2eEnvironment.databaseUrl,
        E2E_ADMIN_EMAIL: e2eEnvironment.adminEmail,
        E2E_ADMIN_PASSWORD: e2eEnvironment.adminPassword,
        // Seed khách E2E cần cùng mật khẩu với các spec đăng nhập, không dùng mật khẩu admin.
        E2E_ACCOUNT_PASSWORD: e2eEnvironment.accountPassword,
        // Tách thư của suite khỏi outbox dev. Spec đọc link mời từ thư mục này.
        DEV_OUTBOX_DIR: e2eEnvironment.outboxDirectory,
        // Xóa token GHN để API dùng `FakeGhnClient`. Spec đặt hàng cần phí vận chuyển cố định và không gọi mạng.
        GHN_TOKEN: '',
        // Xóa khóa AI để luồng identity không gọi provider thật.
        LLM_API_KEY: '',
        LLM_MODEL: '',
        EMBEDDING_API_KEY: '',
        SEPAY_WEBHOOK_API_KEY: e2eEnvironment.sepayWebhookApiKey,
        // Webhook GHN cần bí mật để test gọi được `POST /api/webhooks/ghn`. Thiếu biến này webhook luôn trả 401.
        GHN_WEBHOOK_SECRET: e2eEnvironment.ghnWebhookSecret,
      },
    },
    {
      name: 'E2E Web',
      command: 'pnpm exec vite --host 127.0.0.1 --port 4173 --strictPort',
      url: e2eEnvironment.webOrigin,
      // Dùng strictPort để browser không đi vào web server khác.
      reuseExistingServer: false,
      env: { API_PROXY_TARGET: e2eEnvironment.apiOrigin },
    },
  ],
});
