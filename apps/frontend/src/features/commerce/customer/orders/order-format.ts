export function formatDateTime(isoString: string): string {
  const date = new Date(isoString);
  // Ngày không hợp lệ không ném lỗi nên phải kiểm tra trước khi định dạng.
  if (Number.isNaN(date.getTime())) return isoString;
  // Giữ định dạng tiếng Việt nhất quán trên danh sách và chi tiết đơn.
  return date.toLocaleString('vi-VN', {
    hour: '2-digit',
    minute: '2-digit',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  });
}
