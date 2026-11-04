import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from '../../../shared/api';
import { getErrorMessage } from '../../../shared/error-message';
import {
  SupportEventStream,
  type EventSourceLike,
  type StreamStatus,
} from '../shared/support-event-stream';
import {
  fetchOrCreateSupportConversation,
  fetchSupportMessages,
  sendSupportMessage,
  supportCustomerEventsPath,
} from './support-customer-api';
import {
  parseSupportCustomerEvent,
  type SupportConversation,
  type SupportMessage,
  type SupportMessagePage,
  type SupportSessionStatus,
} from './support-customer-schemas';
import { mergeMessagesById } from './support-conversation-state';

// Sự kiện phía khách theo mục "Sự kiện SSE phía khách" của `tasks/support-desk/api-contract.md`.
const SUPPORT_CUSTOMER_EVENT_NAMES = [
  'message.created',
  'session.waiting',
  'session.assigned',
  'session.closed',
] as const;

// Lỗi này đồng nghĩa hội thoại hiện tại không còn dùng được. Hook boot lại để lấy hội thoại đang mở.
const CONVERSATION_GONE_CODES = ['SUPPORT_CONVERSATION_CLOSED', 'SUPPORT_CONVERSATION_NOT_FOUND'];

const LOAD_ERROR_FALLBACK = 'Không tải được hội thoại. Hãy thử lại.';
const SEND_ERROR_FALLBACK = 'Chưa gửi được tin. Hãy thử lại.';

export interface UseSupportConversationOptions {
  // Test thay bằng `FakeEventSource` để quyết định thời điểm luồng mở, lỗi hay nhận sự kiện.
  createEventSource?: (url: string) => EventSourceLike;
}

export interface SupportConversationController {
  conversation: SupportConversation | null;
  messages: SupportMessage[];
  sessionStatus: SupportSessionStatus | null;
  streamStatus: StreamStatus;
  // Chỉ đúng khi hook tạo được luồng sự kiện. Trình duyệt thiếu `EventSource` thì không báo mất kết nối.
  hasEventStream: boolean;
  // Chỉ đúng ở lần tải đầu và lúc boot lại. Lần đọc lại sau mỗi lần luồng mở không bật trạng thái này.
  isLoading: boolean;
  loadError: string | null;
  sendError: string | null;
  isSending: boolean;
  // Trả `true` khi máy chủ nhận tin, để ô soạn dọn nội dung. Lỗi trả `false` và giữ nội dung ô soạn.
  send(body: string): Promise<boolean>;
  retryLoad(): void;
}

// Nạp hội thoại, lịch sử tin và giữ luồng SSE của trang `/support`.
// Mỗi lần luồng mở, kể cả lần mở lại, hàm đọc lại hội thoại và lịch sử theo hợp đồng của SD-16.
export function useSupportConversation(
  options: UseSupportConversationOptions = {},
): SupportConversationController {
  const [conversation, setConversation] = useState<SupportConversation | null>(null);
  const [messages, setMessages] = useState<SupportMessage[]>([]);
  const [sessionStatus, setSessionStatus] = useState<SupportSessionStatus | null>(null);
  const [streamStatus, setStreamStatus] = useState<StreamStatus>(() =>
    typeof EventSource === 'undefined' ? 'lost' : 'connecting',
  );
  const [hasEventStream, setHasEventStream] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [sendError, setSendError] = useState<string | null>(null);
  const [isSending, setIsSending] = useState(false);
  const [loadAttempt, setLoadAttempt] = useState(0);

  // Giữ hội thoại mới nhất ngoài state để `send` và `reloadConversation` không cần dependency của effect.
  const conversationRef = useRef<SupportConversation | null>(null);
  const createEventSourceRef = useRef(options.createEventSource);
  createEventSourceRef.current = options.createEventSource;
  // StrictMode chạy effect hai lần trên cùng instance. Hai lần chạy chia sẻ một lần boot qua ref này.
  const bootRef = useRef<{ attempt: number; promise: Promise<SupportEventStream | null> } | null>(
    null,
  );
  const stopStreamTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Mỗi sự kiện `session.*` tăng bộ đếm. Lần đọc lại chỉ đặt trạng thái khi không có sự kiện mới hơn.
  const sessionEventCounterRef = useRef(0);
  // Tăng mỗi lần boot lại. Tác vụ của thế hệ cũ thấy số khác thì bỏ kết quả, không ghi đè dữ liệu mới.
  const runGenerationRef = useRef(0);

  useEffect(() => {
    const previousBoot = bootRef.current;
    if (previousBoot !== null && previousBoot.attempt !== loadAttempt) {
      // Lần chạy mới do retry hoặc đổi hội thoại. Dừng luồng của lần chạy cũ ngay, không chờ hẹn giờ.
      void previousBoot.promise.then((createdStream) => createdStream?.stop());
    }
    // StrictMode chạy lại effect ngay sau cleanup trên cùng instance. Hủy lần dừng luồng đã hẹn của cleanup.
    if (stopStreamTimerRef.current !== null) {
      clearTimeout(stopStreamTimerRef.current);
      stopStreamTimerRef.current = null;
    }

    // Đọc lại hội thoại và lịch sử. Gộp tin theo `id` nên đọc lại không làm nhân đôi tin đã hiện.
    async function reloadConversation(): Promise<void> {
      const generation = runGenerationRef.current;
      // Ghi số sự kiện phiên trước khi gọi POST. Sự kiện đến trong lúc chờ POST làm dữ liệu POST thành cũ.
      const eventCountAtRead = sessionEventCounterRef.current;
      try {
        const nextConversation = await fetchOrCreateSupportConversation();
        if (runGenerationRef.current !== generation) return;
        const currentId = conversationRef.current?.id;
        if (currentId !== undefined && nextConversation.id !== currentId) {
          // Hội thoại đổi, ví dụ hội thoại cũ đã đóng. Boot lại để dừng luồng cũ và xóa tin của hội thoại cũ.
          restart();
          return;
        }
        const page = await fetchSupportMessages(nextConversation.id);
        if (runGenerationRef.current !== generation) return;
        conversationRef.current = nextConversation;
        setConversation(nextConversation);
        setMessages((current) => mergeMessagesById(current, page.items));
        const nextSession = nextConversation.session;
        // Máy chủ chưa trả `session` thì giữ trạng thái đang hiện. Có sự kiện phiên mới hơn thì cũng giữ.
        if (nextSession !== null && eventCountAtRead === sessionEventCounterRef.current) {
          setSessionStatus(nextSession.status);
        }
      } catch {
        // Lần đọc lại sau khi luồng mở lại: giữ dữ liệu đang hiện, không xóa khung chat vì một lỗi mạng.
      }
    }

    function restart(): void {
      runGenerationRef.current += 1;
      // Hẹn giờ dừng luồng của cleanup lần chạy cũ không còn dùng, vì lần chạy mới dừng luồng ngay bên dưới.
      clearTimeout(stopStreamTimerRef.current ?? undefined);
      stopStreamTimerRef.current = null;
      const runningBoot = bootRef.current;
      // Dừng luồng của lần boot đang chạy, rồi xóa dữ liệu cũ trước khi boot lại.
      if (runningBoot) void runningBoot.promise.then((createdStream) => createdStream?.stop());
      conversationRef.current = null;
      setConversation(null);
      setMessages([]);
      setSessionStatus(null);
      setLoadAttempt((attempt) => attempt + 1);
    }

    // Lần đầu của khách vãng lai có thể nhận 404: các request đầu trang đua set cookie `pk_anon`
    // khác nhau nên POST và GET dùng hai chủ. Đọc lại POST và GET đúng một lần, lúc cookie đã ổn định.
    async function loadConversationAndHistory(
      firstConversation: SupportConversation,
    ): Promise<{ conversation: SupportConversation; page: SupportMessagePage }> {
      try {
        const page = await fetchSupportMessages(firstConversation.id);
        return { conversation: firstConversation, page };
      } catch (error) {
        const isOwnerMismatch =
          error instanceof ApiError &&
          error.code === 'SUPPORT_CONVERSATION_NOT_FOUND' &&
          conversationRef.current === null;
        if (!isOwnerMismatch) throw error;
        const refreshed = await fetchOrCreateSupportConversation();
        const page = await fetchSupportMessages(refreshed.id);
        return { conversation: refreshed, page };
      }
    }

    async function boot(): Promise<SupportEventStream | null> {
      const generation = runGenerationRef.current;
      setIsLoading(true);
      setLoadError(null);
      try {
        const firstConversation = await fetchOrCreateSupportConversation();
        const { conversation: loadedConversation, page } =
          await loadConversationAndHistory(firstConversation);
        if (runGenerationRef.current !== generation) return null;
        conversationRef.current = loadedConversation;
        setConversation(loadedConversation);
        setMessages(page.items);
        setSessionStatus(loadedConversation.session ? loadedConversation.session.status : null);
        setIsLoading(false);

        // Trình duyệt quá cũ không có `EventSource` thì không nối luồng được. Trang vẫn hiện dữ liệu đã tải.
        if (typeof EventSource === 'undefined' && createEventSourceRef.current === undefined) {
          return null;
        }

        const createdStream = new SupportEventStream(
          {
            url: supportCustomerEventsPath(loadedConversation.id),
            eventNames: SUPPORT_CUSTOMER_EVENT_NAMES,
            createEventSource: createEventSourceRef.current,
          },
          {
            onStatusChange: setStreamStatus,
            // Sự kiện phát lúc mất kết nối không được gửi lại. Mở lại luồng phải đọc lại mọi thứ.
            // Lần mở đầu cũng chạy, để lấp khoảng hở giữa lúc boot tải xong và lúc luồng mở.
            onOpen: () => {
              void reloadConversation();
            },
            onEvent: (name, data) => {
              const event = parseSupportCustomerEvent(name, data);
              // Sự kiện tên lạ hoặc dữ liệu hỏng bị bỏ qua.
              // Nhờ vậy sự kiện mới của backend không làm vỡ trang cũ.
              if (!event) return;
              if (event.kind === 'message') {
                setMessages((current) => mergeMessagesById(current, [event.message]));
              } else {
                sessionEventCounterRef.current += 1;
                setSessionStatus(event.status);
              }
            },
          },
        );
        createdStream.start();
        setHasEventStream(true);
        return createdStream;
      } catch (error) {
        if (runGenerationRef.current !== generation) return null;
        setLoadError(getErrorMessage(error, LOAD_ERROR_FALLBACK));
        setIsLoading(false);
        return null;
      }
    }

    // Hai POST đua nhau khi chưa có cookie `pk_anon` tạo hai hội thoại cho hai anonymous id.
    // Chia sẻ boot giữa hai lần chạy effect giữ đúng một POST và đúng một luồng sự kiện.
    const existingBoot = bootRef.current;
    const bootPromise =
      existingBoot !== null && existingBoot.attempt === loadAttempt
        ? existingBoot.promise
        : (bootRef.current = { attempt: loadAttempt, promise: boot() }).promise;

    return () => {
      conversationRef.current = null;
      // Hoãn việc dừng luồng một lượt hẹn giờ. StrictMode chạy lại effect ngay sau cleanup,
      // nên lần chạy lại hủy hẹn giờ này trước khi nó kịp dừng luồng còn dùng.
      stopStreamTimerRef.current = setTimeout(() => {
        void bootPromise.then((createdStream) => createdStream?.stop());
      }, 0);
    };
  }, [loadAttempt]);

  const send = useCallback(async (body: string): Promise<boolean> => {
    const currentConversation = conversationRef.current;
    if (!currentConversation) return false;
    const generation = runGenerationRef.current;
    setIsSending(true);
    setSendError(null);
    try {
      const message = await sendSupportMessage(currentConversation.id, body);
      setIsSending(false);
      // Lần boot lại trong lúc chờ đã đổi dữ liệu. Bỏ kết quả của thế hệ cũ.
      if (runGenerationRef.current !== generation) return false;
      setMessages((current) => mergeMessagesById(current, [message]));
      return true;
    } catch (error) {
      setIsSending(false);
      // Gửi lỗi thì giữ nguyên nội dung ô soạn, chỉ hiện câu lỗi của API để khách sửa rồi gửi lại.
      setSendError(getErrorMessage(error, SEND_ERROR_FALLBACK));
      if (
        error instanceof ApiError &&
        CONVERSATION_GONE_CODES.includes(error.code) &&
        runGenerationRef.current === generation
      ) {
        // Hội thoại đã đóng hoặc không còn của khách. Tăng thế hệ để bỏ kết quả của tác vụ cũ.
        runGenerationRef.current += 1;
        conversationRef.current = null;
        setLoadAttempt((attempt) => attempt + 1);
      }
      return false;
    }
  }, []);

  const retryLoad = useCallback(() => {
    // Bật lại effect từ đầu. Chỉ dùng khi lần tải đầu lỗi, lúc luồng SSE chưa được tạo.
    setLoadAttempt((attempt) => attempt + 1);
  }, []);

  return {
    conversation,
    messages,
    sessionStatus,
    streamStatus,
    hasEventStream,
    isLoading,
    loadError,
    sendError,
    isSending,
    send,
    retryLoad,
  };
}
