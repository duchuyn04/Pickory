import { useEffect } from 'react';
import { apiRequest } from './api';

// Dữ liệu của `GET /api/chatbot-widget`. Hai địa chỉ là null khi `enabled` là false.
type ChatbotWidgetConfig = {
  enabled: boolean;
  scriptUrl: string | null;
  apiUrl: string | null;
};

// Dấu nhận ra thẻ script do Pickory chèn, để không chèn lần hai và để dọn đúng thẻ này.
const SCRIPT_MARKER_ATTRIBUTE = 'data-pickory-chatbot';
// Id phần tử gốc mà `widget.js` của `chatbot-rag` gắn vào `body`. Dọn phần tử này khi rời trang khách.
const WIDGET_HOST_ID = 'rag-chatbot-host';

// Hẹn chạy `task` sau khi trang khách tải xong, để widget không làm chậm lần hiện đầu tiên.
function runAfterPageLoad(task: () => void): () => void {
  let timer: number | undefined;
  const scheduleTask = () => {
    timer = window.setTimeout(task, 0);
  };

  // Trang đã tải xong thì chạy ngay ở lượt sau. Chưa xong thì chờ sự kiện `load`.
  if (document.readyState === 'complete') {
    scheduleTask();
  } else {
    window.addEventListener('load', scheduleTask, { once: true });
  }

  // Hàm hủy dùng khi layout bị gỡ trước lúc việc được chạy.
  return () => {
    window.removeEventListener('load', scheduleTask);
    window.clearTimeout(timer);
  };
}

// Chèn thẻ script của widget. Mỗi trang chỉ có một thẻ.
function insertWidgetScript(config: ChatbotWidgetConfig): void {
  // Thiếu địa chỉ thì không có gì để tải. Backend đã chặn trường hợp này, kiểm lại để kiểu dữ liệu chặt.
  if (!config.enabled || !config.scriptUrl || !config.apiUrl) return;

  // Đã có thẻ thì không chèn lần hai. Widget tự tạo khung chat mỗi lần script chạy.
  if (document.querySelector(`script[${SCRIPT_MARKER_ATTRIBUTE}]`)) return;

  const script = document.createElement('script');
  script.src = config.scriptUrl;
  // `async` để script không chặn việc dựng trang.
  script.async = true;
  // Widget đọc địa chỉ API của dịch vụ chatbot từ thuộc tính này.
  script.setAttribute('data-api', config.apiUrl);
  script.setAttribute(SCRIPT_MARKER_ATTRIBUTE, 'true');
  // Tải lỗi thì bỏ thẻ hỏng. Lần vào trang khách sau sẽ thử tải lại. Trang không bị ảnh hưởng.
  script.addEventListener('error', () => script.remove());
  document.head.append(script);
}

// Xóa thẻ script và khung chat của widget khỏi trang.
function removeWidget(): void {
  document
    .querySelectorAll(`script[${SCRIPT_MARKER_ATTRIBUTE}]`)
    .forEach((script) => script.remove());
  document.getElementById(WIDGET_HOST_ID)?.remove();
}

// Tải khung chat của `chatbot-rag` ở trang khách. Component không vẽ gì ra giao diện.
export function ChatbotWidgetLoader(): null {
  useEffect(() => {
    // Cờ hủy: layout bị gỡ giữa lúc chờ API thì không chèn script nữa.
    let cancelled = false;

    const cancelScheduledLoad = runAfterPageLoad(() => {
      apiRequest<ChatbotWidgetConfig>('/api/chatbot-widget')
        .then((config) => {
          if (!cancelled) insertWidgetScript(config);
        })
        .catch(() => {
          // Route lỗi thì trang khách chạy như không có chatbot. Khung chat là tính năng phụ, nên không hiện lỗi.
        });
    });

    return () => {
      cancelled = true;
      cancelScheduledLoad();
      // Trang quản trị dùng layout khác. Rời layout khách thì khung chat phải biến mất.
      removeWidget();
    };
  }, []);

  return null;
}
