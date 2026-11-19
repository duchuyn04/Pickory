import { describe, expect, it } from 'vitest';
import { findCommerceExample, listCommerceExamples } from './contract-fixtures';

describe('bộ đọc file mẫu commerce', () => {
  it('tìm được ví dụ theo tên', () => {
    const example = findCommerceExample('manual-fulfillment.json', 'start manual shipping');

    expect(example.status).toBe(200);
    expect(example.method).toBe('PUT');
  });

  it('đọc được file mẫu điều kiện đánh giá', () => {
    const example = findCommerceExample('review-eligibility.json', 'order shipping');

    expect(example.method).toBe('GET');
  });

  it('báo đúng chữ khi sai tên ví dụ', () => {
    expect(() => findCommerceExample('manual-fulfillment.json', 'khong co')).toThrow(
      'Không có ví dụ "khong co" trong file mẫu "manual-fulfillment.json".',
    );
  });

  it('báo đúng chữ khi sai tên file', () => {
    expect(() => findCommerceExample('khong-co.json', 'x')).toThrow(
      'Không tìm thấy file mẫu "khong-co.json" trong spec/fixtures/commerce.',
    );
  });

  it('liệt kê mọi ví dụ theo thứ tự trong file', () => {
    const names = listCommerceExamples('manual-fulfillment.json').map((example) => example.name);

    expect(names).toEqual([
      'start manual shipping',
      'confirm manual delivery',
      'invalid reason',
      'invalid transition',
      'forbidden',
      'order not found',
      'unauthorized',
    ]);
  });
});
