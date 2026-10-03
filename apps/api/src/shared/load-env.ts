// Nạp file .env ở gốc repo vào process.env khi chạy dev.
// Biến đã có sẵn trong môi trường (CI, production, test) không bị ghi đè.
try {
  process.loadEnvFile(new URL('../../../../.env', import.meta.url));
} catch {
  // không có .env: dùng biến môi trường có sẵn
}
