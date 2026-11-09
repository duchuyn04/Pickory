import {
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type FocusEvent,
  type KeyboardEvent,
} from 'react';
import { FieldError, fieldErrorProps } from '../../../shared/field-error';
import { Icon } from '../../../shared/icons';

// Hợp đồng cho phép tin dài tối đa 2.000 ký tự sau khi bỏ khoảng trắng hai đầu.
// Kiểm ở ô soạn để không gọi API chắc chắn lỗi.
export const SUPPORT_MESSAGE_MAX_LENGTH = 2000;
// Bộ đếm chỉ hiện khi khách gần chạm giới hạn, để không làm nhiễu tin ngắn.
const COUNTER_FROM_LENGTH = 1800;
const MESSAGE_INPUT_ID = 'support-message-body';
const MESSAGE_LIMIT_LABEL = SUPPORT_MESSAGE_MAX_LENGTH.toLocaleString('vi-VN');
const TOO_LONG_MESSAGE = `Tin nhắn tối đa ${MESSAGE_LIMIT_LABEL} ký tự.`;

interface SupportMessageComposerProps {
  isSending: boolean;
  // `false` khi hội thoại chưa tải xong. Ô soạn tắt nhưng vẫn hiện để khung chat không nhảy bố cục.
  isReady?: boolean;
  // Trả `true` khi máy chủ nhận tin. Ô soạn chỉ dọn nội dung khi gửi thành công.
  onSend: (body: string) => Promise<boolean>;
}

// Ô soạn tin của khách: Enter gửi, Shift+Enter xuống dòng, nút gửi tắt khi không gửi được.
export function SupportMessageComposer({
  isSending,
  isReady = true,
  onSend,
}: SupportMessageComposerProps) {
  const [body, setBody] = useState('');
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  // Nhớ ô soạn từng giữ focus trước khi bị tắt, để trả focus khi bật lại sau lần tải lại.
  const keepFocusRef = useRef(false);
  const trimmedBody = body.trim();
  // Hợp đồng tính độ dài sau khi bỏ khoảng trắng hai đầu, nên dấu xuống dòng hay dấu cách cuối không bị phạt.
  const isTooLong = trimmedBody.length > SUPPORT_MESSAGE_MAX_LENGTH;
  const canSend = isReady && trimmedBody.length > 0 && !isTooLong && !isSending;

  // Tự cao theo nội dung tới giới hạn của CSS, dùng `useLayoutEffect` để không nháy chiều cao.
  useLayoutEffect(() => {
    const textarea = textareaRef.current;
    if (!textarea) return;
    textarea.style.height = 'auto';
    // `scrollHeight` không gồm viền, mà `box-sizing: border-box` tính viền vào chiều cao.
    // Cộng phần viền để vùng chữ không hụt 2 px và không sinh thanh cuộn sớm.
    const borderHeight = textarea.offsetHeight - textarea.clientHeight;
    textarea.style.height = `${textarea.scrollHeight + borderHeight}px`;
  }, [body]);

  // Trả lại focus cho ô soạn sau khi tải lại xong. Ô soạn bị tắt trong lúc tải lại làm mất vị trí nhập.
  useEffect(() => {
    if (!isReady || !keepFocusRef.current) return;
    textareaRef.current?.focus();
    keepFocusRef.current = false;
  }, [isReady]);

  function handleComposerFocus() {
    keepFocusRef.current = true;
  }

  function handleComposerBlur(event: FocusEvent<HTMLElement>) {
    // Còn focus trong cụm soạn thì giữ cờ. Chuyển sang phần tử khác thì nhả cờ.
    if (event.relatedTarget !== null) keepFocusRef.current = false;
  }

  async function handleSend() {
    if (!canSend) return;
    const sendingBody = trimmedBody;
    const sent = await onSend(sendingBody);
    // Gửi thành công mới dọn ô. Gửi lỗi giữ nguyên nội dung để khách sửa rồi gửi lại.
    // Khách gõ tin mới trong lúc chờ thì giữ bản mới, không xóa mất chữ vừa nhập.
    if (sent) setBody((current) => (current.trim() === sendingBody ? '' : current));
    // Nút "Gửi" bị tắt trong lúc gửi nên trình duyệt bỏ focus. Trả focus về ô soạn để khách gõ tiếp.
    textareaRef.current?.focus();
  }

  function handleKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    // Bộ gõ tiếng Việt dùng composition. Enter để chốt chữ không được gửi tin.
    if (event.nativeEvent.isComposing || event.keyCode === 229) return;
    // Enter không kèm Shift là gửi. Chặn xuống dòng mặc định của textarea để trang không nhận dòng trống.
    if (event.key !== 'Enter' || event.shiftKey) return;
    event.preventDefault();
    void handleSend();
  }

  const showCounter = trimmedBody.length > COUNTER_FROM_LENGTH;

  return (
    <div className="support-composer" onFocus={handleComposerFocus} onBlur={handleComposerBlur}>
      <div className="support-composer-bar">
        <div className="field support-composer-field">
          <label htmlFor={MESSAGE_INPUT_ID} className="sr">
            Nội dung tin nhắn
          </label>
          <textarea
            ref={textareaRef}
            id={MESSAGE_INPUT_ID}
            rows={1}
            placeholder="Nhập tin nhắn..."
            value={body}
            disabled={!isReady}
            onChange={(event) => setBody(event.target.value)}
            onKeyDown={handleKeyDown}
            {...fieldErrorProps(MESSAGE_INPUT_ID, isTooLong ? TOO_LONG_MESSAGE : null)}
          />
        </div>
        <button
          type="button"
          className="btn btn-primary support-composer-send"
          disabled={!canSend}
          onClick={() => void handleSend()}
        >
          <Icon name="send" className="icon" />
          {isSending ? 'Đang gửi...' : 'Gửi'}
        </button>
      </div>
      <FieldError inputId={MESSAGE_INPUT_ID} message={isTooLong ? TOO_LONG_MESSAGE : null} />
      <div className="support-composer-meta">
        <p className="hint support-composer-hint">Enter để gửi, Shift+Enter để xuống dòng</p>
        {showCounter && (
          <span className="support-composer-counter">
            {trimmedBody.length.toLocaleString('vi-VN')}/{MESSAGE_LIMIT_LABEL}
          </span>
        )}
      </div>
    </div>
  );
}
