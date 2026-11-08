import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { TabList, tabId } from '../../../shared/tab-list';
import { formatVietnamDateTime } from '../../../shared/vietnam-time';
import { QueueResult } from './queue-result';
import {
  CALLBACKS_QUERY_KEY,
  SESSIONS_QUERY_KEY,
  fetchCallbackRequests,
  fetchSessions,
} from './support-desk-api';
import type { CallbackRequest, QueueSession } from './support-desk-schemas';
import { formatWaitingTime, selectPendingCallbacks, waitingBadgeCount } from './queue-view';

type QueueTab = 'waiting' | 'mine' | 'callbacks';

const ID_PREFIX = 'support-queue';
const PANEL_ID = 'support-queue-panel';
// Nhãn "Chờ ..." chạy theo đồng hồ máy khách giữa hai lần đọc danh sách. Nửa phút một lần là đủ cho đơn vị phút.
const CLOCK_TICK_MS = 30_000;

// Hook trả giờ hiện tại và cập nhật định kỳ. Thời gian chờ trên dòng nhờ đó không đứng yên giữa hai lần đọc danh sách.
function useNow(intervalMs: number): number {
  const [now, setNow] = useState(Date.now);
  useEffect(() => {
    const timerId = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(timerId);
  }, [intervalMs]);
  return now;
}

function SessionList({
  label,
  sessions,
  fetchedAt,
  selectedSessionId,
  onSelect,
}: {
  label: string;
  sessions: QueueSession[];
  fetchedAt: number;
  selectedSessionId: string | null;
  onSelect: (session: QueueSession) => void;
}) {
  const now = useNow(CLOCK_TICK_MS);
  // `waitingSeconds` là số giây lúc máy chủ trả danh sách. Cộng thêm thời gian trôi qua từ lúc đó.
  const secondsSinceFetch = Math.max(0, Math.floor((now - fetchedAt) / 1000));

  return (
    <ul className="support-queue-list" aria-label={label}>
      {sessions.map((session) => (
        <li key={session.id}>
          {/* Dòng là một nút chọn phiên cho khung chat, dùng được bằng bàn phím như mọi nút khác. */}
          <button
            type="button"
            className="support-queue-item"
            aria-current={session.id === selectedSessionId ? 'true' : undefined}
            onClick={() => onSelect(session)}
          >
            {/* Tin của khách là dữ liệu bên ngoài, nên chỉ vẽ bằng text node của React. */}
            {/* `span` thay `p` vì nút chỉ chứa phrasing content. Lớp CSS vẫn hiển thị dạng khối. */}
            <span className="support-queue-preview wrap-anywhere">{session.preview}</span>
            <span className="cell-sub">
              Chờ {formatWaitingTime(session.waitingSeconds + secondsSinceFetch)}
            </span>
          </button>
        </li>
      ))}
    </ul>
  );
}

function CallbackList({ requests }: { requests: CallbackRequest[] }) {
  return (
    <ul className="support-queue-list" aria-label="Yêu cầu gọi lại">
      {requests.map((request) => (
        <li key={request.id} className="support-queue-item">
          <p className="support-queue-preview wrap-anywhere">{request.phone}</p>
          <span className="cell-sub">Gửi lúc {formatVietnamDateTime(request.createdAt)}</span>
        </li>
      ))}
    </ul>
  );
}

// Cột hàng chờ: ba tab Đang chờ, Của tôi, Gọi lại. Mỗi tab đọc danh sách riêng từ API.
interface QueueColumnProps {
  selectedSessionId: string | null;
  // Nguồn `waiting` nghĩa là phiên chưa giữ; `mine` nghĩa là phiên đang giữ.
  onSelectSession: (session: QueueSession, source: 'waiting' | 'mine') => void;
}

export function QueueColumn({ selectedSessionId, onSelectSession }: QueueColumnProps) {
  const [selectedTab, setSelectedTab] = useState<QueueTab>('waiting');

  // Hàng chờ luôn được đọc, vì số dòng của nó là badge trên tab dù nhân viên đang xem tab khác.
  const waitingQuery = useQuery({
    queryKey: [...SESSIONS_QUERY_KEY, 'waiting'],
    queryFn: () => fetchSessions('waiting'),
    // Route có thể chưa có (trả 404). Không thử lại để cột hiện lỗi và nút "Thử lại" ngay.
    retry: false,
  });
  // Hai tab còn lại chỉ đọc khi đang mở, để mỗi lần luồng SSE mở lại chỉ đọc danh sách đang xem.
  const mineQuery = useQuery({
    queryKey: [...SESSIONS_QUERY_KEY, 'mine'],
    queryFn: () => fetchSessions('mine'),
    enabled: selectedTab === 'mine',
    retry: false,
  });
  const callbacksQuery = useQuery({
    queryKey: CALLBACKS_QUERY_KEY,
    queryFn: fetchCallbackRequests,
    enabled: selectedTab === 'callbacks',
    retry: false,
  });

  const tabs = [
    { code: 'waiting', label: 'Đang chờ', count: waitingBadgeCount(waitingQuery.data) },
    { code: 'mine', label: 'Của tôi' },
    { code: 'callbacks', label: 'Gọi lại' },
  ];

  return (
    <section className="card card-flush support-queue" aria-label="Hàng chờ">
      <TabList
        label="Hàng chờ chăm sóc khách hàng"
        idPrefix={ID_PREFIX}
        panelId={PANEL_ID}
        tabs={tabs}
        selected={selectedTab}
        onSelect={(code) => setSelectedTab(code as QueueTab)}
      />

      <div id={PANEL_ID} role="tabpanel" aria-labelledby={tabId(ID_PREFIX, selectedTab)}>
        {selectedTab === 'waiting' && (
          <QueueResult
            query={waitingQuery}
            loadingMessage="Đang nạp hàng chờ..."
            emptyMessage="Chưa có khách nào đang chờ."
            errorMessage="Không tải được hàng chờ."
          >
            {(sessions) => (
              <SessionList
                label="Phiên đang chờ"
                sessions={sessions}
                fetchedAt={waitingQuery.dataUpdatedAt}
                selectedSessionId={selectedSessionId}
                onSelect={(session) => onSelectSession(session, 'waiting')}
              />
            )}
          </QueueResult>
        )}
        {selectedTab === 'mine' && (
          <QueueResult
            query={mineQuery}
            loadingMessage="Đang nạp hội thoại của bạn..."
            emptyMessage="Bạn chưa nhận hội thoại nào."
            errorMessage="Không tải được hội thoại của bạn."
          >
            {(sessions) => (
              <SessionList
                label="Phiên của tôi"
                sessions={sessions}
                fetchedAt={mineQuery.dataUpdatedAt}
                selectedSessionId={selectedSessionId}
                onSelect={(session) => onSelectSession(session, 'mine')}
              />
            )}
          </QueueResult>
        )}
        {selectedTab === 'callbacks' && (
          <QueueResult
            query={callbacksQuery}
            loadingMessage="Đang nạp yêu cầu gọi lại..."
            emptyMessage="Chưa có yêu cầu gọi lại nào."
            errorMessage="Không tải được yêu cầu gọi lại."
          >
            {(requests) => {
              // Chỉ dòng `pending` là việc còn phải làm. Lọc ở client vì hợp đồng trả cả các dòng đã xong.
              const pendingRequests = selectPendingCallbacks(requests);
              return pendingRequests.length === 0 ? (
                <p className="empty-state">Chưa có yêu cầu gọi lại nào.</p>
              ) : (
                <CallbackList requests={pendingRequests} />
              );
            }}
          </QueueResult>
        )}
      </div>
    </section>
  );
}
