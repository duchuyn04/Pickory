// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import { ApiError, messageForStatus } from '../../../src/shared/api';
import { downloadFile, readDownloadFileName } from '../../../src/shared/download-file';
import { findExportsPrintingExample } from '../features/exports-printing/contract-fixtures';
import {
  buildExampleResponse,
  readServerErrorExample,
} from '../features/exports-printing/error-examples';

const FALLBACK_FILE_NAME = 'pickory-export.xlsx';
const NETWORK_FAILURE_MESSAGE = 'Không tải được file. Vui lòng thử lại.';

describe('readDownloadFileName', () => {
  it('dùng `filename*` khi header có cả `filename` và `filename*` khác nhau', () => {
    // Hai tên khác nhau cố ý. Nếu code chọn nhầm `filename` thì test này đỏ.
    const header = `attachment; filename="tên-ascii.xlsx"; filename*=UTF-8''don-h%C3%A0ng.xlsx`;
    expect(readDownloadFileName(header)).toBe('don-hàng.xlsx');
  });

  it('dùng `filename` có dấu ngoặc kép khi không có `filename*`', () => {
    expect(readDownloadFileName('attachment; filename="don-hang.xlsx"')).toBe('don-hang.xlsx');
  });

  it('dùng `filename` không có dấu ngoặc kép khi không có `filename*`', () => {
    expect(readDownloadFileName('attachment; filename=don-hang.xlsx')).toBe('don-hang.xlsx');
  });

  it('giải mã tên tiếng Việt mã hóa phần trăm', () => {
    expect(readDownloadFileName(`attachment; filename*=UTF-8''don-h%C3%A0ng.xlsx`)).toBe(
      'don-hàng.xlsx',
    );
  });

  it('đọc đúng tên từ header của ví dụ `orders export success`', () => {
    const example = findExportsPrintingExample('exports.json', 'orders export success');
    const contentDisposition = example.headers?.['Content-Disposition'] ?? null;
    expect(readDownloadFileName(contentDisposition)).toBe('don-hang-20261110-1530.xlsx');
  });

  it('header `null` thì dùng tên dự phòng', () => {
    expect(readDownloadFileName(null)).toBe(FALLBACK_FILE_NAME);
  });

  it('header rỗng thì dùng tên dự phòng', () => {
    expect(readDownloadFileName('')).toBe(FALLBACK_FILE_NAME);
  });

  it('header không có tên file thì dùng tên dự phòng', () => {
    expect(readDownloadFileName('attachment')).toBe(FALLBACK_FILE_NAME);
  });

  it('`filename*` mã hóa lỗi thì dùng tên dự phòng và không ném lỗi', () => {
    expect(readDownloadFileName(`attachment; filename*=UTF-8''%E0%A4%A`)).toBe(FALLBACK_FILE_NAME);
  });

  it('chỉ giữ phần tên cuối khi tên chứa `../`', () => {
    expect(readDownloadFileName('attachment; filename="../../etc/passwd.xlsx"')).toBe(
      'passwd.xlsx',
    );
  });

  it('chỉ giữ phần tên cuối khi tên chứa dấu `\\`', () => {
    expect(readDownloadFileName('attachment; filename="..\\..\\windows\\don.xlsx"')).toBe(
      'don.xlsx',
    );
  });

  it('chỉ giữ phần tên cuối khi `filename*` mã hóa dấu `/`', () => {
    expect(readDownloadFileName(`attachment; filename*=UTF-8''..%2F..%2Fdon.xlsx`)).toBe(
      'don.xlsx',
    );
  });

  it('tên chỉ gồm khoảng trắng thì dùng tên dự phòng', () => {
    expect(readDownloadFileName('attachment; filename="   "')).toBe(FALLBACK_FILE_NAME);
  });

  it('tên kết thúc bằng dấu `/` thì dùng tên dự phòng', () => {
    expect(readDownloadFileName('attachment; filename="thu-muc/"')).toBe(FALLBACK_FILE_NAME);
  });

  it.each([['..'], ['.'], ['a/..'], ['a/.'], ['..\\..']])(
    'tên `%s` chỉ gồm dấu chấm thì dùng tên dự phòng',
    (fileName) => {
      expect(readDownloadFileName(`attachment; filename="${fileName}"`)).toBe(FALLBACK_FILE_NAME);
    },
  );
});

// Theo dõi lần bấm của thẻ `<a>` tạm mà `downloadFile` tạo ra.
interface RecordedAnchorClick {
  download: string;
  href: string;
  wasInBodyAtClick: boolean;
}

function fileResponse(contentDisposition?: string): Response {
  const headers = new Headers({
    'Content-Type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  });
  if (contentDisposition) headers.set('Content-Disposition', contentDisposition);
  return new Response(new Blob(['xlsx-bytes']), { status: 200, headers });
}

// Chạy hàm phải thất bại và trả lỗi `ApiError` của nó. Hàm chạy xong không lỗi thì test đỏ.
async function captureApiError(action: () => Promise<void>): Promise<ApiError> {
  try {
    await action();
  } catch (error) {
    if (error instanceof ApiError) return error;
    throw error;
  }
  throw new Error('Hàm phải ném ApiError nhưng đã chạy xong.');
}

describe('downloadFile', () => {
  let anchorClicks: RecordedAnchorClick[];
  let createObjectUrl: Mock<(blob: Blob) => string>;
  let revokeObjectUrl: Mock<(url: string) => void>;
  let fetchMock: Mock<typeof fetch>;
  let clickShouldThrow: boolean;

  beforeEach(() => {
    anchorClicks = [];
    clickShouldThrow = false;
    createObjectUrl = vi.fn(() => 'blob:pickory-test');
    revokeObjectUrl = vi.fn();
    // jsdom không có hai hàm này, nên test gắn bản giả lên `URL`.
    Object.assign(URL, { createObjectURL: createObjectUrl, revokeObjectURL: revokeObjectUrl });

    // Thay `click` của thẻ `<a>` để jsdom không điều hướng, và để test ghi lại lần bấm.
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      anchorClicks.push({
        download: this.download,
        href: this.getAttribute('href') ?? '',
        wasInBodyAtClick: document.body.contains(this),
      });
      if (clickShouldThrow) throw new Error('click thất bại');
    });

    fetchMock = vi.fn<typeof fetch>();
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('gọi `fetch` với cookie phiên và không gửi `Content-Type` của JSON', async () => {
    fetchMock.mockResolvedValue(fileResponse());
    await downloadFile('/api/admin/exports/orders');

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledWith('/api/admin/exports/orders', {
      credentials: 'include',
    });
  });

  it('trả 200 đúng tên file từ header của ví dụ `orders export success`', async () => {
    const example = findExportsPrintingExample('exports.json', 'orders export success');
    fetchMock.mockResolvedValue(fileResponse(example.headers?.['Content-Disposition']));

    await downloadFile('/api/admin/exports/orders');

    expect(anchorClicks).toEqual([
      {
        download: 'don-hang-20261110-1530.xlsx',
        href: 'blob:pickory-test',
        wasInBodyAtClick: true,
      },
    ]);
    expect(createObjectUrl).toHaveBeenCalledTimes(1);
    expect(createObjectUrl.mock.calls[0]?.[0]).toBeInstanceOf(Blob);
    expect(revokeObjectUrl).toHaveBeenCalledWith('blob:pickory-test');
  });

  it('trả 200 không có header `Content-Disposition` thì dùng tên dự phòng', async () => {
    fetchMock.mockResolvedValue(fileResponse());
    await downloadFile('/api/admin/exports/orders');
    expect(anchorClicks[0]?.download).toBe(FALLBACK_FILE_NAME);
  });

  it('gỡ thẻ `<a>` tạm khỏi `document.body` sau khi xong', async () => {
    fetchMock.mockResolvedValue(fileResponse());
    await downloadFile('/api/admin/exports/orders');
    expect(document.body.querySelector('a[download]')).toBeNull();
  });

  it('422 `orders export too large` ném `ApiError` mang câu của server', async () => {
    const serverError = readServerErrorExample('orders export too large');
    fetchMock.mockResolvedValue(buildExampleResponse('orders export too large'));

    const error = await captureApiError(() => downloadFile('/api/admin/exports/orders'));

    expect(error.code).toBe('EXPORT_TOO_LARGE');
    expect(error.status).toBe(422);
    expect(error.message).toBe(serverError.message);
    expect(createObjectUrl).not.toHaveBeenCalled();
  });

  it('403 `orders export missing reports export permission` ném `ApiError` mang câu của server', async () => {
    const exampleName = 'orders export missing reports export permission';
    const serverError = readServerErrorExample(exampleName);
    fetchMock.mockResolvedValue(buildExampleResponse(exampleName));

    const error = await captureApiError(() => downloadFile('/api/admin/exports/orders'));

    expect(error.code).toBe(serverError.code);
    expect(error.status).toBe(403);
    expect(error.message).toBe(serverError.message);
  });

  it('400 `orders export invalid filter` chọn câu lỗi giống `apiRequest`', async () => {
    fetchMock.mockResolvedValue(buildExampleResponse('orders export invalid filter'));

    const error = await captureApiError(() => downloadFile('/api/admin/exports/orders'));

    expect(error.code).toBe('VALIDATION_ERROR');
    // Câu của từng trường, không phải câu tổng `Dữ liệu không hợp lệ.`.
    expect(error.message).toBe('Ngày bắt đầu không được sau ngày kết thúc.');
    expect(error.details).toEqual([
      { key: 'from', message: 'Ngày bắt đầu không được sau ngày kết thúc.' },
    ]);
  });

  it('500 có thân HTML ném `UNKNOWN_ERROR` với câu theo mã trạng thái', async () => {
    fetchMock.mockResolvedValue(
      new Response('<html><body>Bad gateway</body></html>', {
        status: 500,
        headers: { 'Content-Type': 'text/html' },
      }),
    );

    const error = await captureApiError(() => downloadFile('/api/admin/exports/orders'));

    expect(error.code).toBe('UNKNOWN_ERROR');
    expect(error.status).toBe(500);
    expect(error.message).toBe('Máy chủ đang gặp sự cố. Hãy thử lại sau.');
  });

  // Thân JSON hợp lệ nhưng không đúng dạng lỗi của API. Mọi dạng phải ra `UNKNOWN_ERROR`, không ném lỗi lạ.
  it.each([
    ['`error` là chuỗi', { error: 'x' }],
    ['`error` là `null`', { error: null }],
    ['`error` thiếu `code`', { error: { message: 'm' } }],
    ['`error` thiếu `message`', { error: { code: 'SOME_CODE' } }],
    ['`message` không phải chuỗi', { error: { code: 'SOME_CODE', message: 42 } }],
    [
      '`details` không phải mảng',
      { error: { code: 'VALIDATION_ERROR', message: 'm', details: 'sai' } },
    ],
    [
      'dòng của `details` thiếu `message`',
      { error: { code: 'VALIDATION_ERROR', message: 'm', details: [{ key: 'from' }] } },
    ],
    [
      'thân dạng ProblemDetails (type, title, status)',
      { type: 'https://example.test/errors/400', title: 'Bad Request', status: 400 },
    ],
    ['thân là mảng', []],
  ])(
    'thân JSON sai dạng (%s) ném `UNKNOWN_ERROR` với câu theo mã trạng thái',
    async (_name, body) => {
      fetchMock.mockResolvedValue(
        new Response(JSON.stringify(body), {
          status: 400,
          headers: { 'Content-Type': 'application/json' },
        }),
      );

      const error = await captureApiError(() => downloadFile('/api/admin/exports/orders'));

      expect(error.code).toBe('UNKNOWN_ERROR');
      expect(error.status).toBe(400);
      expect(error.message).toBe(messageForStatus(400));
    },
  );

  it('`fetch` ném lỗi thì ném `NETWORK_ERROR` với câu riêng của việc tải file', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));

    const error = await captureApiError(() => downloadFile('/api/admin/exports/orders'));

    expect(error.code).toBe('NETWORK_ERROR');
    expect(error.status).toBe(0);
    expect(error.message).toBe(NETWORK_FAILURE_MESSAGE);
  });

  it('`blob()` ném lỗi giữa lúc tải thân file thì ném `NETWORK_ERROR` với câu riêng của việc tải file', async () => {
    const response = fileResponse();
    // Mô phỏng mất mạng sau khi đã nhận header: `fetch` xong nhưng đọc thân thất bại.
    vi.spyOn(response, 'blob').mockRejectedValue(new TypeError('network error'));
    fetchMock.mockResolvedValue(response);

    const error = await captureApiError(() => downloadFile('/api/admin/exports/orders'));

    expect(error.code).toBe('NETWORK_ERROR');
    expect(error.status).toBe(0);
    expect(error.message).toBe(NETWORK_FAILURE_MESSAGE);
    expect(createObjectUrl).not.toHaveBeenCalled();
  });

  it('`click` ném lỗi thì vẫn gọi `revokeObjectURL` và gỡ thẻ `<a>`', async () => {
    fetchMock.mockResolvedValue(fileResponse());
    clickShouldThrow = true;

    await expect(downloadFile('/api/admin/exports/orders')).rejects.toThrow('click thất bại');

    expect(revokeObjectUrl).toHaveBeenCalledWith('blob:pickory-test');
    expect(document.body.querySelector('a[download]')).toBeNull();
  });
});
