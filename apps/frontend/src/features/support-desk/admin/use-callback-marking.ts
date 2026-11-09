import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { ApiError } from '../../../shared/api';
import { phoneOrDeletedLabel, STATUS_LABELS } from './callback-labels';
import { PENDING_CALLBACK_STATUS } from './queue-view';
import {
  CALLBACKS_QUERY_KEY,
  updateCallbackRequestStatus,
  type HandledCallbackStatus,
} from './support-desk-api';
import type { CallbackRequest } from './support-desk-schemas';

const SAVE_ERROR_FALLBACK = 'Chưa lưu được trạng thái. Hãy thử lại.';
const NOT_FOUND_CODE = 'CALLBACK_REQUEST_NOT_FOUND';

// Bản sao của bản ghi không còn khóa đã cho. React cần một object mới để vẽ lại.
function withoutKey<Value>(record: Record<string, Value>, key: string): Record<string, Value> {
  const copy = { ...record };
  delete copy[key];
  return copy;
}

// Trạng thái và thao tác đánh dấu yêu cầu gọi lại. Phần vẽ nằm ở `CallbackList`.
export function useCallbackMarking(requests: CallbackRequest[]) {
  const queryClient = useQueryClient();
  const [savingStatusByRow, setSavingStatusByRow] = useState<Record<string, HandledCallbackStatus>>(
    {},
  );
  const [errorByRow, setErrorByRow] = useState<Record<string, string>>({});
  // Lỗi 404 không gắn vào dòng, vì lần đọc lại sẽ gỡ dòng và lỗi biến mất theo.
  const [listError, setListError] = useState<string | null>(null);
  const [statusMessage, setStatusMessage] = useState('');
  // Mã dòng vừa đánh dấu thành công, chờ danh sách vẽ lại để đưa tiêu điểm đi.
  const markedRowIdRef = useRef<string | null>(null);
  // Ref chặn bấm đôi ngay lập tức, vì state chỉ có hiệu lực sau lần vẽ kế tiếp. Theo từng dòng.
  const savingRowIdsRef = useRef(new Set<string>());
  const pendingHeadingRef = useRef<HTMLHeadingElement>(null);
  const handledHeadingRef = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    const markedRowId = markedRowIdRef.current;
    if (markedRowId === null) return;
    // Cache được cập nhật sau khi API trả lời, không cùng lúc với state. Chờ tới lần vẽ mà dòng đã rời nhóm Cần gọi lại.
    const markedRow = requests.find((request) => request.id === markedRowId);
    if (markedRow?.status === PENDING_CALLBACK_STATUS) return;
    // Xóa mã ngay để các lần đọc lại sau không kéo tiêu điểm thêm lần nữa.
    markedRowIdRef.current = null;
    // Nhân viên có thể đã chuyển sang việc khác, ví dụ nút của dòng khác hoặc ô soạn tin. Chỉ đưa tiêu điểm khi nó đang trống.
    const focused = document.activeElement;
    if (focused !== null && focused !== document.body) return;
    // Nút của dòng đã bị gỡ. Đưa tiêu điểm về tiêu đề nhóm còn lại để bàn phím không mất vị trí.
    const headingToFocus = pendingHeadingRef.current ?? handledHeadingRef.current;
    headingToFocus?.focus();
  }, [requests]);

  async function markRow(request: CallbackRequest, status: HandledCallbackStatus) {
    if (savingRowIdsRef.current.has(request.id)) return;
    // Khóa dòng ngay để hai lần bấm không tạo hai request cho cùng một yêu cầu.
    savingRowIdsRef.current.add(request.id);
    setSavingStatusByRow((current) => ({ ...current, [request.id]: status }));
    // Xóa kết quả của lần trước, để lỗi cũ hoặc câu thông báo cũ không đứng cạnh kết quả mới.
    setErrorByRow((current) => withoutKey(current, request.id));
    setListError(null);
    setStatusMessage('');
    try {
      const updated = await updateCallbackRequestStatus(request.id, status);
      // Ghi dòng vừa đánh dấu trước khi cache đổi, để lần vẽ lại đầu tiên đã thấy mã này.
      markedRowIdRef.current = updated.id;
      // Thay dòng cũ bằng dòng máy chủ trả, giữ nguyên thứ tự API, để dòng chuyển nhóm ngay.
      queryClient.setQueryData<CallbackRequest[]>(CALLBACKS_QUERY_KEY, (current) =>
        current?.map((row) => (row.id === updated.id ? updated : row)),
      );
      // Đọc lại để khớp với máy chủ, vì nhân viên khác có thể đã thêm hoặc xử lý yêu cầu.
      void queryClient.invalidateQueries({ queryKey: CALLBACKS_QUERY_KEY });
      // Cập nhật vùng `role="status"` để trình đọc màn hình đọc kết quả.
      setStatusMessage(
        `Đã đánh dấu yêu cầu ${phoneOrDeletedLabel(updated)} là ${STATUS_LABELS[status]}.`,
      );
    } catch (error) {
      const message = error instanceof ApiError ? error.message : SAVE_ERROR_FALLBACK;
      if (error instanceof ApiError && error.code === NOT_FOUND_CODE) {
        // Yêu cầu đã bị xóa. Lần đọc lại gỡ dòng, nên thông báo nằm ngoài dòng để còn đọc được.
        setListError(message);
        void queryClient.invalidateQueries({ queryKey: CALLBACKS_QUERY_KEY });
      } else {
        // Chỉ dòng này hiện lỗi. Lỗi của dòng khác giữ nguyên.
        setErrorByRow((current) => ({ ...current, [request.id]: message }));
      }
    } finally {
      // Mở khóa dòng dù thành công hay lỗi, để nút không bị kẹt ở trạng thái đang lưu.
      savingRowIdsRef.current.delete(request.id);
      setSavingStatusByRow((current) => withoutKey(current, request.id));
    }
  }

  return {
    savingStatusByRow,
    errorByRow,
    listError,
    statusMessage,
    markRow,
    pendingHeadingRef,
    handledHeadingRef,
  };
}
