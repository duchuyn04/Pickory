import { ErrorAlert } from '../../../shared/error-alert';
import { WarningAlert } from '../../../shared/warning-alert';
import type { EventSourceLike } from '../shared/support-event-stream';
import { SupportChatHead } from './support-chat-head';
import { SupportMessageComposer } from './support-message-composer';
import { SupportMessageList } from './support-message-list';
import { useSupportConversation } from './use-support-conversation';
import './support-customer.css';

interface SupportPageProps {
  // Test truyền `FakeEventSource` để quyết định thời điểm luồng mở hay nhận sự kiện. Trang thật không truyền.
  createEventSource?: (url: string) => EventSourceLike;
}

// Trang chat của khách. Dữ liệu và trạng thái đều đến từ máy chủ qua hook `useSupportConversation`.
export function SupportPage({ createEventSource }: SupportPageProps = {}) {
  const controller = useSupportConversation({ createEventSource });

  if (controller.loadError !== null) {
    return (
      <main className="page support-page">
        <header className="page-head">
          <div className="page-head-text">
            <h1>Liên hệ CSKH</h1>
            <p className="muted">Chat trực tiếp với nhân viên CSKH của Pickory.</p>
          </div>
        </header>
        <ErrorAlert message={controller.loadError} />
        <div>
          <button type="button" className="btn btn-outline" onClick={controller.retryLoad}>
            Thử lại
          </button>
        </div>
      </main>
    );
  }

  const showRetentionNotice = controller.conversation?.showRetentionNotice === true;

  return (
    <main className="page support-page">
      <header className="page-head">
        <div className="page-head-text">
          <h1>Liên hệ CSKH</h1>
          <p className="muted">Chat trực tiếp với nhân viên CSKH của Pickory.</p>
        </div>
      </header>
      <section className="card support-chat" aria-label="Khung chat hỗ trợ">
        <SupportChatHead
          sessionStatus={controller.sessionStatus}
          showRetentionNotice={showRetentionNotice}
        />
        {/* Chỉ báo mất kết nối khi hook có mở luồng. Trình duyệt thiếu `EventSource` thì không báo. */}
        {controller.hasEventStream &&
          controller.streamStatus === 'lost' &&
          !controller.isLoading && (
            <WarningAlert>Mất kết nối. Đang thử kết nối lại...</WarningAlert>
          )}
        {controller.isLoading ? (
          <p role="status" className="empty-state">
            Đang tải hội thoại...
          </p>
        ) : (
          <SupportMessageList messages={controller.messages} />
        )}
        {controller.sendError !== null && <ErrorAlert message={controller.sendError} />}
        <SupportMessageComposer
          isSending={controller.isSending}
          isReady={!controller.isLoading}
          onSend={controller.send}
        />
      </section>
    </main>
  );
}
