import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import { useEffect, useState, type ReactNode } from 'react';
import { ErrorAlert } from '../../../shared/error-alert';
import { TabList, tabId } from '../../../shared/tab-list';
import { formatVietnamDateTime } from '../../../shared/vietnam-time';
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

interface QueueResultProps<Row> {
  query: UseQueryResult<Row[]>;
  loadingMessage: string;
  emptyMessage: string;
  errorMessage: string;
  children: (rows: Row[]) => ReactNode;
}

// Ba trạng thái dùng chung của mỗi tab: đang nạp, lỗi có nút "Thử lại", và rỗng.
function QueueResult<Row>({
  query,
  loadingMessage,
  emptyMessage,
  errorMessage,
  children,
}: QueueResultProps<Row>) {
  if (query.isError) {
    return (
      <div className="support-queue-alert">
        <ErrorAlert message={errorMessage} />
        <button
          type="button"
          className="btn btn-sm"
          disabled={query.isFetching}
          onClick={() => void query.refetch()}
        >
          Thử lại
        </button>
      </div>
    );
  }
  if (query.data === undefined) {
    return (
      <p role="status" className="empty-state">
        {loadingMessage}
      </p>
    );
  }
  if (query.data.length === 0) return <p className="empty-state">{emptyMessage}</p>;
  return <>{children(query.data)}</>;
}

function SessionList({
  label,
  sessions,
  fetchedAt,
}: {
  label: string;
  sessions: QueueSession[];
  fetchedAt: number;
}) {
  const now = useNow(CLOCK_TICK_MS);
  // `waitingSeconds` là số giây lúc máy chủ trả danh sách. Cộng thêm thời gian trôi qua từ lúc đó.
  const secondsSinceFetch = Math.max(0, Math.floor((now - fetchedAt) / 1000));

  return (
    <ul className="support-queue-list" aria-label={label}>
      {sessions.map((session) => (
        <li key={session.id} className="support-queue-item">
          {/* Tin của khách là dữ liệu bên ngoài, nên chỉ vẽ bằng text node của React. */}
          <p className="support-queue-preview wrap-anywhere">{session.preview}</p>
          <span className="cell-sub">
            Chờ {formatWaitingTime(session.waitingSeconds + secondsSinceFetch)}
          </span>
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
export function QueueColumn() {
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
