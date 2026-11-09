import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Fragment, useEffect, useRef, useState } from 'react';
import { ApiError } from '../../../shared/api';
import { ErrorAlert } from '../../../shared/error-alert';
import { getErrorMessage } from '../../../shared/error-message';
import { formatVietnamDateTime, formatVietnamTime } from '../../../shared/vietnam-time';
import { QueueResult } from './queue-result';
import { StaffMessageComposer } from './staff-message-composer';
import { EndSessionDialog } from './end-session-dialog';
import { TransferSessionDialog } from './transfer-session-dialog';
import type { CannedResponseVariables } from './render-canned-response';
import type { OpenSupportSession } from './support-desk-session';
import {
  SESSIONS_QUERY_KEY,
  claimSupportSession,
  fetchStaffMessages,
  sendStaffMessage,
  staffMessagesQueryKey,
} from './support-desk-api';
import type { StaffViewMessage } from './support-desk-schemas';
import './support-chat.css';

// Lỗi 409 của route nhận phiên hiện câu cố định, theo tiêu chí nghiệm thu của SD-UI2.
const ALREADY_CLAIMED_MESSAGE = 'Đã có người nhận';
const CLAIM_ERROR_FALLBACK = 'Chưa nhận được hội thoại. Hãy thử lại.';
const SEND_ERROR_FALLBACK = 'Chưa gửi được tin. Hãy thử lại.';
// Nhân viên ở cách đáy bằng ngần này thì tin mới tự cuộn xuống, để không giật màn khi đang đọc tin cũ.
const NEAR_BOTTOM_PX = 120;

// Nhãn người gửi là chữ thật trong DOM. Trình đọc màn hình đọc được vai trò, không chỉ dựa vào màu.
function senderLabel(message: StaffViewMessage): string {
  if (message.role === 'customer') return 'Khách';
  if (message.role === 'staff') return message.staffDisplayName ?? 'Nhân viên CSKH';
  return 'Hệ thống';
}

interface SupportChatProps {
  session: OpenSupportSession | null;
  canReply: boolean;
  // Giá trị thay cho biến của mẫu. SD-UI3 truyền giá trị thật từ dữ liệu khách của phiên đang mở.
  variables: CannedResponseVariables;
  // Nhận thành công: trang đặt `isHeld` bằng `true` ngay, rồi danh sách phiên đọc lại.
  onClaimed(sessionId: string): void;
  // Mất quyền gửi tin (403) thì đặt lại isHeld để ô soạn chuyển sang trạng thái nhắc nhận phiên.
  onLostHold(sessionId: string): void;
  // Thông báo tên người nhận cho trang chính khi hoàn tất chuyển phiên.
  onTransferSuccess(recipientName: string): void;
  // Cập nhật trạng thái khi phiên kết thúc để vô hiệu hóa ô soạn tin và dọn nút thao tác.
  onSessionClosed(sessionId: string): void;
}

// Cột giữa của màn CSKH: nút Nhận, nút Chuyển, nút Kết thúc, lịch sử tin và ô soạn.
export function SupportChat({
  session,
  canReply,
  variables,
  onClaimed,
  onLostHold,
  onTransferSuccess,
  onSessionClosed,
}: SupportChatProps) {
  const queryClient = useQueryClient();
  const [isClaiming, setIsClaiming] = useState(false);
  // Ref chặn bấm đôi đồng bộ ngay trong microtask, trước khi React kịp vẽ lại với `isClaiming = true`.
  const isClaimingRef = useRef(false);
  const [claimError, setClaimError] = useState<string | null>(null);
  const [isSending, setIsSending] = useState(false);
  const [sendError, setSendError] = useState<string | null>(null);
  const scrollRef = useRef<HTMLDivElement>(null);
  const stickToBottomRef = useRef(true);
  const lastMessageIdRef = useRef<string | null>(null);
  const [isTransferOpen, setIsTransferOpen] = useState(false);
  const [isEndOpen, setIsEndOpen] = useState(false);
  // Giữ ref nút mở để trả tiêu điểm dự phòng cho trình duyệt không tự focus khi đóng.
  const transferButtonRef = useRef<HTMLButtonElement>(null);
  const endButtonRef = useRef<HTMLButtonElement>(null);
  const chatTitleRef = useRef<HTMLHeadingElement>(null);

  // Trả tiêu điểm về nút mở để hỗ trợ trình duyệt không tự khôi phục vị trí bấm.
  function handleCloseTransfer() {
    setIsTransferOpen(false);
    transferButtonRef.current?.focus();
  }

  // Đóng hộp thoại và gửi tên người nhận lên trang chính để hiển thị dòng thông báo.
  function handleTransferSuccess(recipientName: string) {
    setIsTransferOpen(false);
    onTransferSuccess(recipientName);
  }

  // Trả tiêu điểm về nút mở để người dùng tiếp tục thao tác trên bàn phím.
  function handleCloseEnd() {
    setIsEndOpen(false);
    endButtonRef.current?.focus();
  }

  // Đánh dấu phiên kết thúc và nạp lại lịch sử tin để hiển thị biểu mẫu csat_form.
  function handleEndSuccess() {
    setIsEndOpen(false);
    if (session) {
      onSessionClosed(session.sessionId);
      void queryClient.invalidateQueries({ queryKey: SESSIONS_QUERY_KEY });
      if (session.conversationId) {
        void queryClient.invalidateQueries({
          queryKey: staffMessagesQueryKey(session.conversationId),
        });
      }
    }
    // Chờ hộp thoại gỡ xong và useDialogFocus trả focus xong rồi mới đưa focus vào tiêu đề.
    setTimeout(() => chatTitleRef.current?.focus(), 0);
  }

  // Đồng bộ cờ đã kết thúc khi máy chủ trả lỗi 409 do phiên đã đóng ở nơi khác.
  function handleSessionAlreadyClosed() {
    setIsEndOpen(false);
    setIsTransferOpen(false);
    if (session) {
      onSessionClosed(session.sessionId);
      void queryClient.invalidateQueries({ queryKey: SESSIONS_QUERY_KEY });
    }
  }

  // Người gọi mất quyền giữ phiên (lỗi 403) thì đặt lại cờ để ô soạn hiển thị nút Nhận.
  function handleTransferLostHold() {
    setIsTransferOpen(false);
    if (session) {
      onLostHold(session.sessionId);
    }
  }

  const conversationId = session?.conversationId ?? null;
  const messagesQuery = useQuery({
    queryKey: staffMessagesQueryKey(conversationId ?? ''),
    queryFn: () => fetchStaffMessages(conversationId as string),
    // Chưa chọn phiên thì không đọc lịch sử, để trang mới mở không gọi API khung chat.
    enabled: conversationId !== null,
    retry: false,
    select: (page) => page.items,
  });
  const messages = messagesQuery.data ?? [];

  // Chỉ kéo xuống khi nhân viên đang ở gần đáy, hoặc tin vừa đến là tin của chính nhân viên.
  useEffect(() => {
    const region = scrollRef.current;
    const lastMessage = messages.at(-1);
    const ownMessageJustArrived =
      lastMessage !== undefined &&
      lastMessage.role === 'staff' &&
      lastMessage.id !== lastMessageIdRef.current;
    if (region && (stickToBottomRef.current || ownMessageJustArrived)) {
      region.scrollTop = region.scrollHeight;
    }
    lastMessageIdRef.current = lastMessage?.id ?? null;
  }, [messages]);

  // Vùng tin đổi chiều cao khi mở danh sách mẫu hoặc đổi cỡ màn hình.
  // Nếu nhân viên đang ở gần đáy, cuộn xuống để tin cuối không bị che.
  // Phụ thuộc `messagesQuery.data` vì lúc đầu đang nạp thì `scrollRef.current` còn `null`.
  useEffect(() => {
    const region = scrollRef.current;
    // Môi trường test không có `ResizeObserver`. Thiếu nó thì chỉ mất việc cuộn lại, không lỗi.
    if (!region || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(() => {
      if (stickToBottomRef.current) region.scrollTop = region.scrollHeight;
    });
    observer.observe(region);
    return () => observer.disconnect();
  }, [messagesQuery.data]);

  function handleScroll() {
    const region = scrollRef.current;
    if (!region) return;
    // Nhân viên cuộn lên đọc tin cũ thì không kéo xuống nữa. Ngưỡng gần đáy nhận biết ý định đọc tiếp.
    stickToBottomRef.current =
      region.scrollHeight - region.scrollTop - region.clientHeight < NEAR_BOTTOM_PX;
  }

  async function handleClaim() {
    if (session === null || isClaimingRef.current) return;
    // Khóa nút ngay để hai lần bấm không tạo hai request nhận cùng một phiên.
    isClaimingRef.current = true;
    setIsClaiming(true);
    setClaimError(null);
    try {
      await claimSupportSession(session.sessionId);
      // Đặt `isHeld` ngay, không chờ danh sách đọc lại, để ô soạn bật ngay sau khi nhận.
      onClaimed(session.sessionId);
      void queryClient.invalidateQueries({ queryKey: SESSIONS_QUERY_KEY });
    } catch (error) {
      if (error instanceof ApiError && error.code === 'SUPPORT_SESSION_ALREADY_CLAIMED') {
        setClaimError(ALREADY_CLAIMED_MESSAGE);
        // Người khác đã nhận. Đọc lại danh sách để dòng phiên rời hàng chờ dù nhân viên không đổi được phiên.
        void queryClient.invalidateQueries({ queryKey: SESSIONS_QUERY_KEY });
      } else {
        setClaimError(getErrorMessage(error, CLAIM_ERROR_FALLBACK));
      }
    } finally {
      isClaimingRef.current = false;
      setIsClaiming(false);
    }
  }

  async function handleSend(body: string): Promise<boolean> {
    if (session === null) return false;
    // Khóa nút gửi trong lúc chờ API, để hai lần gửi không tạo hai tin trùng nhau.
    setIsSending(true);
    setSendError(null);
    try {
      await sendStaffMessage(session.sessionId, body);
      // Mở lại nút gửi sau khi máy chủ nhận tin thành công, để nhân viên gõ tin tiếp theo.
      setIsSending(false);
      // Backend chỉ phát `message.created` cho người giữ phiên, nên tin của chính nhân viên không đến qua SSE.
      // Giao diện tự đọc lại lịch sử để tin vừa gửi hiện ra.
      void queryClient.invalidateQueries({
        queryKey: staffMessagesQueryKey(session.conversationId),
      });
      return true;
    } catch (error) {
      // Mở lại nút gửi khi gửi lỗi để nhân viên có thể bấm gửi lại sau khi sửa.
      setIsSending(false);
      // Gửi lỗi thì giữ nguyên chữ trong ô soạn, chỉ hiện câu lỗi của API.
      setSendError(getErrorMessage(error, SEND_ERROR_FALLBACK));
      if (error instanceof ApiError && error.code === 'SUPPORT_SESSION_NOT_ASSIGNED') {
        // Nhân viên không còn giữ phiên, ví dụ phiên bị chuyển. Ô soạn tắt theo trạng thái mới.
        onLostHold(session.sessionId);
      }
      return false;
    }
  }

  if (session === null) {
    return (
      <section
        className="card card-flush support-pane support-chat"
        aria-labelledby="support-chat-title"
      >
        <h2 id="support-chat-title" className="support-pane-title">
          Khung chat
        </h2>
        <p className="support-pane-empty">Tin nhắn của hội thoại đang mở sẽ hiện ở đây.</p>
      </section>
    );
  }

  // Chú thích nói rõ lý do ô soạn tắt: thiếu quyền, phiên đã đóng, hoặc chưa nhận phiên.
  const disabledNote = !canReply
    ? 'Bạn chỉ có quyền xem hội thoại.'
    : session.isClosed
      ? 'Phiên đã kết thúc.'
      : !session.isHeld
        ? 'Nhận hội thoại để trả lời.'
        : null;

  return (
    <section
      className="card card-flush support-pane support-chat support-chat-open"
      aria-labelledby="support-chat-title"
    >
      <div className="support-chat-header">
        <h2 id="support-chat-title" ref={chatTitleRef} tabIndex={-1} className="support-pane-title">
          Khung chat
        </h2>
        {session.isHeld && !session.isClosed && canReply && (
          <div className="support-chat-actions">
            <button
              ref={transferButtonRef}
              type="button"
              className="btn btn-outline btn-sm"
              onClick={() => setIsTransferOpen(true)}
            >
              Chuyển
            </button>
            <button
              ref={endButtonRef}
              type="button"
              className="btn btn-danger btn-sm"
              onClick={() => setIsEndOpen(true)}
            >
              Kết thúc
            </button>
          </div>
        )}
      </div>
      {!session.isHeld && !session.isClosed && canReply && (
        <div className="support-chat-claim">
          <button
            type="button"
            className="btn btn-primary btn-sm"
            disabled={isClaiming}
            onClick={() => void handleClaim()}
          >
            {isClaiming ? 'Đang nhận...' : 'Nhận'}
          </button>
        </div>
      )}
      {claimError !== null && <ErrorAlert message={claimError} />}
      <QueueResult
        query={messagesQuery}
        loadingMessage="Đang nạp tin nhắn..."
        emptyMessage="Chưa có tin nhắn nào."
        errorMessage="Không tải được lịch sử tin."
        keepDataOnError
      >
        {(rows) => (
          <div
            ref={scrollRef}
            className="support-chat-scroll"
            role="log"
            aria-live="polite"
            aria-label="Tin nhắn"
            tabIndex={0}
            onScroll={handleScroll}
          >
            <ul className="support-chat-list">
              {rows.map((message, index) => {
                const previous = index > 0 ? rows[index - 1] : undefined;
                // Dòng phân cách chỉ khi cả hai tin liền nhau đều thuộc phiên và khác phiên nhau.
                const showSeparator =
                  previous !== undefined &&
                  previous.sessionId !== null &&
                  message.sessionId !== null &&
                  previous.sessionId !== message.sessionId;
                const time = formatVietnamTime(message.createdAt);
                return (
                  <Fragment key={message.id}>
                    {showSeparator && (
                      <li role="separator" className="support-chat-separator">
                        Phiên mới, {formatVietnamDateTime(message.createdAt)}
                      </li>
                    )}
                    {message.role === 'system' ? (
                      <li className="support-chat-message is-system">
                        <p className="support-chat-meta">
                          <span className="support-chat-sender">{senderLabel(message)}</span>
                          <span aria-hidden="true"> · </span>
                          <time dateTime={message.createdAt}>{time}</time>
                        </p>
                        {/* Tin hệ thống chỉ hiện nội dung chữ, không vẽ form theo `action` ở phía nhân viên. */}
                        <p className="support-chat-system">{message.body}</p>
                      </li>
                    ) : (
                      <li className={`support-chat-message is-${message.role}`}>
                        <p className="support-chat-meta">
                          <span className="support-chat-sender">{senderLabel(message)}</span>
                          <span aria-hidden="true"> · </span>
                          <time dateTime={message.createdAt}>{time}</time>
                        </p>
                        <div
                          className="support-chat-bubble"
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
          </div>
        )}
      </QueueResult>
      {sendError !== null && <ErrorAlert message={sendError} />}
      <StaffMessageComposer
        isHeld={session.isHeld && !session.isClosed}
        canReply={canReply}
        disabledNote={disabledNote}
        isSending={isSending}
        variables={variables}
        onSend={handleSend}
      />
      {isTransferOpen && (
        <TransferSessionDialog
          sessionId={session.sessionId}
          onSuccess={handleTransferSuccess}
          onSessionClosed={handleSessionAlreadyClosed}
          onLostHold={handleTransferLostHold}
          onClose={handleCloseTransfer}
        />
      )}
      {isEndOpen && (
        <EndSessionDialog
          sessionId={session.sessionId}
          onSuccess={handleEndSuccess}
          onSessionClosed={handleSessionAlreadyClosed}
          onClose={handleCloseEnd}
        />
      )}
    </section>
  );
}
