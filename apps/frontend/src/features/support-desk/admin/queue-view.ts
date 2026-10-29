const PENDING_CALLBACK_STATUS = 'pending';

// Nhân viên chỉ cần thấy yêu cầu chưa gọi. Dòng đã hoàn tất hoặc đã hủy thuộc về báo cáo, không thuộc hàng chờ.
export function selectPendingCallbacks<Row extends { status: string }>(rows: Row[]): Row[] {
  return rows.filter((row) => row.status === PENDING_CALLBACK_STATUS);
}

// Badge bằng số dòng của danh sách đang chờ. Chưa có dữ liệu hoặc danh sách rỗng thì không hiện badge.
export function waitingBadgeCount(rows: readonly unknown[] | undefined): number | null {
  if (rows === undefined || rows.length === 0) return null;
  return rows.length;
}

const SECONDS_PER_MINUTE = 60;
const SECONDS_PER_HOUR = 3600;

// Thời gian một khách đã chờ, viết ngắn để vừa cột hẹp. Nơi gọi thêm tiền tố "Chờ" phía trước.
export function formatWaitingTime(waitingSeconds: number): string {
  // Đồng hồ máy khách có thể chạy lệch so với máy chủ, nên số âm coi như 0 giây.
  const seconds = Math.max(0, Math.floor(waitingSeconds));
  if (seconds < SECONDS_PER_MINUTE) return 'dưới 1 phút';

  const hours = Math.floor(seconds / SECONDS_PER_HOUR);
  const minutes = Math.floor((seconds % SECONDS_PER_HOUR) / SECONDS_PER_MINUTE);
  if (hours === 0) return `${minutes} phút`;
  if (minutes === 0) return `${hours} giờ`;
  return `${hours} giờ ${minutes} phút`;
}
