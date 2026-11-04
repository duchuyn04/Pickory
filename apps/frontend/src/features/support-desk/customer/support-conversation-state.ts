import type { SupportMessage } from './support-customer-schemas';

// Gộp tin theo `id`. Tin từ lịch sử, response gửi tin và `message.created` cùng `id` chỉ hiện một lần.
export function mergeMessagesById(
  current: SupportMessage[],
  incoming: SupportMessage[],
): SupportMessage[] {
  const byId = new Map(current.map((message) => [message.id, message]));
  for (const message of incoming) {
    // Tin đến sau ghi đè tin cũ cùng `id`, để nội dung luôn khớp dữ liệu mới nhất của máy chủ.
    byId.set(message.id, message);
  }
  // Thứ tự hiển thị theo thời điểm tạo. So bằng `Date.parse` vì so chuỗi sai khi phần lẻ giây dài khác nhau.
  return [...byId.values()].sort((a, b) => {
    const byTime = Date.parse(a.createdAt) - Date.parse(b.createdAt);
    // Cùng thời điểm thì so `id`, để thứ tự ổn định không phụ thuộc thứ tự nạp.
    return byTime !== 0 ? byTime : a.id.localeCompare(b.id);
  });
}
