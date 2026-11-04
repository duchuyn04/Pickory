import { describe, expect, it } from 'vitest';
import {
  parseCallbackRequests,
  parseQueueSessions,
} from '../../../../../src/features/support-desk/admin/support-desk-schemas';
import { findSupportDeskExample } from '../contract-fixtures';

// Ví dụ trong file mẫu là hợp đồng dùng chung với backend. Schema phải đọc được mọi ví dụ màn CSKH dùng.
describe('schema hàng chờ đọc ví dụ của admin-sessions.json', () => {
  it.each(['hàng chờ', 'phiên của tôi sau khi đồng nghiệp chuyển'])(
    'đọc được ví dụ %s',
    (exampleName) => {
      const example = findSupportDeskExample('admin-sessions.json', exampleName);
      const response = example.response as { data: unknown };

      // Đọc rồi trả lại đúng dữ liệu mẫu. Schema thiếu trường hay sai kiểu thì case này đỏ.
      expect(parseQueueSessions(response.data)).toEqual(response.data);
    },
  );
});

describe('schema yêu cầu gọi lại đọc ví dụ của admin-callbacks.json', () => {
  it('đọc được yêu cầu đã xóa số sau 30 ngày', () => {
    const example = findSupportDeskExample('admin-callbacks.json', 'danh sách yêu cầu gọi lại');
    const response = example.response as { data: unknown };

    expect(parseCallbackRequests(response.data)).toEqual(response.data);
  });
});
