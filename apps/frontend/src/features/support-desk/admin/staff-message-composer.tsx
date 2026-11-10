import { useQuery } from '@tanstack/react-query';
import { useEffect, useId, useLayoutEffect, useRef, useState, type KeyboardEvent } from 'react';
import { FieldError, fieldErrorProps } from '../../../shared/field-error';
import { Icon } from '../../../shared/icons';
import { SUPPORT_MESSAGE_MAX_LENGTH } from '../customer/support-message-composer';
import { MyCannedResponsesDialog } from './my-canned-responses-dialog';
import { renderCannedResponse, type CannedResponseVariables } from './render-canned-response';
import { CANNED_RESPONSES_QUERY_KEY, fetchCannedResponses } from './support-desk-api';

// Bộ đếm chỉ hiện khi nội dung gần chạm giới hạn, để không làm nhiễu tin ngắn.
const COUNTER_FROM_LENGTH = 1800;
const MESSAGE_LIMIT_LABEL = SUPPORT_MESSAGE_MAX_LENGTH.toLocaleString('vi-VN');
const TOO_LONG_MESSAGE = `Tin nhắn tối đa ${MESSAGE_LIMIT_LABEL} ký tự.`;

interface StaffMessageComposerProps {
  // `false` khi nhân viên chưa nhận phiên hoặc phiên đã đóng. Ô soạn tắt nhưng vẫn hiện.
  isHeld: boolean;
  canReply: boolean;
  // Chú thích hiện dưới ô soạn khi ô tắt. `null` khi ô đang bật.
  disabledNote: string | null;
  isSending: boolean;
  // Giá trị thay cho biến của mẫu. Trang truyền giá trị thật của khách và đơn.
  variables: CannedResponseVariables;
  // Trả `true` khi máy chủ nhận tin. Ô soạn chỉ dọn nội dung khi gửi thành công.
  onSend: (body: string) => Promise<boolean>;
}

// Ô soạn tin của nhân viên: Enter gửi, Shift+Enter xuống dòng, gõ `/` mở danh sách mẫu trả lời nhanh.
export function StaffMessageComposer({
  isHeld,
  canReply,
  disabledNote,
  isSending,
  variables,
  onSend,
}: StaffMessageComposerProps) {
  const composerId = useId();
  const inputId = `${composerId}-input`;
  const listboxId = `${composerId}-listbox`;
  const [body, setBody] = useState('');
  const [activeIndex, setActiveIndex] = useState(0);
  // Esc đóng danh sách cho tới khi nội dung ô đổi. Là state để đổi giao diện ngay khi bấm Esc.
  const [dismissed, setDismissed] = useState(false);
  const cursorToEndRef = useRef(false);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const manageButtonRef = useRef<HTMLButtonElement>(null);
  const [isManagerOpen, setIsManagerOpen] = useState(false);

  function closeManager() {
    setIsManagerOpen(false);
    // Hộp thoại đóng xong thì trả focus về nút đã mở nó, để bàn phím tiếp tục từ chỗ cũ.
    manageButtonRef.current?.focus();
  }

  const isEnabled = isHeld && canReply;
  // Chỉ đọc mẫu khi ô soạn đang bật, để người thiếu quyền không gọi route này.
  const cannedQuery = useQuery({
    queryKey: CANNED_RESPONSES_QUERY_KEY,
    queryFn: fetchCannedResponses,
    enabled: isEnabled,
    retry: false,
  });

  const trimmedBody = body.trim();
  // Hợp đồng tính độ dài sau khi bỏ khoảng trắng hai đầu, nên dấu cách cuối không bị phạt.
  const isTooLong = trimmedBody.length > SUPPORT_MESSAGE_MAX_LENGTH;
  const canSend = isEnabled && trimmedBody.length > 0 && !isTooLong && !isSending;

  // Danh sách mẫu chỉ mở khi ô soạn đang bật và toàn bộ ô là `/` cộng một từ không khoảng trắng.
  // Ô tắt (ví dụ gửi bị 403) thì không vẽ listbox, để query tắt không treo ở trạng thái "Đang nạp mẫu...".
  const triggerMatch = isEnabled ? /^\/(\S*)$/.exec(body) : null;
  const listOpen = triggerMatch !== null && !dismissed;
  const filterWord = triggerMatch?.[1]?.toLowerCase() ?? '';
  // Lọc theo đầu từ của shortcut, không phân biệt hoa thường, theo quyết định của SD-UI2.
  const filtered = listOpen
    ? (cannedQuery.data ?? []).filter((response) =>
        response.shortcut.toLowerCase().startsWith(filterWord),
      )
    : [];
  // Mục đang chọn không vượt quá danh sách đã lọc, để danh sách co lại khi lọc chặt hơn.
  const clampedActive = Math.min(activeIndex, Math.max(filtered.length - 1, 0));

  // Nội dung đổi thì mở lại danh sách đã đóng bằng Esc và đưa mục chọn về đầu.
  useEffect(() => {
    setDismissed(false);
    setActiveIndex(0);
  }, [body]);

  // Tự cao theo nội dung tới giới hạn của CSS, dùng `useLayoutEffect` để không nháy chiều cao.
  useLayoutEffect(() => {
    const textarea = textareaRef.current;
    if (!textarea) return;
    textarea.style.height = 'auto';
    // `scrollHeight` không gồm viền, mà `box-sizing: border-box` tính viền vào chiều cao.
    const borderHeight = textarea.offsetHeight - textarea.clientHeight;
    textarea.style.height = `${textarea.scrollHeight + borderHeight}px`;
  }, [body]);

  // Sau khi chèn mẫu, đặt con trỏ ở cuối ô rồi trả focus cho ô soạn.
  useLayoutEffect(() => {
    if (!cursorToEndRef.current) return;
    cursorToEndRef.current = false;
    const textarea = textareaRef.current;
    if (!textarea) return;
    textarea.focus();
    textarea.setSelectionRange(textarea.value.length, textarea.value.length);
  }, [body]);

  // Cuộn mục đang chọn vào khung nhìn khi mũi tên đi qua danh sách dài hơn khung.
  useEffect(() => {
    if (!listOpen || filtered.length === 0) return;
    const active = document.getElementById(`${listboxId}-option-${clampedActive}`);
    // jsdom không có `scrollIntoView`, nên kiểm tra trước khi gọi để test không vỡ.
    if (active && typeof active.scrollIntoView === 'function') {
      active.scrollIntoView({ block: 'nearest' });
    }
  }, [clampedActive, listOpen, filtered.length, listboxId]);

  async function handleSend() {
    if (!canSend) return;
    const sendingBody = trimmedBody;
    const sent = await onSend(sendingBody);
    // Gửi thành công mới dọn ô. Gửi lỗi giữ nguyên nội dung để nhân viên sửa rồi gửi lại.
    if (sent) setBody((current) => (current.trim() === sendingBody ? '' : current));
    // Nút gửi tắt trong lúc gửi nên trình duyệt bỏ focus. Trả focus về ô soạn để gõ tiếp.
    textareaRef.current?.focus();
  }

  function insertCannedAt(index: number) {
    const selected = filtered[index];
    if (!selected) return;
    // Thay toàn bộ ô bằng nội dung mẫu đã thay biến. Không tự gửi tin.
    setBody(renderCannedResponse(selected.body, variables));
    setDismissed(true);
    cursorToEndRef.current = true;
  }

  function handleKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    // Bộ gõ tiếng Việt dùng composition. Enter để chốt chữ không được gửi tin hay chèn mẫu.
    if (event.nativeEvent.isComposing || event.keyCode === 229) return;
    if (listOpen) {
      if (event.key === 'ArrowDown') {
        event.preventDefault();
        setActiveIndex((index) => Math.min(index + 1, Math.max(filtered.length - 1, 0)));
        return;
      }
      if (event.key === 'ArrowUp') {
        event.preventDefault();
        setActiveIndex((index) => Math.max(index - 1, 0));
        return;
      }
      if (event.key === 'Enter') {
        // Danh sách đang mở thì Enter chèn mẫu đang chọn, không gửi tin.
        event.preventDefault();
        insertCannedAt(clampedActive);
        return;
      }
      if (event.key === 'Escape') {
        event.preventDefault();
        setDismissed(true);
        return;
      }
    }
    // Enter không kèm Shift là gửi. Chặn xuống dòng mặc định của textarea để không gửi dòng trống.
    if (event.key !== 'Enter' || event.shiftKey) return;
    event.preventDefault();
    void handleSend();
  }

  const showCounter = trimmedBody.length > COUNTER_FROM_LENGTH;
  const activeDescendant =
    listOpen && filtered.length > 0 ? `${listboxId}-option-${clampedActive}` : undefined;

  return (
    <div className="support-chat-composer">
      <div className="support-chat-composer-bar">
        <div className="field support-chat-composer-field">
          <label htmlFor={inputId} className="sr">
            Nội dung tin nhắn
          </label>
          <textarea
            ref={textareaRef}
            id={inputId}
            rows={1}
            role="combobox"
            aria-expanded={listOpen}
            aria-controls={listboxId}
            aria-haspopup="listbox"
            aria-autocomplete="list"
            aria-activedescendant={activeDescendant}
            placeholder="Nhập tin nhắn..."
            value={body}
            disabled={!isEnabled}
            onChange={(event) => setBody(event.target.value)}
            onKeyDown={handleKeyDown}
            {...fieldErrorProps(inputId, isTooLong ? TOO_LONG_MESSAGE : null)}
          />
        </div>
        <button
          type="button"
          className="btn btn-primary support-chat-composer-send"
          disabled={!canSend}
          onClick={() => void handleSend()}
        >
          <Icon name="send" className="icon" />
          {isSending ? 'Đang gửi...' : 'Gửi'}
        </button>
      </div>
      {listOpen && (
        <ul id={listboxId} role="listbox" className="support-canned-list">
          {cannedQuery.isError ? (
            <li className="support-canned-empty">Không tải được mẫu.</li>
          ) : cannedQuery.isPending ? (
            <li className="support-canned-empty">Đang nạp mẫu...</li>
          ) : filtered.length === 0 ? (
            <li className="support-canned-empty">Không có mẫu nào khớp.</li>
          ) : (
            filtered.map((response, index) => (
              <li
                key={response.id}
                id={`${listboxId}-option-${index}`}
                role="option"
                aria-selected={index === clampedActive}
                className={
                  index === clampedActive ? 'support-canned-item is-active' : 'support-canned-item'
                }
                // `mousedown` chặn mất focus cho mọi nút chuột. Chỉ chèn mẫu khi bấm chuột trái (`event.button === 0`).
                onMouseDown={(event) => {
                  event.preventDefault();
                  if (event.button !== 0) return;
                  insertCannedAt(index);
                }}
              >
                <span className="support-canned-shortcut">/{response.shortcut}</span>
                <span className="support-canned-body wrap-anywhere">{response.body}</span>
                {response.shared && <span className="badge">Dùng chung</span>}
              </li>
            ))
          )}
        </ul>
      )}
      <FieldError inputId={inputId} message={isTooLong ? TOO_LONG_MESSAGE : null} />
      <div className="support-chat-composer-meta">
        {disabledNote !== null ? (
          <p className="hint support-chat-composer-hint">{disabledNote}</p>
        ) : (
          <p className="hint support-chat-composer-hint">Enter để gửi, Shift+Enter để xuống dòng</p>
        )}
        <div className="support-chat-composer-meta-actions">
          {showCounter && (
            <span className="support-chat-composer-counter">
              {trimmedBody.length.toLocaleString('vi-VN')}/{MESSAGE_LIMIT_LABEL}
            </span>
          )}
          {/* Người thiếu quyền trả lời không thấy nút, vì backend từ chối mọi thay đổi mẫu của họ. */}
          {canReply && (
            <button
              ref={manageButtonRef}
              type="button"
              className="link-btn"
              onClick={() => setIsManagerOpen(true)}
            >
              Quản lý mẫu
            </button>
          )}
        </div>
      </div>
      {isManagerOpen && <MyCannedResponsesDialog onClose={closeManager} />}
    </div>
  );
}
