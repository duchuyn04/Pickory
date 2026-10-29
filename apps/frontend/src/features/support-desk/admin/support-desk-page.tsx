import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { useCan } from '../../identity/hooks/use-actor';
import { AlertControls } from './alert-controls';
import { QueueColumn } from './queue-column';
import './support-desk.css';

// Màn CSKH 3 cột: hàng chờ, khung chat, thông tin khách. Hai cột sau là khung trống, các task sau lấp nội dung.
export function SupportDeskPage() {
  const canRead = useCan('conversations:read');
  const canReply = useCan('conversations:reply');

  // Thiếu quyền thì không vẽ cột nào, nên không có query nào chạy và không gọi API của support desk.
  if (!canRead) {
    return <ForbiddenNotice permission="conversations:read" subject="màn chăm sóc khách hàng" />;
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
        <QueueColumn />

        <section className="card card-flush support-pane" aria-labelledby="support-chat-title">
          <h2 id="support-chat-title" className="support-pane-title">
            Khung chat
          </h2>
          <p className="support-pane-empty">Tin nhắn của hội thoại đang mở sẽ hiện ở đây.</p>
        </section>

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
