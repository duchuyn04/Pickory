import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { ErrorAlert } from '../../../shared/error-alert';
import { getErrorMessage } from '../../../shared/error-message';
import {
  CannedResponseFormDialog,
  type CannedResponseFormTarget,
} from './canned-response-form-dialog';
import { DeleteCannedResponseDialog } from './delete-canned-response-dialog';
import { CANNED_RESPONSES_QUERY_KEY, fetchCannedResponses } from './support-desk-api';
import type { CannedResponse } from './support-desk-schemas';
import './canned-responses.css';

// `shared` là mẫu dùng chung cho mọi nhân viên. `personal` là mẫu riêng của người đang đăng nhập.
export type CannedResponseScope = 'shared' | 'personal';

interface ScopeText {
  intro: string;
  empty: string;
  loadError: string;
}

const SCOPE_TEXT: Record<CannedResponseScope, ScopeText> = {
  shared: {
    intro:
      'Mọi nhân viên chăm sóc khách hàng chèn được các mẫu này khi trả lời. Mẫu lưu ngay khi bấm Lưu mẫu, không cần bấm Lưu cấu hình.',
    empty: 'Chưa có mẫu dùng chung nào.',
    loadError: 'Không tải được mẫu dùng chung.',
  },
  personal: {
    intro: 'Chỉ bạn thấy các mẫu này. Gõ / trong khung soạn tin để chèn.',
    empty: 'Bạn chưa có mẫu nào.',
    loadError: 'Không tải được mẫu của bạn.',
  },
};

interface CannedResponsePanelProps {
  scope: CannedResponseScope;
  // `false` khi người dùng thiếu quyền `conversations:reply`. Khối không gọi API và không cho thêm mẫu.
  canUse: boolean;
}

// Thông báo kết quả của lần thêm, sửa hoặc xóa gần nhất. Lỗi hiện bằng `ErrorAlert`, thành công hiện bằng chữ trạng thái.
interface PanelNotice {
  kind: 'success' | 'error';
  text: string;
}

// Danh sách, nút thêm, hộp thoại sửa và hộp thoại xóa của một loại mẫu. Hai màn dùng chung bộ này.
export function CannedResponsePanel({ scope, canUse }: CannedResponsePanelProps) {
  const queryClient = useQueryClient();
  const text = SCOPE_TEXT[scope];
  const [formTarget, setFormTarget] = useState<CannedResponseFormTarget | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<CannedResponse | null>(null);
  const [notice, setNotice] = useState<PanelNotice | null>(null);
  const addButtonRef = useRef<HTMLButtonElement>(null);
  // Mẫu vừa xóa làm mất nút đã mở hộp thoại, nên cờ này nhắc đưa focus về nút "Thêm mẫu".
  const restoreFocusToAddRef = useRef(false);

  const cannedQuery = useQuery({
    queryKey: CANNED_RESPONSES_QUERY_KEY,
    queryFn: fetchCannedResponses,
    // Thiếu quyền thì route chắc chắn trả 403, nên không gọi.
    enabled: canUse,
    // Tắt tải lại khi focus cửa sổ để danh sách không nhảy lúc nhân viên đang sửa.
    refetchOnWindowFocus: false,
  });

  // Chỉ trả focus khi không còn hộp thoại nào mở. Cờ bật do mẫu vừa xóa hoặc đã bị xóa ở nơi khác làm mất nút đã mở hộp.
  useEffect(() => {
    if (formTarget !== null || deleteTarget !== null || !restoreFocusToAddRef.current) return;
    restoreFocusToAddRef.current = false;
    addButtonRef.current?.focus();
  }, [formTarget, deleteTarget]);

  // Danh sách chung trả cả mẫu cá nhân của người gọi, nên lọc theo loại của khối này.
  const isSharedScope = scope === 'shared';
  const visibleResponses = (cannedQuery.data ?? []).filter(
    (response) => response.shared === isSharedScope,
  );

  // Đọc lại danh sách để khung soạn tin của màn chăm sóc khách hàng dùng ngay bản mới.
  function refreshList() {
    void queryClient.invalidateQueries({ queryKey: CANNED_RESPONSES_QUERY_KEY });
  }

  function handleSaved(saved: CannedResponse) {
    refreshList();
    setFormTarget(null);
    setNotice({ kind: 'success', text: `Đã lưu mẫu /${saved.shortcut}.` });
  }

  function handleDeleted(deleted: CannedResponse) {
    refreshList();
    restoreFocusToAddRef.current = true;
    setDeleteTarget(null);
    setNotice({ kind: 'success', text: `Đã xóa mẫu /${deleted.shortcut}.` });
  }

  // Mẫu đã bị xóa ở nơi khác: hiện câu của API và đọc lại danh sách để dòng cũ biến mất.
  function handleMissing(message: string) {
    refreshList();
    restoreFocusToAddRef.current = true;
    setFormTarget(null);
    setDeleteTarget(null);
    setNotice({ kind: 'error', text: message });
  }

  function openCreateDialog() {
    setNotice(null);
    setFormTarget({ mode: 'create' });
  }

  function openEditDialog(response: CannedResponse) {
    setNotice(null);
    setFormTarget({ mode: 'edit', response });
  }

  function openDeleteDialog(response: CannedResponse) {
    setNotice(null);
    setDeleteTarget(response);
  }

  // Thiếu quyền thì không có gì để hiện hay sửa. Câu giải thích thay cho danh sách và nút thêm.
  if (!canUse) {
    return (
      <p className="canned-empty">
        Bạn cần quyền <code>conversations:reply</code> để xem và sửa mẫu trả lời.
      </p>
    );
  }

  return (
    <div className="canned-panel">
      <div className="canned-toolbar">
        <div className="canned-toolbar-text">
          <p className="canned-intro">{text.intro}</p>
          {/* Vùng trạng thái luôn có trong DOM để trình đọc màn hình đọc được thông báo thành công. */}
          <p role="status" className="canned-status">
            {notice?.kind === 'success' ? notice.text : ''}
          </p>
        </div>
        <button
          ref={addButtonRef}
          type="button"
          className="btn btn-outline"
          onClick={openCreateDialog}
        >
          Thêm mẫu
        </button>
      </div>

      {notice?.kind === 'error' && <ErrorAlert message={notice.text} />}

      {cannedQuery.isError && (
        <div className="canned-load-error">
          <ErrorAlert message={getErrorMessage(cannedQuery.error, text.loadError)} />
          <button
            type="button"
            className="btn btn-outline"
            disabled={cannedQuery.isFetching}
            onClick={() => void cannedQuery.refetch()}
          >
            Thử lại
          </button>
        </div>
      )}

      {cannedQuery.isLoading && (
        <p role="status" className="canned-empty">
          Đang tải mẫu...
        </p>
      )}

      {cannedQuery.isSuccess && visibleResponses.length === 0 && (
        <p className="canned-empty">{text.empty}</p>
      )}

      {visibleResponses.length > 0 && (
        <ul className="canned-list">
          {visibleResponses.map((response) => (
            <CannedResponseRow
              key={response.id}
              response={response}
              onEdit={() => openEditDialog(response)}
              onDelete={() => openDeleteDialog(response)}
            />
          ))}
        </ul>
      )}

      {formTarget && (
        <CannedResponseFormDialog
          target={formTarget}
          isShared={isSharedScope}
          onClose={() => setFormTarget(null)}
          onSaved={handleSaved}
          onMissing={handleMissing}
        />
      )}
      {deleteTarget && (
        <DeleteCannedResponseDialog
          response={deleteTarget}
          onClose={() => setDeleteTarget(null)}
          onDeleted={() => handleDeleted(deleteTarget)}
          onMissing={handleMissing}
        />
      )}
    </div>
  );
}

interface CannedResponseRowProps {
  response: CannedResponse;
  onEdit: () => void;
  onDelete: () => void;
}

function CannedResponseRow({ response, onEdit, onDelete }: CannedResponseRowProps) {
  return (
    <li className="canned-row">
      <div className="canned-row-main">
        <div className="canned-row-title">
          <span className="canned-shortcut">/{response.shortcut}</span>
          {/* Mẫu không sửa được chỉ để xem. Backend vẫn từ chối sửa và xóa các mẫu này. */}
          {!response.editable && <span className="badge">Chỉ xem</span>}
        </div>
        {/* CSS cắt nội dung ở hai dòng. `title` giữ nội dung đầy đủ cho người rê chuột. */}
        <p className="canned-body" title={response.body}>
          {response.body}
        </p>
      </div>
      {response.editable && (
        <div className="canned-row-actions">
          <button
            type="button"
            className="btn btn-outline btn-sm"
            aria-label={`Sửa mẫu /${response.shortcut}`}
            onClick={onEdit}
          >
            Sửa
          </button>
          <button
            type="button"
            className="btn btn-outline btn-sm btn-danger"
            aria-label={`Xóa mẫu /${response.shortcut}`}
            onClick={onDelete}
          >
            Xóa
          </button>
        </div>
      )}
    </li>
  );
}
