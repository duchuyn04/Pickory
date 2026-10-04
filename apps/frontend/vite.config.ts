import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // Web gọi `/api/...` cùng origin; Vite chuyển tiếp sang API (port 3000) khi chạy dev.
    // Nhờ vậy cookie phiên là first-party và không cần cấu hình CORS.
    proxy: { '/api': 'http://localhost:3000' },
  },
});
