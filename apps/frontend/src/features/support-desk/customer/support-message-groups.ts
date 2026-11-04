import { isSameVietnamDay } from '../../../shared/vietnam-time';
import type { SupportMessage } from './support-customer-schemas';

// Một tin kèm cờ mở nhóm. Tin mở nhóm hiện tên người gửi và giờ gửi; tin sau trong nhóm thì không.
export interface SupportMessageGroupItem {
  message: SupportMessage;
  startsGroup: boolean;
}

// Tin liên tiếp cùng người gửi cách tin đầu nhóm quá 5 phút hoặc sang ngày mới thì mở nhóm mới.
const GROUP_WINDOW_MS = 5 * 60 * 1000;

// Hai tin của cùng một nhân viên vẫn tách nhóm khi tên hiện khác nhau, ví dụ một tin có tên một tin không.
function senderKey(message: SupportMessage): string {
  if (message.role === 'staff') return `staff:${message.staffDisplayName ?? ''}`;
  return message.role;
}

// Đánh dấu tin mở nhóm theo người gửi và khoảng cách thời gian, để khung chat bớt lặp dòng phụ.
export function groupSupportMessages(messages: SupportMessage[]): SupportMessageGroupItem[] {
  let groupHeadMessage: SupportMessage | undefined;

  return messages.map((message, index) => {
    const previous = messages[index - 1];
    // Tin đầu tiên của hội thoại luôn mở nhóm.
    if (previous === undefined || groupHeadMessage === undefined) {
      groupHeadMessage = message;
      return { message, startsGroup: true };
    }
    const sameSender = senderKey(previous) === senderKey(message);
    const sameDay = isSameVietnamDay(previous.createdAt, message.createdAt);
    const gapFromHeadMs = Date.parse(message.createdAt) - Date.parse(groupHeadMessage.createdAt);
    // Chuỗi giờ hỏng đã bị schema loại, nên `gapFromHeadMs` là số hợp lệ ở đây.
    const withinWindow = gapFromHeadMs >= 0 && gapFromHeadMs <= GROUP_WINDOW_MS;
    // Mở nhóm mới khi đổi người gửi, sang ngày mới theo giờ Việt Nam hoặc cách tin đầu nhóm quá 5 phút.
    const startsGroup = !sameSender || !sameDay || !withinWindow;
    if (startsGroup) {
      groupHeadMessage = message;
    }
    return { message, startsGroup };
  });
}
