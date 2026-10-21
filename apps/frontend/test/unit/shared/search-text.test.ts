import { describe, expect, it } from 'vitest';
import { findSearchMatches, normalizeForSearch } from '../../../src/shared/search-text';

describe('normalizeForSearch dùng chung', () => {
  it('bỏ dấu và hạ chữ thường để so sánh được', () => {
    expect(normalizeForSearch('Điện Thoại')).toBe('dien thoai');
  });

  it('đổi chữ đ và Đ thành d vì NFD không tách được dấu của hai chữ này', () => {
    expect(normalizeForSearch('đường')).toBe('duong');
    expect(normalizeForSearch('Đà Nẵng')).toBe('da nang');
  });

  it('gộp khoảng trắng thừa và bỏ khoảng trắng hai đầu', () => {
    expect(normalizeForSearch('  ha    noi  ')).toBe('ha noi');
  });
});

describe('findSearchMatches', () => {
  it('khớp không dấu và trả đoạn theo vị trí trong chuỗi gốc có dấu', () => {
    expect(findSearchMatches('Điện thoại Samsung', 'dien thoai')).toEqual([{ start: 0, end: 10 }]);
  });

  it('không phân biệt hoa thường', () => {
    expect(findSearchMatches('IPHONE 15', 'iphone')).toEqual([{ start: 0, end: 6 }]);
    expect(findSearchMatches('iphone 15', 'IPHONE')).toEqual([{ start: 0, end: 6 }]);
  });

  it('chữ đ và Đ khớp d', () => {
    expect(findSearchMatches('Đường đi', 'd')).toEqual([
      { start: 0, end: 1 },
      { start: 6, end: 7 },
    ]);
    expect(findSearchMatches('Đà Nẵng', 'da nang')).toEqual([{ start: 0, end: 7 }]);
  });

  it('chuỗi gốc ở dạng NFD thì đoạn trả về bao cả dấu rời', () => {
    const decomposed = 'Điện thoại Samsung'.normalize('NFD');

    const matches = findSearchMatches(decomposed, 'dien thoai');

    expect(matches).toEqual([{ start: 0, end: decomposed.indexOf(' Samsung') }]);
    // Đoạn kết thúc sau dấu nặng rời của chữ `i` cuối, không dừng giữa chữ và dấu.
    expect(decomposed.slice(matches[0]!.start, matches[0]!.end)).toBe(
      'Điện thoại'.normalize('NFD'),
    );
  });

  it('trả mọi lần xuất hiện, không chỉ lần đầu', () => {
    expect(findSearchMatches('ao ao', 'ao')).toEqual([
      { start: 0, end: 2 },
      { start: 3, end: 5 },
    ]);
  });

  it('các lần xuất hiện không chồng nhau', () => {
    expect(findSearchMatches('aaaa', 'aa')).toEqual([
      { start: 0, end: 2 },
      { start: 2, end: 4 },
    ]);
  });

  it('so từ khóa như một cụm liền, không tách từng từ', () => {
    expect(findSearchMatches('thoai dien', 'dien thoai')).toEqual([]);
  });

  it('khoảng trắng thừa trong chữ gốc vẫn khớp cụm và đoạn bao cả khoảng trắng đó', () => {
    expect(findSearchMatches('Điện   thoại', 'dien thoai')).toEqual([{ start: 0, end: 12 }]);
  });

  it('khoảng trắng thừa ở hai đầu từ khóa bị bỏ', () => {
    expect(findSearchMatches('Áo thun', '  ao  ')).toEqual([{ start: 0, end: 2 }]);
  });

  it('từ khóa rỗng hoặc toàn khoảng trắng trả danh sách rỗng', () => {
    expect(findSearchMatches('Áo thun', '')).toEqual([]);
    expect(findSearchMatches('Áo thun', '   ')).toEqual([]);
  });

  it('không khớp trả danh sách rỗng', () => {
    expect(findSearchMatches('Áo thun', 'quan')).toEqual([]);
  });

  it('chữ rỗng trả danh sách rỗng', () => {
    expect(findSearchMatches('', 'ao')).toEqual([]);
  });
});
