import { afterEach, describe, expect, it, vi, type Mock } from 'vitest';
import { ApiError } from '../../../../../src/shared/api';
import {
  createCannedResponse,
  deleteCannedResponse,
  fetchSupportSettings,
  saveSupportSettings,
  updateCannedResponse,
} from '../../../../../src/features/support-desk/admin/support-desk-api';
import {
  parseCannedResponse,
  parseCannedResponses,
  parseSupportSettings,
} from '../../../../../src/features/support-desk/admin/support-desk-schemas';
import { mapSupportSettingsServerErrors } from '../../../../../src/features/support-desk/admin/support-settings-validation';
import { findSupportDeskExample } from '../contract-fixtures';

afterEach(() => {
  vi.unstubAllGlobals();
});

function dataOf(fileName: string, exampleName: string): unknown {
  const example = findSupportDeskExample(fileName, exampleName);
  return (example.response as { data: unknown }).data;
}

// Giả `fetch` trả đúng ví dụ của file mẫu. Trả về mock để test kiểm URL, method và body đã gửi.
function stubFetchWithExample(fileName: string, exampleName: string) {
  const example = findSupportDeskExample(fileName, exampleName);
  const fetchMock = vi.fn(async () =>
    example.status === 204
      ? new Response(null, { status: 204 })
      : Response.json(example.response, { status: example.status }),
  );
  vi.stubGlobal('fetch', fetchMock);
  return { example, fetchMock };
}

function lastCall(fetchMock: Mock): { url: string; init: RequestInit } {
  const [url, init] = fetchMock.mock.calls.at(-1) as unknown as [string, RequestInit];
  return { url, init };
}

describe('schema cấu hình đọc ví dụ của admin-settings.json', () => {
  it.each(['đọc cấu hình', 'lưu cấu hình'])('đọc được ví dụ %s', (exampleName) => {
    const data = dataOf('admin-settings.json', exampleName);

    // Đọc rồi trả lại đúng dữ liệu mẫu. Schema thiếu trường hay sai kiểu thì case này đỏ.
    expect(parseSupportSettings(data)).toEqual(data);
  });

  it('opensAt sai dạng làm lỗi đọc', () => {
    const data = dataOf('admin-settings.json', 'đọc cấu hình') as Record<string, unknown>;

    expect(() => parseSupportSettings({ ...data, opensAt: '8h sáng' })).toThrow(ApiError);
    expect(() => parseSupportSettings({ ...data, opensAt: '24:00' })).toThrow(ApiError);
  });

  it('holidays sai dạng làm lỗi đọc', () => {
    const data = dataOf('admin-settings.json', 'đọc cấu hình') as Record<string, unknown>;

    expect(() => parseSupportSettings({ ...data, holidays: ['06/02/2027'] })).toThrow(ApiError);
    expect(() => parseSupportSettings({ ...data, holidays: 'none' })).toThrow(ApiError);
  });

  it('thiếu updatedAt làm lỗi đọc', () => {
    const withoutUpdatedAt: Record<string, unknown> = {
      ...parseSupportSettings(dataOf('admin-settings.json', 'đọc cấu hình')),
    };
    delete withoutUpdatedAt.updatedAt;

    expect(() => parseSupportSettings(withoutUpdatedAt)).toThrow(ApiError);
  });
});

describe('hàm API cấu hình đọc ví dụ của admin-settings.json', () => {
  it('fetchSupportSettings gọi GET và đọc response mẫu', async () => {
    const { example, fetchMock } = stubFetchWithExample('admin-settings.json', 'đọc cấu hình');

    const settings = await fetchSupportSettings();

    expect(settings).toEqual(dataOf('admin-settings.json', 'đọc cấu hình'));
    const { url, init } = lastCall(fetchMock);
    expect(url).toBe(example.path);
    expect(init.method ?? 'GET').toBe('GET');
  });

  it('saveSupportSettings gửi PUT với body của ví dụ, không có updatedAt', async () => {
    const { example, fetchMock } = stubFetchWithExample('admin-settings.json', 'lưu cấu hình');
    const requestBody = example.request as Record<string, unknown>;

    await saveSupportSettings({
      opensAt: '08:30',
      closesAt: '21:00',
      holidays: ['2027-02-06'],
      waitNudgeMinutes: 10,
      autoCloseMinutes: 30,
    });

    const { url, init } = lastCall(fetchMock);
    expect(url).toBe(example.path);
    expect(init.method).toBe('PUT');
    expect(JSON.parse(String(init.body))).toEqual(requestBody);
    expect(Object.keys(JSON.parse(String(init.body)))).not.toContain('updatedAt');
  });

  it('lỗi mốc nhắc lại giữ khóa waitNudgeMinutes để gắn đúng ô', async () => {
    stubFetchWithExample('admin-settings.json', 'mốc nhắc lại không trước mốc tự đóng');

    const error = await saveSupportSettings({
      opensAt: '08:00',
      closesAt: '21:00',
      holidays: [],
      waitNudgeMinutes: 30,
      autoCloseMinutes: 30,
    }).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    const mapped = mapSupportSettingsServerErrors(error as ApiError);
    expect(mapped.fieldErrors).toEqual({
      waitNudgeMinutes: 'Mốc nhắc lại phải nhỏ hơn mốc tự đóng.',
    });
    expect(mapped.unmatched).toEqual([]);
  });

  it('lỗi giờ mở cửa sau giờ đóng cửa gắn vào ô opensAt', async () => {
    stubFetchWithExample('admin-settings.json', 'giờ mở cửa sau giờ đóng cửa');

    const error = await saveSupportSettings({
      opensAt: '22:00',
      closesAt: '21:00',
      holidays: [],
      waitNudgeMinutes: 10,
      autoCloseMinutes: 30,
    }).catch((caught: unknown) => caught);

    const mapped = mapSupportSettingsServerErrors(error as ApiError);
    expect(mapped.fieldErrors).toEqual({ opensAt: 'Giờ mở cửa phải trước giờ đóng cửa.' });
  });
});

describe('schema mẫu trả lời đọc ví dụ của admin-canned-responses.json', () => {
  it('đọc được ví dụ "danh sách mẫu"', () => {
    const data = dataOf('admin-canned-responses.json', 'danh sách mẫu');

    expect(parseCannedResponses(data)).toEqual(data);
  });

  it.each(['tạo mẫu cá nhân', 'sửa mẫu'])('đọc được ví dụ %s', (exampleName) => {
    const data = dataOf('admin-canned-responses.json', exampleName);

    expect(parseCannedResponse(data)).toEqual(data);
  });

  it('thiếu editable làm lỗi đọc', () => {
    const withoutEditable: Record<string, unknown> = {
      ...parseCannedResponses(dataOf('admin-canned-responses.json', 'danh sách mẫu'))[0],
    };
    delete withoutEditable.editable;

    expect(() => parseCannedResponses([withoutEditable])).toThrow(ApiError);
  });
});

describe('hàm API mẫu trả lời đọc ví dụ của admin-canned-responses.json', () => {
  it('createCannedResponse gửi POST với body của ví dụ', async () => {
    const { example, fetchMock } = stubFetchWithExample(
      'admin-canned-responses.json',
      'tạo mẫu cá nhân',
    );

    const created = await createCannedResponse({
      shortcut: 'baohanh',
      body: 'Sản phẩm được bảo hành 12 tháng.',
      shared: false,
    });

    expect(created).toEqual(dataOf('admin-canned-responses.json', 'tạo mẫu cá nhân'));
    const { url, init } = lastCall(fetchMock);
    expect(url).toBe(example.path);
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual(example.request);
  });

  it('createCannedResponse báo CANNED_SHORTCUT_TAKEN với mã 409', async () => {
    stubFetchWithExample('admin-canned-responses.json', 'trùng phím tắt');

    const error = await createCannedResponse({
      shortcut: 'kiemdon',
      body: 'Mình xem đơn giúp bạn.',
      shared: false,
    }).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({
      code: 'CANNED_SHORTCUT_TAKEN',
      status: 409,
      message: 'Bạn đã có mẫu dùng phím tắt này.',
    });
  });

  it('updateCannedResponse gửi PATCH tới đường dẫn của mẫu và chỉ gửi trường đã đổi', async () => {
    const { example, fetchMock } = stubFetchWithExample('admin-canned-responses.json', 'sửa mẫu');

    await updateCannedResponse('ca000000-0000-4000-8000-000000000003', {
      body: 'Sản phẩm được bảo hành chính hãng 12 tháng.',
    });

    const { url, init } = lastCall(fetchMock);
    expect(url).toBe(example.path);
    expect(init.method).toBe('PATCH');
    expect(JSON.parse(String(init.body))).toEqual(example.request);
  });

  it('deleteCannedResponse gửi DELETE và chấp nhận 204 không có thân', async () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(
      deleteCannedResponse('ca000000-0000-4000-8000-000000000003'),
    ).resolves.toBeUndefined();

    const { url, init } = lastCall(fetchMock);
    expect(url).toBe('/api/admin/support/canned-responses/ca000000-0000-4000-8000-000000000003');
    expect(init.method).toBe('DELETE');
  });
});
