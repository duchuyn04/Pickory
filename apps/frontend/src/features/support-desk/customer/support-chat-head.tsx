import type { SupportSessionStatus } from './support-customer-schemas';

const SESSION_STATUS_META: Record<SupportSessionStatus, { label: string; badgeClass: string }> = {
  waiting: { label: 'Đang chờ nhân viên', badgeClass: 'badge badge-warn' },
  assigned: { label: 'Nhân viên đang hỗ trợ', badgeClass: 'badge badge-ok' },
  // Phiên đã đóng là trạng thái kết thúc, dùng badge trung tính không màu cảnh báo.
  closed: { label: 'Phiên hỗ trợ đã kết thúc', badgeClass: 'badge' },
};

interface SupportChatHeadProps {
  sessionStatus: SupportSessionStatus | null;
  showRetentionNotice: boolean;
}

// Đầu thẻ chat: tên kênh và badge trạng thái phiên. Vùng `role="status"` luôn có trong DOM.
// Chỉ đổi chữ và badge bên trong, để trình đọc màn hình đọc khi trạng thái xuất hiện hay đổi.
export function SupportChatHead({ sessionStatus, showRetentionNotice }: SupportChatHeadProps) {
  const statusMeta = sessionStatus !== null ? SESSION_STATUS_META[sessionStatus] : null;
  return (
    <div className="support-chat-head">
      <div className="support-chat-head-row">
        <h2 className="support-chat-title">CSKH Pickory</h2>
        <p role="status" className="support-chat-status">
          {statusMeta !== null && <span className={statusMeta.badgeClass}>{statusMeta.label}</span>}
        </p>
      </div>
      {showRetentionNotice && (
        <p className="support-chat-retention">Hội thoại này được lưu 90 ngày.</p>
      )}
    </div>
  );
}
