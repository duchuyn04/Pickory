import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // Web gọi `/api` cùng origin. Cookie phiên là first-party nên không cần CORS.
    // E2E dùng `API_PROXY_TARGET` để trỏ proxy sang API tại port 3101. Proxy không gọi API dev.
    proxy: { '/api': process.env.API_PROXY_TARGET ?? 'http://localhost:3000' },
  },
});
