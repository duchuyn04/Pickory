import { useQuery } from '@tanstack/react-query';
import { APP_NAME } from './app-name';
import { apiRequest } from './api';

// Trang placeholder: hiển thị trạng thái `/api/health` để xác nhận web nối được với API.
export function HealthPage() {
  const health = useQuery({
    queryKey: ['health'],
    queryFn: () => apiRequest<{ status: string }>('/api/health'),
  });

  return (
    <main>
      <h1>{APP_NAME}</h1>
      {health.isPending && <p>Đang kiểm tra API...</p>}
      {health.isError && <p>API lỗi: {health.error.message}</p>}
      {health.isSuccess && <p>API: {health.data.status}</p>}
    </main>
  );
}
