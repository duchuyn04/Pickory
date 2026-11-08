import { type UseQueryResult } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { ErrorAlert } from '../../../shared/error-alert';

interface QueueResultProps<Row> {
  query: UseQueryResult<Row[]>;
  loadingMessage: string;
  emptyMessage: string;
  errorMessage: string;
  // Giữ dữ liệu đang hiện khi lần đọc lại nền lỗi. Khung chat dùng, để một lỗi mạng không xóa lịch sử tin.
  keepDataOnError?: boolean;
  children: (rows: Row[]) => ReactNode;
}

// Ba trạng thái dùng chung của một danh sách: đang nạp, lỗi có nút "Thử lại", và rỗng.
// `keepDataOnError` đúng thì lỗi đọc lại chỉ báo khi chưa có dữ liệu, còn dữ liệu cũ vẫn hiện.
export function QueueResult<Row>({
  query,
  loadingMessage,
  emptyMessage,
  errorMessage,
  keepDataOnError = false,
  children,
}: QueueResultProps<Row>) {
  const hasData = query.data !== undefined;
  if (query.isError && !(keepDataOnError && hasData)) {
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
  if (!hasData) {
    return (
      <p role="status" className="empty-state">
        {loadingMessage}
      </p>
    );
  }
  if (query.data.length === 0) return <p className="empty-state">{emptyMessage}</p>;
  return <>{children(query.data)}</>;
}
