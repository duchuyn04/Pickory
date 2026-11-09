import { describe, expect, it } from 'vitest';
import { findSupportDeskExample, selectSupportDeskEvents } from './contract-fixtures';

// Bộ đọc dùng chung giữa Vitest và Playwright. Test đọc thẳng file mẫu trong `spec/fixtures/support-desk/`.
describe('findSupportDeskExample', () => {
  it('trả ví dụ theo tên trong file mẫu', () => {
    const example = findSupportDeskExample('admin-sessions.json', 'hàng chờ');

    expect(example.method).toBe('GET');
    expect(example.path).toBe('/api/admin/support/sessions?view=waiting');
    expect(example.status).toBe(200);
    expect(example.response).toMatchObject({
      data: [{ preview: 'Đơn PK123 của mình giao tới đâu rồi?' }],
    });
  });

  it('trả đúng ví dụ khi file có nhiều ví dụ cùng route', () => {
    const example = findSupportDeskExample(
      'admin-sessions.json',
      'phiên của tôi sau khi đồng nghiệp chuyển',
    );

    expect(example.path).toBe('/api/admin/support/sessions?view=mine');
    expect(example.response).toMatchObject({
      data: [{ transferNote: 'Khách cần đổi size, đơn PK098 đã giao.' }],
    });
  });

  it('báo lỗi nêu tên file khi file mẫu không tồn tại', () => {
    expect(() => findSupportDeskExample('khong-co.json', 'hàng chờ')).toThrow(
      'Không tìm thấy file mẫu "khong-co.json" trong spec/fixtures/support-desk.',
    );
  });

  it('báo lỗi nêu tên file và tên ví dụ khi ví dụ không tồn tại', () => {
    expect(() => findSupportDeskExample('admin-sessions.json', 'không có')).toThrow(
      'Không có ví dụ "không có" trong file mẫu "admin-sessions.json".',
    );
  });
});

describe('selectSupportDeskEvents', () => {
  it('giữ mọi sự kiện cùng tên, đúng thứ tự trong file mẫu', () => {
    const events = selectSupportDeskEvents('customer-events.json', ['message.created']);

    expect(events.map((event) => event.name)).toEqual([
      'message.created',
      'message.created',
      'message.created',
    ]);
    expect(events[0]?.data).toMatchObject({ id: 'a0000000-0000-4000-8000-000000000003' });
  });

  it('chỉ trả sự kiện được chọn', () => {
    const events = selectSupportDeskEvents('admin-events.json', ['session.closed']);

    expect(events).toEqual([
      { name: 'session.closed', data: { sessionId: '5e550000-0000-4000-8000-000000000001' } },
    ]);
  });

  it('báo lỗi khi tên sự kiện không có trong file mẫu', () => {
    expect(() => selectSupportDeskEvents('admin-events.json', ['khong.co'])).toThrow(
      'Không có sự kiện "khong.co" trong file mẫu "admin-events.json".',
    );
  });

  it('báo lỗi khi file mẫu không có danh sách sự kiện', () => {
    expect(() => selectSupportDeskEvents('admin-sessions.json', ['session.closed'])).toThrow(
      'File mẫu "admin-sessions.json" không có danh sách sự kiện "events".',
    );
  });
});
