import { useId, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { ApiError } from '../../../shared/api';
import { ErrorAlert } from '../../../shared/error-alert';
import { useDialogFocus } from '../../../shared/use-dialog-focus';
import { useActor } from '../../identity/hooks/use-actor';
import { staffRadioLabel } from './presence-state';
import {
  PRESENCE_QUERY_KEY,
  fetchAvailableStaff,
  transferSupportSession,
} from './support-desk-api';
import './transfer-dialog.css';

interface TransferSessionDialogProps {
  sessionId: string;
  onSuccess: (recipientName: string) => void;
  onSessionClosed: () => void;
  onLostHold: () => void;
  onClose: () => void;
}

const DEFAULT_TRANSFER_ERROR = 'Chưa chuyển được hội thoại. Hãy thử lại.';

// Hộp thoại chuyển phiên hỗ trợ cho đồng nghiệp đang sẵn sàng.
export function TransferSessionDialog({
  sessionId,
  onSuccess,
  onSessionClosed,
  onLostHold,
  onClose,
}: TransferSessionDialogProps) {
  const noteId = useId();
  const { data: actor } = useActor();
  const currentUserId = actor?.userId ?? null;

  const [selectedStaffId, setSelectedStaffId] = useState<string | null>(null);
  const [note, setNote] = useState('');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const isSubmittingRef = useRef(false);

  // Đọc danh sách trạng thái nhân viên từ cache presence dùng chung của màn CSKH.
  const presenceQuery = useQuery({
    queryKey: PRESENCE_QUERY_KEY,
    queryFn: fetchAvailableStaff,
  });

  // Giữ nhân viên sẵn sàng và loại chính tài khoản đang đăng nhập.
  const availableStaff = (presenceQuery.data ?? []).filter(
    (staff) => staff.status === 'available' && staff.staffId !== currentUserId,
  );

  // Chặn phím Esc và nút Hủy đóng hộp thoại khi yêu cầu đang xử lý.
  function closeUnlessSubmitting() {
    if (isSubmitting) return;
    onClose();
  }

  const dialogRef = useDialogFocus({ onClose: closeUnlessSubmitting });

  async function handleConfirm() {
    // Chặn gửi yêu cầu khi nhân viên chưa chọn người nhận trong danh sách.
    if (!selectedStaffId) return;

    // Chặn bấm đôi đồng bộ trước khi React vẽ lại trạng thái đang gửi.
    if (isSubmittingRef.current) return;

    // Chốt thông tin người nhận trước khi gọi API để tránh mất tên nếu người đó đổi trạng thái.
    const recipient = availableStaff.find((staff) => staff.staffId === selectedStaffId);
    if (!recipient) {
      // Người được chọn vừa chuyển sang bận hoặc vắng mặt, hủy lựa chọn và yêu cầu chọn lại.
      setSelectedStaffId(null);
      setErrorMessage('Đồng nghiệp này không còn sẵn sàng. Hãy chọn người khác.');
      return;
    }

    const recipientName = staffRadioLabel(recipient);
    isSubmittingRef.current = true;
    setIsSubmitting(true);
    setErrorMessage(null);

    const trimmedNote = note.trim();
    // Hợp đồng API quy định gửi null khi nội dung ghi chú để trống.
    const noteToSend = trimmedNote.length > 0 ? trimmedNote : null;

    try {
      // Chuyển quyền xử lý phiên cho đồng nghiệp để họ tiếp quản hội thoại của khách.
      await transferSupportSession(sessionId, selectedStaffId, noteToSend);
      onSuccess(recipientName);
    } catch (error) {
      if (error instanceof ApiError) {
        // Người nhận không còn sẵn sàng thì đọc lại presence và bỏ chọn nếu họ đã rời danh sách.
        if (error.code === 'SUPPORT_STAFF_UNAVAILABLE') {
          const refreshed = await presenceQuery.refetch();
          const refreshedList = refreshed.data ?? [];
          const stillAvailable = refreshedList.some(
            (staff) => staff.staffId === selectedStaffId && staff.status === 'available',
          );
          if (!stillAvailable) {
            setSelectedStaffId(null);
          }
          setErrorMessage(error.message);
          return;
        }

        // Phiên đã kết thúc thì cập nhật cờ phiên và đóng hộp thoại.
        if (error.code === 'SUPPORT_SESSION_CLOSED') {
          onSessionClosed();
          onClose();
          return;
        }

        // Mất quyền giữ phiên thì gọi callback đặt lại trạng thái và đóng hộp thoại.
        if (error.code === 'SUPPORT_SESSION_NOT_ASSIGNED') {
          onLostHold();
          onClose();
          return;
        }

        setErrorMessage(error.message);
        return;
      }

      setErrorMessage(DEFAULT_TRANSFER_ERROR);
    } finally {
      // Chỉ nhả cờ gửi sau khi yêu cầu và quá trình đọc lại dữ liệu hoàn tất.
      isSubmittingRef.current = false;
      setIsSubmitting(false);
    }
  }

  return (
    <div className="dialog-backdrop">
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="transfer-session-title"
        tabIndex={-1}
        className="dialog"
      >
        <div className="dialog-head">
          <h2 id="transfer-session-title">Chuyển hội thoại</h2>
        </div>
        <div className="dialog-body stack">
          {errorMessage && <ErrorAlert message={errorMessage} />}

          <fieldset className="support-transfer-fieldset">
            <legend className="support-transfer-legend">Chuyển cho</legend>
            {presenceQuery.isLoading ? (
              <p role="status" className="empty-state">
                Đang tải danh sách...
              </p>
            ) : presenceQuery.isError ? (
              <div className="support-queue-alert">
                <ErrorAlert message="Không tải được danh sách nhân viên." />
                <button
                  type="button"
                  className="btn btn-sm"
                  disabled={presenceQuery.isFetching}
                  onClick={() => void presenceQuery.refetch()}
                >
                  Thử lại
                </button>
              </div>
            ) : availableStaff.length === 0 ? (
              <p className="empty-state">Chưa có đồng nghiệp nào đang sẵn sàng.</p>
            ) : (
              <div className="support-transfer-staff-list">
                {availableStaff.map((staff) => {
                  const label = staffRadioLabel(staff);
                  const radioId = `transfer-staff-${staff.staffId}`;
                  return (
                    <label
                      key={staff.staffId}
                      htmlFor={radioId}
                      className="support-transfer-radio-item"
                    >
                      <input
                        id={radioId}
                        type="radio"
                        name="transferStaff"
                        value={staff.staffId}
                        checked={selectedStaffId === staff.staffId}
                        onChange={() => {
                          setSelectedStaffId(staff.staffId);
                          // Xóa thông báo lỗi khi nhân viên chọn lại một người khác.
                          setErrorMessage(null);
                        }}
                        disabled={isSubmitting}
                      />
                      <span>{label}</span>
                    </label>
                  );
                })}
              </div>
            )}
          </fieldset>

          <div className="field">
            <label htmlFor={noteId}>Ghi chú nội bộ</label>
            <textarea
              id={noteId}
              rows={3}
              maxLength={500}
              value={note}
              onChange={(e) => setNote(e.target.value)}
              disabled={isSubmitting}
              className="support-transfer-note"
            />
            <div className="support-chat-composer-meta">
              <span className="support-chat-composer-hint">Khách không đọc được ghi chú này.</span>
              <span className="support-chat-composer-counter">{note.length}/500</span>
            </div>
          </div>
        </div>

        <div className="dialog-foot">
          <button
            type="button"
            className="btn btn-outline"
            onClick={closeUnlessSubmitting}
            disabled={isSubmitting}
          >
            Hủy
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => void handleConfirm()}
            disabled={isSubmitting || selectedStaffId === null}
          >
            {isSubmitting ? 'Đang chuyển...' : 'Chuyển'}
          </button>
        </div>
      </div>
    </div>
  );
}
