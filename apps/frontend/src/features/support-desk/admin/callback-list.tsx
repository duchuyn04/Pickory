import { useId } from 'react';
import { ErrorAlert } from '../../../shared/error-alert';
import { useCan } from '../../identity/hooks/use-actor';
import { CallbackRow } from './callback-row';
import { groupCallbackRequests } from './queue-view';
import type { CallbackRequest } from './support-desk-schemas';
import { useCallbackMarking } from './use-callback-marking';
import './callback-list.css';

// Nội dung tab Gọi lại: nhóm "Cần gọi lại" trước, nhóm "Đã xử lý" sau.
export function CallbackList({ requests }: { requests: CallbackRequest[] }) {
  const canMark = useCan('conversations:reply');
  const marking = useCallbackMarking(requests);
  // Mã riêng cho mỗi thể hiện, để hai danh sách cùng trang không trùng `id` của tiêu đề.
  const idPrefix = useId();
  const pendingTitleId = `${idPrefix}-pending-title`;
  const handledTitleId = `${idPrefix}-handled-title`;

  const { pending, handled, hasHiddenHandled } = groupCallbackRequests(requests);

  function renderRows(rows: CallbackRequest[]) {
    return rows.map((request) => (
      <CallbackRow
        key={request.id}
        request={request}
        canMark={canMark}
        savingStatus={marking.savingStatusByRow[request.id] ?? null}
        errorMessage={marking.errorByRow[request.id] ?? null}
        onMark={(row, status) => void marking.markRow(row, status)}
      />
    ));
  }

  return (
    <div className="support-callbacks" role="group" aria-label="Yêu cầu gọi lại">
      {/* Vùng thông báo luôn có trong DOM, để trình đọc màn hình nhận ra khi nội dung đổi. */}
      <p role="status" className="support-callbacks-status">
        {marking.statusMessage}
      </p>
      {marking.listError !== null && (
        <ErrorAlert className="support-callbacks-error" message={marking.listError} />
      )}
      {pending.length > 0 ? (
        <section className="support-callback-group">
          <h3
            id={pendingTitleId}
            ref={marking.pendingHeadingRef}
            tabIndex={-1}
            className="support-callback-group-title"
          >
            Cần gọi lại <span className="badge badge-info">{pending.length}</span>
          </h3>
          <ul className="support-callback-list" aria-labelledby={pendingTitleId}>
            {renderRows(pending)}
          </ul>
        </section>
      ) : (
        <p className="empty-state">
          {/* Danh sách hết dòng vẫn do `CallbackList` vẽ, nên `listError` không mất khi `QueueResult` đổi nhánh. */}
          {requests.length === 0
            ? 'Chưa có yêu cầu gọi lại nào.'
            : 'Không có yêu cầu nào cần gọi lại.'}
        </p>
      )}
      {handled.length > 0 && (
        <section className="support-callback-group">
          <h3
            id={handledTitleId}
            ref={marking.handledHeadingRef}
            tabIndex={-1}
            className="support-callback-group-title"
          >
            Đã xử lý
          </h3>
          <ul className="support-callback-list" aria-labelledby={handledTitleId}>
            {renderRows(handled)}
          </ul>
          {hasHiddenHandled && (
            <p className="support-callbacks-note">Chỉ hiện 20 yêu cầu đã xử lý gần nhất.</p>
          )}
        </section>
      )}
    </div>
  );
}
