import { describe, expect, it } from 'vitest';
import { toSlug } from '../../../../../src/modules/catalog/domain/slug';

describe('toSlug', () => {
  it('chuyển tên tiếng Việt có dấu thành slug không dấu và nối bằng gạch ngang', () => {
    const input = 'Laptop ASUS ROG Strix G16 Đen';
    const expected = 'laptop-asus-rog-strix-g16-den';
    expect(toSlug(input)).toBe(expected);
  });

  it('gộp ký tự đặc biệt và khoảng trắng liên tiếp thành một dấu gạch ngang duy nhất', () => {
    const input = 'Laptop   ASUS -- ROG!!! Strix #$% G16';
    const expected = 'laptop-asus-rog-strix-g16';
    expect(toSlug(input)).toBe(expected);
  });

  it('bỏ dấu gạch ngang ở đầu và cuối chuỗi kết quả', () => {
    const input = '---Tai nghe Sony WH-1000XM5---';
    const expected = 'tai-nghe-sony-wh-1000xm5';
    expect(toSlug(input)).toBe(expected);
  });

  it('xử lý đúng toàn bộ các nguyên âm tiếng Việt có dấu', () => {
    const input = 'Á À Ả Ã Ạ Ă Ắ Ằ Ẳ Ẵ Ặ Â Ấ Ầ Ẩ Ẫ Ậ É È Ẻ Ẽ Ẹ Ê Ế Ề Ể Ễ Ệ';
    const expected = 'a-a-a-a-a-a-a-a-a-a-a-a-a-a-a-a-a-e-e-e-e-e-e-e-e-e-e-e';
    expect(toSlug(input)).toBe(expected);
  });

  it('trả về chuỗi rỗng khi đầu vào chỉ gồm ký tự đặc biệt', () => {
    expect(toSlug('   @@@ ###   ')).toBe('');
  });
});
