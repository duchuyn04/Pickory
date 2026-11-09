import { UserFacingError } from './error-message';

type ApiErrorDetail = { key: string; message: string };

// Lỗi trả về từ API theo mẫu chung `{ error: { code, message } }`.
export class ApiError extends UserFacingError {
  constructor(
    public readonly code: string,
    public readonly status: number,
    message: string,
    public readonly details?: ApiErrorDetail[],
  ) {
    super(message);
  }
}

// Dạng response của API: thành công `{ data }`, lỗi `{ error }`.
type ApiResponse<T> =
  { data: T } | { error: { code: string; message: string; details?: ApiErrorDetail[] } };

const NETWORK_ERROR_MESSAGE = 'Không kết nối được máy chủ.';

// Backend ghép lỗi 400 thành `Dữ liệu không hợp lệ (khóa: câu; khóa: câu).`. Mẫu này tách phần trong ngoặc.
const VALIDATION_SUMMARY_PATTERN = /^Dữ liệu không hợp lệ \((.*)\)\.$/s;
// Khóa đứng đầu mỗi phần, ví dụ `items[0].quantity: `. Khóa là tên trường kỹ thuật, người dùng không cần đọc.
const FIELD_KEY_PREFIX_PATTERN = /^[\w.[\]]+:\s*/;

// Câu lỗi theo mã trạng thái, dùng khi phản hồi không có câu lỗi đọc được, ví dụ trang lỗi HTML của proxy.
function messageForStatus(status: number): string {
  if (status === 401) return 'Phiên đăng nhập đã hết hạn. Hãy đăng nhập lại.';
  if (status === 403) return 'Tài khoản không có quyền thực hiện thao tác này.';
  if (status === 404) return 'Không tìm thấy dữ liệu cần xem.';
  if (status === 413) return 'Dữ liệu gửi lên quá lớn.';
  if (status === 429) return 'Thao tác quá nhanh. Hãy chờ một lát rồi thử lại.';
  if (status >= 500) return 'Máy chủ đang gặp sự cố. Hãy thử lại sau.';
  return 'Đã có lỗi xảy ra. Hãy thử lại.';
}

// Ghép câu lỗi 400 chỉ từ câu của từng trường. Câu trùng nhau chỉ giữ một lần.
function joinDetailMessages(details: ApiErrorDetail[]): string {
  const uniqueMessages = [...new Set(details.map((detail) => detail.message.trim()))];
  return uniqueMessages.filter((message) => message.length > 0).join(' ');
}

// Bỏ tên trường kỹ thuật khỏi câu tổng của lỗi 400 khi phản hồi không có `details`.
function stripFieldKeys(message: string): string {
  const summary = VALIDATION_SUMMARY_PATTERN.exec(message)?.[1];
  // Câu không theo mẫu ghép của backend thì giữ nguyên, vì câu đó do code nghiệp vụ viết riêng.
  if (!summary) return message;
  const parts = summary.split('; ').map((part) => part.replace(FIELD_KEY_PREFIX_PATTERN, ''));
  return parts.join(' ');
}

// Chọn câu lỗi để hiện cho người dùng từ thân lỗi của API.
function toDisplayMessage(
  status: number,
  error: { code: string; message: string; details?: ApiErrorDetail[] },
): string {
  // Lỗi 400 kiểm dữ liệu: câu tổng chứa tên trường kỹ thuật. Câu của từng trường đã là tiếng Việt.
  if (error.code === 'VALIDATION_ERROR') {
    const detailMessage = error.details ? joinDetailMessages(error.details) : '';
    if (detailMessage) return detailMessage;
    return stripFieldKeys(error.message);
  }
  // Thân lỗi thiếu câu thì dùng câu theo mã trạng thái, để giao diện không hiện ô lỗi trống.
  if (!error.message || !error.message.trim()) return messageForStatus(status);
  return error.message;
}

// Promise dùng chung cho request đầu tiên của tab.
// Khởi tạo ngay khi nạp module để các luồng sự kiện luôn chờ request đầu.
let resolveInitialRequestGate: (() => void) | null = null;
const initialRequestGate = new Promise<void>((resolve) => {
  resolveInitialRequestGate = resolve;
});
let hasStartedInitialRequest = false;
let isInitialRequestSettled = false;

// Kiểm tra xem request API đầu tiên có đang chờ hoặc chưa hoàn tất hay không.
export function isInitialApiRequestPending(): boolean {
  return !isInitialRequestSettled;
}

// Chờ request API đầu tiên của tab kết thúc trước khi thực hiện kết nối tiếp theo.
export function waitForInitialApiRequest(): Promise<void> {
  return initialRequestGate;
}

async function executeRequest<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(path, {
      ...init,
      credentials: 'include', // gửi kèm cookie phiên
      headers: { 'Content-Type': 'application/json', ...init?.headers },
    });
  } catch {
    throw new ApiError('NETWORK_ERROR', 0, NETWORK_ERROR_MESSAGE);
  }

  // `DELETE /api/admin/products/{id}` trả 204 không có thân. Phản hồi này là thành công, không phải phản hồi hỏng.
  if (response.status === 204) return undefined as T;

  const body = (await response.json().catch(() => null)) as ApiResponse<T> | null;
  if (body && 'data' in body) return body.data;
  if (body && 'error' in body) {
    const displayMessage = toDisplayMessage(response.status, body.error);
    throw new ApiError(body.error.code, response.status, displayMessage, body.error.details);
  }
  // Thân không theo mẫu chung, ví dụ trang lỗi HTML của proxy. Câu lỗi không nói về dạng phản hồi kỹ thuật.
  throw new ApiError('UNKNOWN_ERROR', response.status, messageForStatus(response.status));
}

/**
 * Gọi API và trả về phần `data`. Lỗi mạng hoặc lỗi từ API đều thành `ApiError`
 * để giao diện hiển thị `message` tiếng Việt mà không phải tự phân tích response.
 */
export async function apiRequest<T>(path: string, init?: RequestInit): Promise<T> {
  // Request đầu tiên của tab chạy một mình để nhận cookie pk_anon từ máy chủ.
  const isFirstRequest = !hasStartedInitialRequest;
  if (isFirstRequest) {
    hasStartedInitialRequest = true;
  } else {
    // Các request gửi sau phải chờ request đầu tiên hoàn tất.
    await initialRequestGate;
  }

  try {
    return await executeRequest<T>(path, init);
  } finally {
    // Giải phóng bước chờ trong mọi nhánh thành công và thất bại.
    if (isFirstRequest) {
      isInitialRequestSettled = true;
      resolveInitialRequestGate?.();
    }
  }
}
