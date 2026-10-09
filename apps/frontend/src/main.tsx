// Nạp CSS dùng chung trước `App`. CSS riêng của từng màn nạp sau nên ghi đè được lớp chung cùng độ ưu tiên.
import './shared/styles/tokens.css';
import './shared/styles/base.css';
import './shared/styles/components.css';
import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { App } from './app';
import { ApiError } from './shared/api';

// Một QueryClient cho cả ứng dụng: cache và gọi lại dữ liệu từ API do TanStack Query quản lý.
// Phiên có thể hết hạn khi actor còn trong cache, nên lỗi 401 phải nạp lại actor để layout chuyển về trang đăng nhập.
function handleApiError(error: Error): void {
  if ('status' in error && error.status === 401) {
    void queryClient.invalidateQueries({ queryKey: ['actor'] });
  }
}

const queryClient = new QueryClient({
  queryCache: new QueryCache({ onError: handleApiError }),
  mutationCache: new MutationCache({ onError: handleApiError }),
  defaultOptions: {
    queries: {
      // Lỗi phía khách không tự hết khi thử lại, nhất là 401 cần cập nhật actor ngay.
      retry: (failureCount, error) =>
        !(error instanceof ApiError && error.status < 500) && failureCount < 3,
    },
  },
});

const rootElement = document.getElementById('root');
if (!rootElement) throw new Error('Thiếu phần tử #root trong index.html');

createRoot(rootElement).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
