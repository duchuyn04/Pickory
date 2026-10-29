import { useQueryClient, type QueryClient } from '@tanstack/react-query';
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { useCan } from '../../identity/hooks/use-actor';
import { applyPresenceChange } from './presence-state';
import {
  PRESENCE_QUERY_KEY,
  SESSIONS_QUERY_KEY,
  SUPPORT_EVENTS_URL,
  SUPPORT_QUERY_KEY,
} from './support-desk-api';
import { parsePresenceChange, type StaffPresence } from './support-desk-schemas';
import { SupportEventStream, type StreamStatus } from './support-event-stream';

const PRESENCE_EVENT = 'presence.changed';
// Ba sự kiện của phiên. Hợp đồng ở quyết định 30 của `tasks/support-desk/plan.md`.
const SESSION_EVENTS = ['session.waiting', 'session.assigned', 'session.closed'];
const SUPPORT_EVENT_NAMES = [PRESENCE_EVENT, ...SESSION_EVENTS];

export type SupportEventHandler = (eventName: string, data: unknown) => void;

interface SupportEventsValue {
  status: StreamStatus;
  // Đăng ký nhận mọi sự kiện của luồng. Hàm trả về dùng để hủy đăng ký.
  subscribe: (handler: SupportEventHandler) => () => void;
}

const SupportEventsContext = createContext<SupportEventsValue | null>(null);

export function useSupportEvents(): SupportEventsValue {
  const value = useContext(SupportEventsContext);
  if (!value) throw new Error('useSupportEvents phải nằm trong SupportEventsProvider.');
  return value;
}

// Trình duyệt quá cũ không có `EventSource` thì không có luồng nào để nối, nên báo mất kết nối ngay.
function initialStatus(): StreamStatus {
  return typeof EventSource === 'undefined' ? 'lost' : 'connecting';
}

// Đưa thay đổi trạng thái từ sự kiện vào danh sách đang sẵn sàng trong cache.
function applyPresenceEventToCache(queryClient: QueryClient, data: unknown): void {
  const change = parsePresenceChange(data);
  // Sự kiện sai dạng bị bỏ qua. Một gói tin lỗi không được làm hỏng danh sách đang hiện.
  if (!change) return;
  queryClient.setQueryData<StaffPresence[]>(PRESENCE_QUERY_KEY, (current) =>
    // Danh sách chưa tải xong thì để query tự đọc. Ghi đè bằng mảng rỗng sẽ giả làm "không ai sẵn sàng".
    current ? applyPresenceChange(current, change, new Date().toISOString()) : current,
  );
}

// Giữ đúng một kết nối SSE cho mỗi tab, dùng chung cho công tắc, âm báo và màn CSKH.
// Nhịp tim chỉ chạy khi luồng mở, nên luồng phải sống suốt khu quản trị chứ không chỉ ở trang CSKH.
export function SupportEventsProvider({ children }: { children: ReactNode }) {
  const canReadConversations = useCan('conversations:read');
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<StreamStatus>(initialStatus);
  const handlersRef = useRef(new Set<SupportEventHandler>());

  useEffect(() => {
    // Người thiếu quyền không mở luồng. Backend cũng trả 403, nhưng không gọi thì khỏi sinh lỗi.
    if (!canReadConversations) return;
    if (typeof EventSource === 'undefined') return;

    // Đặt lại trạng thái hiển thị. Lần dựng lại của StrictMode không được giữ chữ `open` của luồng đã đóng.
    setStatus('connecting');
    const stream = new SupportEventStream(
      { url: SUPPORT_EVENTS_URL, eventNames: SUPPORT_EVENT_NAMES },
      {
        onStatusChange: setStatus,
        // Sự kiện phát lúc luồng đứt đã mất. Đọc lại trạng thái và mọi danh sách mỗi lần luồng mở.
        onOpen: () => {
          void queryClient.invalidateQueries({ queryKey: SUPPORT_QUERY_KEY });
        },
        onEvent: (eventName, data) => {
          if (SESSION_EVENTS.includes(eventName)) {
            // Sự kiện của phiên chỉ báo có thay đổi. Client đọc lại danh sách thay vì tin dữ liệu trong sự kiện.
            void queryClient.invalidateQueries({ queryKey: SESSIONS_QUERY_KEY });
          }
          if (eventName === PRESENCE_EVENT) applyPresenceEventToCache(queryClient, data);
          for (const handler of handlersRef.current) handler(eventName, data);
        },
      },
    );
    stream.start();
    // Đóng luồng khi rời khu quản trị, ví dụ lúc đăng xuất. Luồng còn mở thì máy chủ vẫn ghi nhịp tim.
    return () => stream.stop();
  }, [canReadConversations, queryClient]);

  const subscribe = useCallback((handler: SupportEventHandler) => {
    handlersRef.current.add(handler);
    return () => {
      handlersRef.current.delete(handler);
    };
  }, []);

  const value = useMemo(() => ({ status, subscribe }), [status, subscribe]);
  return <SupportEventsContext.Provider value={value}>{children}</SupportEventsContext.Provider>;
}
