import { Icon } from '../../../shared/icons';
import { useSupportEvents } from './support-events-provider';
import type { StreamStatus } from './support-event-stream';

// Vùng `aria-live` luôn có trong trang, nên trình đọc màn hình đọc được chữ khi nó xuất hiện sau đó.
// Không dùng `role="status"` vì các màn quản trị đếm vùng `status` để biết trang còn đang tải.
export function ConnectionStatus({ status }: { status: StreamStatus }) {
  return (
    <span aria-live="polite" className="connection-status">
      {status === 'lost' && (
        <span
          className="connection-status-lost"
          title="Đang thử kết nối lại. Trạng thái sẵn sàng có thể chưa cập nhật."
        >
          <Icon name="alertTriangle" className="icon" />
          Mất kết nối
        </span>
      )}
    </span>
  );
}

export function SupportConnectionStatus() {
  const { status } = useSupportEvents();
  return <ConnectionStatus status={status} />;
}
