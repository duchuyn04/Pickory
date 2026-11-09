import { useEffect, useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { useCan } from '../../identity/hooks/use-actor';
import { AlertControls } from './alert-controls';
import { CustomerPanel } from './customer-panel';
import { QueueColumn } from './queue-column';
import { SupportChat } from './support-chat';
import { useSupportEvents } from './support-events-provider';
import { SESSIONS_QUERY_KEY, SUPPORT_QUERY_KEY, fetchSessionCustomer } from './support-desk-api';
import type { CannedResponseVariables } from './render-canned-response';
import { parseSessionClosedEvent, type QueueSession } from './support-desk-schemas';
import type { OpenSupportSession } from './support-desk-session';
import './support-desk.css';

// Màn CSKH 3 cột: hàng chờ, khung chat, thông tin khách.
export function SupportDeskPage() {
  const canRead = useCan('conversations:read');
  const canReply = useCan('conversations:reply');
  const { subscribe } = useSupportEvents();
  const queryClient = useQueryClient();
  const [openSession, setOpenSession] = useState<OpenSupportSession | null>(null);
  const [transferStatusMessage, setTransferStatusMessage] = useState<string | null>(null);
  const statusNoticeRef = useRef<HTMLDivElement>(null);

  // Đưa tiêu điểm về vùng thông báo ngay khi có kết quả chuyển phiên thành công.
  useEffect(() => {
    if (transferStatusMessage) {
      statusNoticeRef.current?.focus();
    }
  }, [transferStatusMessage]);

  const sessionId = openSession?.sessionId ?? null;
  // Đọc dữ liệu khách hàng của phiên đang mở.
  // Chỉ nạp dữ liệu khi có phiên mở. Tắt thử lại để hiển thị nút Thử lại ngay khi gặp lỗi.
  const customerQuery = useQuery({
    queryKey: [...SUPPORT_QUERY_KEY, 'customer', sessionId],
    queryFn: () => fetchSessionCustomer(sessionId as string),
    enabled: sessionId !== null,
    retry: false,
  });

  const customerData = customerQuery.data;
  // Tên khách lấy từ hồ sơ người dùng. Chuỗi rỗng được coi là chưa có thông tin.
  const tenKhach = customerData?.customer.name?.trim() ? customerData.customer.name.trim() : null;
  // Mã đơn lấy từ đơn đầu tiên là đơn mới nhất theo quyết định đã chốt.
  const maDon = customerData?.orders[0]?.code ?? null;
  const cannedVariables: CannedResponseVariables = {
    tenKhach,
    maDon,
  };
  // Sự kiện đóng phiên của chính phiên đang mở: ô soạn tắt với chú thích phiên đã kết thúc.
  // Sự kiện của phiên khác không đổi trạng thái khung chat đang mở.
  useEffect(() => {
    return subscribe((eventName, data) => {
      if (eventName !== 'session.closed') return;
      const closed = parseSessionClosedEvent(data);
      if (!closed) return;
      setOpenSession((current) =>
        current !== null && current.sessionId === closed.sessionId
          ? { ...current, isHeld: false, isClosed: true }
          : current,
      );
    });
  }, [subscribe]);

  // Thiếu quyền thì không vẽ cột nào, nên không có query nào chạy và không gọi API của support desk.
  if (!canRead) {
    return <ForbiddenNotice permission="conversations:read" subject="màn chăm sóc khách hàng" />;
  }

  // Phiên mở từ tab Của tôi đang được giữ; mở từ tab Đang chờ thì chưa.
  // Bấm lại đúng dòng đang mở thì giữ trạng thái hiện tại, để danh sách đọc lại chưa xong không tắt ô soạn.
  function handleSelectSession(session: QueueSession, source: 'waiting' | 'mine') {
    // Xóa thông báo chuyển thành công của phiên trước khi người dùng chọn phiên khác.
    setTransferStatusMessage(null);
    setOpenSession((current) => {
      if (current?.sessionId === session.id) return current;
      return {
        sessionId: session.id,
        conversationId: session.conversationId,
        isHeld: source === 'mine',
        isClosed: false,
      };
    });
  }

  function handleClaimed(sessionId: string) {
    setOpenSession((current) =>
      current?.sessionId === sessionId ? { ...current, isHeld: true } : current,
    );
  }

  function handleLostHold(sessionId: string) {
    setOpenSession((current) =>
      current?.sessionId === sessionId ? { ...current, isHeld: false } : current,
    );
  }

  // Chuyển thành công: đóng phiên đang mở, hiện thông báo và nạp lại danh sách phiên.
  function handleTransferSuccess(recipientName: string) {
    setTransferStatusMessage(`Đã chuyển hội thoại cho ${recipientName}.`);
    setOpenSession(null);
    void queryClient.invalidateQueries({ queryKey: SESSIONS_QUERY_KEY });
  }

  // Cập nhật trạng thái phiên khi kết thúc hoặc khi máy chủ báo phiên đã đóng.
  function handleSessionClosed(closedSessionId: string) {
    setOpenSession((current) =>
      current?.sessionId === closedSessionId
        ? { ...current, isHeld: false, isClosed: true }
        : current,
    );
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Chăm sóc khách hàng</h1>
          <p>Theo dõi khách đang chờ và nhận hội thoại ngay khi có người vào hàng chờ.</p>
        </div>
        {/* Người chỉ đọc không bao giờ nhận được báo, nên không có gì để bật hay tắt. */}
        {canReply && <AlertControls />}
      </div>
      {/* Vùng thông báo role=status luôn hiện diện trong DOM để trình đọc màn hình theo dõi cập nhật. */}
      <div
        ref={statusNoticeRef}
        role="status"
        tabIndex={-1}
        className={`support-transfer-status ${transferStatusMessage ? 'alert alert-ok' : ''}`}
      >
        {transferStatusMessage}
      </div>

      <div className="support-desk-grid">
        <QueueColumn
          selectedSessionId={openSession?.sessionId ?? null}
          onSelectSession={handleSelectSession}
        />

        {/* `key` theo mã phiên: đổi phiên dựng lại khung chat, để nháp tin và câu lỗi của phiên cũ không sang phiên mới. */}
        <SupportChat
          key={openSession?.sessionId ?? 'none'}
          session={openSession}
          canReply={canReply}
          variables={cannedVariables}
          onClaimed={handleClaimed}
          onLostHold={handleLostHold}
          onTransferSuccess={handleTransferSuccess}
          onSessionClosed={handleSessionClosed}
        />

        {/* Tiền tố customer- tránh trùng key với SupportChat trong cùng thẻ cha, và tự động đóng ngăn khi đổi phiên. */}
        <CustomerPanel
          key={openSession?.sessionId ? `customer-${openSession.sessionId}` : 'customer-none'}
          session={openSession}
          customerQuery={customerQuery}
        />
      </div>
    </div>
  );
}
