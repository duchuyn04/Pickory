// Lỗi trả về từ API theo mẫu chung `{ error: { code, message } }`.
export class ApiError extends Error {
  constructor(
    public readonly code: string,
    public readonly status: number,
    message: string,
    public readonly details?: { key: string; message: string }[],
  ) {
    super(message);
  }
}

// Dạng response của API: thành công `{ data }`, lỗi `{ error }`.
type ApiResponse<T> =
  | { data: T }
  | { error: { code: string; message: string; details?: { key: string; message: string }[] } };

/**
 * Gọi API và trả về phần `data`. Lỗi mạng hoặc lỗi từ API đều thành `ApiError`
 * để giao diện hiển thị `message` tiếng Việt mà không phải tự phân tích response.
 */
export async function apiRequest<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(path, {
      ...init,
      credentials: 'include', // gửi kèm cookie phiên
      headers: { 'Content-Type': 'application/json', ...init?.headers },
    });
  } catch {
    throw new ApiError('NETWORK_ERROR', 0, 'Không kết nối được máy chủ.');
  }

  // `DELETE /api/admin/products/{id}` trả 204 không có thân. Phản hồi này là thành công, không phải phản hồi hỏng.
  if (response.status === 204) return undefined as T;

  const body = (await response.json().catch(() => null)) as ApiResponse<T> | null;
  if (body && 'data' in body) return body.data;
  if (body && 'error' in body) {
    throw new ApiError(body.error.code, response.status, body.error.message, body.error.details);
  }
  throw new ApiError('UNKNOWN_ERROR', response.status, 'Phản hồi từ máy chủ không hợp lệ.');
}
