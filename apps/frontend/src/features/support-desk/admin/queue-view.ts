export const PENDING_CALLBACK_STATUS = 'pending';
// Tab Gọi lại chỉ hiện từng này dòng đã xử lý. Danh sách không phân trang, nên cần một mức chặn.
export const HANDLED_CALLBACK_LIMIT = 20;

export interface CallbackGroups<Row> {
  // Dòng còn phải gọi, theo thứ tự API trả.
  pending: Row[];
  // Dòng đã xử lý, tối đa `HANDLED_CALLBACK_LIMIT` dòng đầu theo thứ tự API trả.
  handled: Row[];
  // Đúng khi còn dòng đã xử lý bị cắt, để giao diện nói rõ danh sách chưa đủ.
  hasHiddenHandled: boolean;
}

// Tách danh sách API thành nhóm cần gọi lại và nhóm đã xử lý. Thứ tự trong mỗi nhóm giữ như API,
// vì API đã sắp theo `createdAt` mới nhất trước.
export function groupCallbackRequests<Row extends { status: string }>(
  rows: readonly Row[],
): CallbackGroups<Row> {
  const pending = rows.filter((row) => row.status === PENDING_CALLBACK_STATUS);
  const allHandled = rows.filter((row) => row.status !== PENDING_CALLBACK_STATUS);
  return {
    pending,
    handled: allHandled.slice(0, HANDLED_CALLBACK_LIMIT),
    hasHiddenHandled: allHandled.length > HANDLED_CALLBACK_LIMIT,
  };
}

// Giá trị `href` cho số điện thoại. Chỉ giữ chữ số và một dấu `+` ở đầu số, vì khoảng trắng, dấu chấm và gạch làm link `tel:` không ổn định.
// Trả `null` khi không còn chữ số nào, để nơi gọi hiện chữ thường thay vì link rỗng.
export function callbackPhoneHref(phone: string): string | null {
  const dialable = phone.replace(/[^\d+]/g, '');
  const hasCountryPrefix = dialable.startsWith('+');
  const digits = dialable.replace(/\+/g, '');
  if (digits === '') return null;
  return `tel:${hasCountryPrefix ? '+' : ''}${digits}`;
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
