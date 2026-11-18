import { describe, expect, it } from 'vitest';
import {
  findExportsPrintingExample,
  listExportsPrintingExamples,
  parseContractFixture,
} from './contract-fixtures';

describe('đọc ví dụ của đợt xuất Excel và in hóa đơn', () => {
  it('đọc được ví dụ có response', () => {
    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices two orders with vat and cancelled',
    );

    expect(example.status).toBe(200);
    expect(example.method).toBe('GET');
    expect(example.response).toMatchObject({ data: { shop: { name: 'Pickory' } } });
  });

  it('đọc được ví dụ file Excel có headers và workbook, không có response', () => {
    const example = findExportsPrintingExample('exports.json', 'orders export success');

    expect(example.status).toBe(200);
    expect(example.response).toBeUndefined();
    expect(example.workbook).toBeDefined();
    expect(example.headers?.['Content-Type']).toBe(
      'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    );
    expect(example.headers?.['Content-Disposition']).toContain('don-hang-20261110-1530.xlsx');
  });

  it('báo lỗi nêu tên file khi file không tồn tại', () => {
    expect(() => findExportsPrintingExample('khong-co.json', 'orders export success')).toThrow(
      'Không tìm thấy file mẫu "khong-co.json" trong spec/fixtures/exports-printing.',
    );
  });

  it('báo lỗi nêu tên ví dụ và tên file khi tên ví dụ sai, không trả ví dụ đầu danh sách', () => {
    expect(() => findExportsPrintingExample('exports.json', 'khong co vi du nay')).toThrow(
      'Không có ví dụ "khong co vi du nay" trong file mẫu "exports.json".',
    );
  });

  it('liệt kê mọi ví dụ của file theo thứ tự trong file', () => {
    const examples = listExportsPrintingExamples('exports.json');

    expect(examples.length).toBeGreaterThan(1);
    expect(examples[0]?.name).toBe('orders export success');
  });
});

describe('kiểm hình dạng file mẫu', () => {
  it('báo lỗi khi gốc file không phải object', () => {
    expect(() => parseContractFixture('hong.json', '[]')).toThrow(
      'File mẫu "hong.json" phải có gốc là object.',
    );
  });

  it('báo lỗi khi file không phải JSON hợp lệ', () => {
    expect(() => parseContractFixture('hong.json', '{')).toThrow(
      'File mẫu "hong.json" không phải JSON hợp lệ.',
    );
  });

  it('báo lỗi khi examples không phải mảng', () => {
    expect(() => parseContractFixture('hong.json', '{"examples":{}}')).toThrow(
      'File mẫu "hong.json" phải có "examples" là mảng.',
    );
  });

  it.each([
    ['name', '{"method":"GET","status":200}', 'Ví dụ thứ 2'],
    ['method', '{"name":"vi du","status":200}', 'Ví dụ "vi du"'],
    ['status', '{"name":"vi du","method":"GET","status":"200"}', 'Ví dụ "vi du"'],
  ])('báo lỗi nêu ví dụ khi thiếu hoặc sai kiểu "%s"', (field, example, subject) => {
    const content = `{"examples":[{"name":"vi du dung","method":"GET","status":200},${example}]}`;

    expect(() => parseContractFixture('hong.json', content)).toThrow(
      `${subject} trong file mẫu "hong.json" thiếu "${field}" hoặc sai kiểu.`,
    );
  });

  it('báo lỗi nêu số thứ tự khi phần tử của examples không phải object', () => {
    const content = '{"examples":[{"name":"vi du dung","method":"GET","status":200},7]}';

    expect(() => parseContractFixture('hong.json', content)).toThrow(
      'Ví dụ thứ 2 trong file mẫu "hong.json" thiếu "name" hoặc sai kiểu.',
    );
  });

  it('báo lỗi nêu tên khi hai ví dụ trùng tên', () => {
    const example = '{"name":"vi du lap","method":"GET","status":200}';

    expect(() => parseContractFixture('hong.json', `{"examples":[${example},${example}]}`)).toThrow(
      'Tên ví dụ "vi du lap" bị trùng trong file mẫu "hong.json".',
    );
  });

  it('nhận ví dụ chỉ có name, method và status', () => {
    const { examples } = parseContractFixture(
      'ok.json',
      '{"examples":[{"name":"toi gian","method":"GET","status":204}]}',
    );

    expect(examples).toEqual([{ name: 'toi gian', method: 'GET', status: 204 }]);
  });
});
