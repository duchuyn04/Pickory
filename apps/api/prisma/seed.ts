// Khung seed rỗng. Mỗi module thêm hàm seed của mình vào đây (ví dụ identity seed 4 role).
async function main(): Promise<void> {
  // chưa có dữ liệu mẫu
}

main().catch((err) => {
  // Script CLI chạy ngoài app nên ghi thẳng ra stderr.
  process.stderr.write(`Seed thất bại: ${String(err)}\n`);
  process.exit(1);
});
