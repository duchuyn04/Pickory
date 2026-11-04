import { Fragment, useEffect, useRef } from 'react';
import { ResultPanel } from '../../../shared/result-panel';
import {
  formatVietnamDateTime,
  formatVietnamDayLabel,
  formatVietnamTime,
} from '../../../shared/vietnam-time';
import type { SupportMessage } from './support-customer-schemas';
import { groupSupportMessages } from './support-message-groups';
import { SupportMessageAction } from './support-message-action';

export const FALLBACK_STAFF_SENDER_NAME = 'Nhân viên CSKH';
const CUSTOMER_SENDER_NAME = 'Bạn';
// Khách ở cách đáy bằng ngần này thì tin mới tự cuộn xuống, để không giật màn khi đang đọc tin cũ.
const NEAR_BOTTOM_PX = 120;

// Tên người gửi là chữ thật trong DOM. Trình đọc màn hình đọc được vai trò, không chỉ dựa vào màu.
function senderName(message: SupportMessage): string {
  if (message.role === 'customer') return CUSTOMER_SENDER_NAME;
  if (message.role === 'staff') return message.staffDisplayName ?? FALLBACK_STAFF_SENDER_NAME;
  return '';
}

interface SupportMessageListProps {
  messages: SupportMessage[];
}

// Danh sách tin là vùng cuộn riêng, cuộn được bằng bàn phím và tự cuộn xuống khi khách ở gần đáy.
export function SupportMessageList({ messages }: SupportMessageListProps) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const stickToBottomRef = useRef(true);
  const lastMessageIdRef = useRef<string | null>(null);

  function handleScroll() {
    const region = scrollRef.current;
    if (!region) return;
    stickToBottomRef.current =
      region.scrollHeight - region.scrollTop - region.clientHeight < NEAR_BOTTOM_PX;
  }

  useEffect(() => {
    const region = scrollRef.current;
    const lastMessage = messages.at(-1);
    // Tin do chính khách gửi luôn cuộn xuống, kể cả khi khách vừa cuộn lên đọc tin cũ.
    const ownMessageJustArrived =
      lastMessage !== undefined &&
      lastMessage.role === 'customer' &&
      lastMessage.id !== lastMessageIdRef.current;
    if (region && (stickToBottomRef.current || ownMessageJustArrived)) {
      region.scrollTop = region.scrollHeight;
    }
    lastMessageIdRef.current = lastMessage?.id ?? null;
  }, [messages]);

  useEffect(() => {
    const region = scrollRef.current;
    // Môi trường test không có `ResizeObserver`. Thiếu nó thì chỉ mất việc cuộn lại, không lỗi.
    if (!region || typeof ResizeObserver === 'undefined') return;
    // Đổi cỡ màn hình (ví dụ xoay điện thoại) làm vùng cuộn ngắn lại.
    // Khách đang ở gần đáy thì giữ tin mới nhất trong tầm nhìn.
    const observer = new ResizeObserver(() => {
      if (stickToBottomRef.current) region.scrollTop = region.scrollHeight;
    });
    observer.observe(region);
    return () => observer.disconnect();
  }, []);

  return (
    <div
      ref={scrollRef}
      className="support-message-scroll"
      role="log"
      aria-live="polite"
      aria-label="Tin nhắn"
      tabIndex={0}
      onScroll={handleScroll}
    >
      {messages.length === 0 ? (
        <div className="support-message-empty">
          <ResultPanel
            tone="info"
            icon="messageCircle"
            title="Bạn cần hỗ trợ gì?"
            description="Chào bạn, hãy mô tả vấn đề cần hỗ trợ. Kèm mã đơn hàng nếu có."
          />
        </div>
      ) : (
        <ul className="support-message-list">
          {groupSupportMessages(messages).map(({ message, startsGroup }, index) => {
            const previous = index > 0 ? messages[index - 1] : undefined;
            const dayLabel = formatVietnamDayLabel(message.createdAt);
            // Ngày chỉ hiện khi khác ngày của tin trước, để đọc lại thấy mốc ngày mà không lặp.
            const showDayLabel =
              previous === undefined || dayLabel !== formatVietnamDayLabel(previous.createdAt);
            const time = formatVietnamTime(message.createdAt);
            return (
              <Fragment key={message.id}>
                {showDayLabel && (
                  <li className="support-message-day">
                    <span>{dayLabel}</span>
                  </li>
                )}
                {message.role === 'system' ? (
                  <li className="support-message is-system">
                    <p className="support-message-system">{message.body}</p>
                    <time className="sr" dateTime={message.createdAt}>
                      {time}
                    </time>
                    {message.action !== null && <SupportMessageAction action={message.action} />}
                  </li>
                ) : (
                  <li
                    className={`support-message is-${message.role}${
                      startsGroup ? ' starts-group' : ''
                    }`}
                  >
                    {startsGroup ? (
                      <p className="support-message-meta">
                        <span className="support-message-sender">{senderName(message)}</span>
                        <span aria-hidden="true"> · </span>
                        <time dateTime={message.createdAt}>{time}</time>
                      </p>
                    ) : (
                      // Tin trong nhóm vẫn giữ giờ cho trình đọc màn hình, nhưng không lặp dòng phụ.
                      <time className="sr" dateTime={message.createdAt}>
                        {time}
                      </time>
                    )}
                    <div
                      className="support-message-bubble"
                      title={formatVietnamDateTime(message.createdAt)}
                    >
                      {message.body}
                    </div>
                  </li>
                )}
              </Fragment>
            );
          })}
        </ul>
      )}
    </div>
  );
}
