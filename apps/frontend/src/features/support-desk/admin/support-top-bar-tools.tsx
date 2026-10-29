import { useCan } from '../../identity/hooks/use-actor';
import { SupportConnectionStatus } from './connection-status';
import { PresenceToggle } from './presence-toggle';
import './support-top-bar.css';

// Công cụ CSKH trên thanh trên cùng của khu quản trị: trạng thái kết nối và công tắc sẵn sàng.
export function SupportTopBarTools() {
  const canRead = useCan('conversations:read');
  const canReply = useCan('conversations:reply');

  // Không có `conversations:read` thì không có luồng sự kiện, nên không có gì để hiện.
  if (!canRead) return null;

  return (
    <div className="admin-top-tools">
      <SupportConnectionStatus />
      {/* Người chỉ đọc không đổi được trạng thái. Backend cũng chặn `PUT` bằng `conversations:reply`. */}
      {canReply && <PresenceToggle />}
    </div>
  );
}
