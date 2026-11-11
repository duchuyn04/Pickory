// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatbotWidgetLoader } from '../../../src/shared/chatbot-widget-loader';

const apiRequestMock = vi.hoisted(() => vi.fn());
vi.mock('../../../src/shared/api', () => ({ apiRequest: apiRequestMock }));

const SCRIPT_URL = 'https://chatbot.example.test/widget.js';
const API_URL = 'https://chatbot.example.test';

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  apiRequestMock.mockReset();
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  document.querySelectorAll('script[data-pickory-chatbot]').forEach((script) => script.remove());
  document.getElementById('rag-chatbot-host')?.remove();
  vi.unstubAllGlobals();
});

// Dựng loader rồi chờ lượt `setTimeout` và lời gọi API hoàn tất.
async function renderLoader(element = <ChatbotWidgetLoader />) {
  await act(async () => root.render(element));
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 10));
  });
}

function chatbotScripts(): HTMLScriptElement[] {
  return [...document.querySelectorAll<HTMLScriptElement>('script[data-pickory-chatbot]')];
}

describe('ChatbotWidgetLoader', () => {
  it('route trả enabled false thì không chèn script nào', async () => {
    apiRequestMock.mockResolvedValue({ enabled: false, scriptUrl: null, apiUrl: null });

    await renderLoader();

    expect(apiRequestMock).toHaveBeenCalledWith('/api/chatbot-widget');
    expect(chatbotScripts()).toHaveLength(0);
  });

  it('enabled false thì không chèn script, kể cả khi route còn trả địa chỉ', async () => {
    apiRequestMock.mockResolvedValue({ enabled: false, scriptUrl: SCRIPT_URL, apiUrl: API_URL });

    await renderLoader();

    expect(chatbotScripts()).toHaveLength(0);
  });

  it('route lỗi thì không chèn script và không ném lỗi', async () => {
    apiRequestMock.mockRejectedValue(new Error('Máy chủ lỗi'));

    await renderLoader();

    expect(chatbotScripts()).toHaveLength(0);
  });

  it('route trả enabled true thì chèn đúng một thẻ script async kèm data-api', async () => {
    apiRequestMock.mockResolvedValue({ enabled: true, scriptUrl: SCRIPT_URL, apiUrl: API_URL });

    await renderLoader();

    const [script] = chatbotScripts();
    expect(chatbotScripts()).toHaveLength(1);
    expect(script?.src).toBe(SCRIPT_URL);
    expect(script?.async).toBe(true);
    expect(script?.getAttribute('data-api')).toBe(API_URL);
  });

  it('hai loader cùng chạy (chuyển giữa các trang khách) vẫn chỉ có một thẻ script', async () => {
    apiRequestMock.mockResolvedValue({ enabled: true, scriptUrl: SCRIPT_URL, apiUrl: API_URL });

    await renderLoader(
      <>
        <ChatbotWidgetLoader />
        <ChatbotWidgetLoader />
      </>,
    );

    expect(chatbotScripts()).toHaveLength(1);
  });

  it('không gọi API ngay lúc dựng, để widget không làm chậm lần hiện đầu tiên', async () => {
    apiRequestMock.mockResolvedValue({ enabled: false, scriptUrl: null, apiUrl: null });

    // Đồng hồ giả giữ lượt `setTimeout` lại, nên thấy được trạng thái ngay sau lúc dựng.
    vi.useFakeTimers();
    try {
      act(() => root.render(<ChatbotWidgetLoader />));
      expect(apiRequestMock).not.toHaveBeenCalled();

      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(apiRequestMock).toHaveBeenCalledTimes(1);
    } finally {
      vi.useRealTimers();
    }
  });

  it('trang chưa tải xong thì chờ sự kiện load mới gọi API', async () => {
    apiRequestMock.mockResolvedValue({ enabled: false, scriptUrl: null, apiUrl: null });
    vi.spyOn(document, 'readyState', 'get').mockReturnValue('loading');

    await renderLoader();
    expect(apiRequestMock).not.toHaveBeenCalled();

    await act(async () => {
      window.dispatchEvent(new Event('load'));
      await new Promise((resolve) => setTimeout(resolve, 10));
    });
    expect(apiRequestMock).toHaveBeenCalledTimes(1);

    vi.restoreAllMocks();
  });

  it('rời layout khách thì gỡ thẻ script và khung chat khỏi trang', async () => {
    apiRequestMock.mockResolvedValue({ enabled: true, scriptUrl: SCRIPT_URL, apiUrl: API_URL });
    await renderLoader();
    // Giả lập khung chat mà widget.js gắn vào body.
    const widgetHost = document.createElement('div');
    widgetHost.id = 'rag-chatbot-host';
    document.body.append(widgetHost);

    await act(async () => root.render(<></>));

    expect(chatbotScripts()).toHaveLength(0);
    expect(document.getElementById('rag-chatbot-host')).toBeNull();
  });

  it('layout bị gỡ trước khi API trả lời thì không chèn script', async () => {
    let resolveRequest: (value: unknown) => void = () => {};
    apiRequestMock.mockReturnValue(
      new Promise((resolve) => {
        resolveRequest = resolve;
      }),
    );
    await renderLoader();
    await act(async () => root.render(<></>));

    await act(async () => {
      resolveRequest({ enabled: true, scriptUrl: SCRIPT_URL, apiUrl: API_URL });
    });

    expect(chatbotScripts()).toHaveLength(0);
  });

  it('script tải lỗi thì gỡ thẻ hỏng và trang không báo lỗi', async () => {
    apiRequestMock.mockResolvedValue({ enabled: true, scriptUrl: SCRIPT_URL, apiUrl: API_URL });
    await renderLoader();

    await act(async () => {
      chatbotScripts()[0]?.dispatchEvent(new Event('error'));
    });

    expect(chatbotScripts()).toHaveLength(0);
  });
});
