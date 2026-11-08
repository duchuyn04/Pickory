import { useEffect, useState } from 'react';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { useCan } from '../../identity/hooks/use-actor';
import { AlertControls } from './alert-controls';
import { QueueColumn } from './queue-column';
import { SupportChat } from './support-chat';
import { useSupportEvents } from './support-events-provider';
import { parseSessionClosedEvent, type QueueSession } from './support-desk-schemas';
import type { OpenSupportSession } from './support-desk-session';
import './support-desk.css';

// Màn CSKH 3 cột: hàng chờ, khung chat, thông tin khách. Cột thông tin khách đợi SD-UI3 lấp nội dung.
export function SupportDeskPage() {
  const canRead = useCan('conversations:read');
  const canReply = useCan('conversations:reply');
  const { subscribe } = useSupportEvents();
  const [openSession, setOpenSession] = useState<OpenSupportSession | null>(null);

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
          variables={{ tenKhach: null, maDon: null }}
          onClaimed={handleClaimed}
          onLostHold={handleLostHold}
        />

        <section className="card card-flush support-pane" aria-labelledby="support-customer-title">
          <h2 id="support-customer-title" className="support-pane-title">
            Thông tin khách
          </h2>
          <p className="support-pane-empty">Tên, email và đơn gần đây của khách sẽ hiện ở đây.</p>
        </section>
      </div>
    </div>
  );
}
