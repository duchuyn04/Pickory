import { ApiError, messageForStatus, toDisplayMessage } from './api';

// Tên file dùng khi header không có tên đọc được. Đuôi `.xlsx` khớp loại file mà route xuất trả về.
const FALLBACK_DOWNLOAD_FILE_NAME = 'pickory-export.xlsx';

// Câu hiện cho người dùng khi không tải được file. Câu này nói đúng về việc tải file, khác câu của `apiRequest`.
export const DOWNLOAD_FAILURE_MESSAGE = 'Không tải được file. Vui lòng thử lại.';

// Tên mã hóa phần trăm của header, ví dụ `filename*=UTF-8''don-h%C3%A0ng.xlsx`. Nhóm bắt lấy chuỗi đã mã hóa.
const ENCODED_FILE_NAME_PATTERN = /(?:^|;)\s*filename\*\s*=\s*UTF-8'[^']*'([^;]*)/i;

// Tên thường, có hoặc không có dấu ngoặc kép. Mẫu không khớp `filename*` vì sau `filename` phải là dấu `=`.
const PLAIN_FILE_NAME_PATTERN = /(?:^|;)\s*filename\s*=\s*(?:"([^"]*)"|([^;]*))/i;

// Dấu phân cách đường dẫn của Windows và Unix. Tên tải về không được chứa chúng.
const PATH_SEPARATOR_PATTERN = /[/\\]/;

// Lấy tên file từ header `Content-Disposition`. Hàm luôn trả một tên dùng được, không bao giờ ném lỗi.
export function readDownloadFileName(contentDisposition: string | null): string {
  // Thiếu header thì không có tên nào để đọc.
  if (!contentDisposition) return FALLBACK_DOWNLOAD_FILE_NAME;

  // Lấy tên thô từ header. Chưa dùng được ngay vì tên có thể chứa đường dẫn.
  const rawFileName = readRawFileName(contentDisposition);

  // Header không có tên, hoặc tên mã hóa lỗi, thì file vẫn phải tải được với tên dự phòng.
  if (rawFileName === null) return FALLBACK_DOWNLOAD_FILE_NAME;

  // Chỉ giữ phần tên cuối. Server hoặc proxy bị lỗi không thể ép file ghi ra ngoài thư mục tải về qua `../`.
  const lastSegment = rawFileName.split(PATH_SEPARATOR_PATTERN).pop()?.trim() ?? '';

  // Tên rỗng, `.` hoặc `..` không phải tên file thật nên dùng tên dự phòng.
  if (lastSegment === '' || lastSegment === '.' || lastSegment === '..') {
    return FALLBACK_DOWNLOAD_FILE_NAME;
  }
  return lastSegment;
}

// Chọn tên thô trong header. Trả `null` khi header không có tên nào, hoặc `filename*` mã hóa lỗi.
function readRawFileName(contentDisposition: string): string | null {
  // Header có cả hai dạng tên thì `filename*` thắng, vì đó là dạng giữ được chữ có dấu.
  const encodedMatch = ENCODED_FILE_NAME_PATTERN.exec(contentDisposition);
  if (encodedMatch) {
    try {
      return decodeURIComponent((encodedMatch[1] ?? '').trim());
    } catch {
      // Chuỗi phần trăm hỏng làm `decodeURIComponent` ném lỗi. Việc tải file không được hỏng chỉ vì một cái tên.
      return null;
    }
  }

  const plainMatch = PLAIN_FILE_NAME_PATTERN.exec(contentDisposition);
  if (!plainMatch) return null;
  // Nhóm 1 là tên trong dấu ngoặc kép, nhóm 2 là tên không có ngoặc.
  return plainMatch[1] ?? plainMatch[2] ?? null;
}

// Một dòng chi tiết lỗi theo trường, ví dụ `{ key: 'from', message: '...' }`.
interface ApiErrorDetail {
  key: string;
  message: string;
}

// Phần `error` trong thân lỗi của API. Dạng này khớp thân lỗi mà `apiRequest` đọc.
interface ApiErrorBody {
  code: string;
  message: string;
  details?: ApiErrorDetail[];
}

// Kiểm một dòng chi tiết có đủ `key` và `message` kiểu chuỗi. `toDisplayMessage` gọi `.trim()` trên `message`.
function isApiErrorDetail(value: unknown): value is ApiErrorDetail {
  return (
    typeof value === 'object' &&
    value !== null &&
    'key' in value &&
    typeof value.key === 'string' &&
    'message' in value &&
    typeof value.message === 'string'
  );
}

// Kiểm `error` đúng dạng mà `toDisplayMessage` cần. Thân lỗi của proxy hoặc server hỏng có thể mang dạng khác.
function isApiErrorBody(value: unknown): value is ApiErrorBody {
  if (typeof value !== 'object' || value === null) return false;
  if (!('code' in value) || typeof value.code !== 'string') return false;
  if (!('message' in value) || typeof value.message !== 'string') return false;
  // Backend bỏ `details` khi không có. Có thì phải là mảng, vì `toDisplayMessage` gọi `.map` trên nó.
  if (!('details' in value) || value.details === undefined) return true;
  return Array.isArray(value.details) && value.details.every(isApiErrorDetail);
}

// Chuyển phản hồi lỗi thành `ApiError` theo đúng cách của `apiRequest`, để giao diện hiện lỗi giống nhau.
async function readErrorResponse(response: Response): Promise<ApiError> {
  // Thân không phải JSON, ví dụ trang lỗi của proxy, thì thành `null` thay vì ném lỗi đọc.
  const body: unknown = await response.json().catch(() => null);

  if (typeof body === 'object' && body !== null && 'error' in body && isApiErrorBody(body.error)) {
    const { error } = body;
    return new ApiError(
      error.code,
      response.status,
      toDisplayMessage(response.status, error),
      error.details,
    );
  }
  // Thân không theo mẫu chung nên chọn câu lỗi theo mã trạng thái.
  return new ApiError('UNKNOWN_ERROR', response.status, messageForStatus(response.status));
}

// Tải file từ API về máy bằng thẻ `<a download>` tạm. Lỗi của server hoặc mạng đều thành `ApiError`.
export async function downloadFile(path: string): Promise<void> {
  let response: Response;
  try {
    // Request GET đọc file nên không gửi `Content-Type`. Cookie phiên phải đi kèm để server nhận ra người dùng.
    response = await fetch(path, { credentials: 'include' });
  } catch {
    throw new ApiError('NETWORK_ERROR', 0, DOWNLOAD_FAILURE_MESSAGE);
  }

  // Chỉ status 200 mang file. Mọi status khác mang thân lỗi hoặc thân không đoán được.
  if (response.status !== 200) throw await readErrorResponse(response);

  let fileBlob: Blob;
  try {
    // Đọc cả thân file vào bộ nhớ. Mất mạng giữa chừng làm `blob()` ném `TypeError`, nên đổi thành lỗi mạng như `fetch`.
    fileBlob = await response.blob();
  } catch {
    throw new ApiError('NETWORK_ERROR', 0, DOWNLOAD_FAILURE_MESSAGE);
  }

  // Lấy tên file từ header của server. Tên này quyết định file tải về mang tên gì trên máy người dùng.
  const fileName = readDownloadFileName(response.headers.get('Content-Disposition'));

  // Tạo URL tạm trỏ tới `Blob` trong bộ nhớ. Thẻ `<a>` cần URL này để tải file mà không gọi server lần nữa.
  const objectUrl = URL.createObjectURL(fileBlob);

  // Thẻ `<a>` tạm mang tên file và URL. Trình duyệt chỉ đặt tên file tải về theo thuộc tính `download`.
  const anchor = document.createElement('a');
  try {
    anchor.href = objectUrl;
    anchor.download = fileName;
    // Một số trình duyệt chỉ nhận `click()` trên thẻ đã nằm trong tài liệu.
    document.body.appendChild(anchor);
    anchor.click();
  } finally {
    // Gỡ thẻ tạm khỏi tài liệu. Thẻ còn lại sẽ tích tụ sau mỗi lần xuất.
    anchor.remove();
    // Thu hồi URL tạm trong `finally`. Nếu `click()` ném lỗi thì bộ nhớ của `Blob` vẫn được giải phóng.
    URL.revokeObjectURL(objectUrl);
  }
}
