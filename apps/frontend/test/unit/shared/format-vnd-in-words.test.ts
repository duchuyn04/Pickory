import { describe, expect, it } from 'vitest';
import { formatVndInWords } from '../../../src/shared/format-vnd-in-words';

// Danh sách cụm đọc sai cần chặn khi quét từ 0 tới 2000, kèm cách đọc đúng làm lý do.
const FORBIDDEN_PHRASES: ReadonlyArray<{ phrase: string; reason: string }> = [
  { phrase: 'mười mốt', reason: 'phải đọc là "mười một"' },
  { phrase: 'mười tư', reason: 'phải đọc là "mười bốn"' },
  { phrase: 'mười năm', reason: 'phải đọc là "mười lăm"' },
  { phrase: 'một mươi', reason: 'phải đọc là "mười"' },
  { phrase: 'mươi một', reason: 'phải đọc là "mươi mốt"' },
  { phrase: 'mươi bốn', reason: 'phải đọc là "mươi tư"' },
  { phrase: 'mươi năm', reason: 'phải đọc là "mươi lăm"' },
  { phrase: 'linh bốn', reason: 'phải đọc là "linh tư" theo tiêu chí' },
];

describe('formatVndInWords', () => {
  it.each([
    [0, 'Không đồng'],
    [1000005, 'Một triệu không trăm linh năm đồng'],
    [12990000, 'Mười hai triệu chín trăm chín mươi nghìn đồng'],
    [1000000000, 'Một tỷ đồng'],
    [21, 'Hai mươi mốt đồng'],
    [15, 'Mười lăm đồng'],
    [25, 'Hai mươi lăm đồng'],
    [104, 'Một trăm linh tư đồng'],
    [1001, 'Một nghìn không trăm linh một đồng'],
  ])('đọc số mẫu tiêu chí %d thành %j', (amount, expected) => {
    expect(formatVndInWords(amount)).toBe(expected);
  });

  it.each([
    [1, 'Một đồng'],
    [4, 'Bốn đồng'],
    [5, 'Năm đồng'],
    [10, 'Mười đồng'],
    [11, 'Mười một đồng'],
    [14, 'Mười bốn đồng'],
    [24, 'Hai mươi tư đồng'],
    [100, 'Một trăm đồng'],
    [101, 'Một trăm linh một đồng'],
    [105, 'Một trăm linh năm đồng'],
    [110, 'Một trăm mười đồng'],
    [1000, 'Một nghìn đồng'],
    [1004, 'Một nghìn không trăm linh tư đồng'],
    [1005, 'Một nghìn không trăm linh năm đồng'],
    [1100, 'Một nghìn một trăm đồng'],
    [1230000, 'Một triệu hai trăm ba mươi nghìn đồng'],
    [1000010, 'Một triệu không trăm mười đồng'],
    [1000014, 'Một triệu không trăm mười bốn đồng'],
    [1005000, 'Một triệu không trăm linh năm nghìn đồng'],
    [2000100, 'Hai triệu một trăm đồng'],
    [1005000000, 'Một tỷ không trăm linh năm triệu đồng'],
    [1000000000000, 'Một nghìn tỷ đồng'],
    [1005000000000, 'Một nghìn tỷ không trăm linh năm tỷ đồng'],
    [1000000000000000, 'Một triệu tỷ đồng'],
  ])('đọc đúng ví dụ và trường hợp biên %d thành %j', (amount, expected) => {
    expect(formatVndInWords(amount)).toBe(expected);
  });

  it('đọc đúng số nguyên an toàn lớn nhất `Number.MAX_SAFE_INTEGER`', () => {
    // Đối chiếu từng nhóm của `9.007.199.254.740.991`: triệu tỷ, nghìn tỷ, tỷ, triệu, nghìn, đơn vị.
    expect(formatVndInWords(Number.MAX_SAFE_INTEGER)).toBe(
      'Chín triệu tỷ không trăm linh bảy nghìn tỷ một trăm chín mươi chín tỷ hai trăm năm mươi tư triệu bảy trăm bốn mươi nghìn chín trăm chín mươi mốt đồng',
    );
  });

  it('coi `-0` như `0` và trả "Không đồng"', () => {
    expect(formatVndInWords(-0)).toBe('Không đồng');
  });

  it.each([
    [-1, '-1'],
    [1.5, '1.5'],
    [Number.NaN, 'NaN'],
    [Number.POSITIVE_INFINITY, 'Infinity'],
    [Number.NEGATIVE_INFINITY, '-Infinity'],
    [Number.MAX_SAFE_INTEGER + 1, String(Number.MAX_SAFE_INTEGER + 1)],
  ])('ném `RangeError` kèm giá trị sai khi nhận %s', (amount, expectedTextInMessage) => {
    expect(() => formatVndInWords(amount)).toThrow(RangeError);
    expect(() => formatVndInWords(amount)).toThrow(expectedTextInMessage);
  });

  it('giữ đúng quy tắc hình thức và không chứa cụm đọc sai từ 0 tới 2000', () => {
    for (let amount = 0; amount <= 2000; amount += 1) {
      const result = formatVndInWords(amount);

      expect(result).toMatch(/^\p{Lu}/u);
      expect(result.slice(1)).toBe(result.slice(1).toLowerCase());
      expect(result.endsWith(' đồng')).toBe(true);
      expect(result).toBe(result.trim());
      expect(result.includes('  ')).toBe(false);

      for (const { phrase, reason } of FORBIDDEN_PHRASES) {
        expect(result, `${amount} tạo "${result}" chứa "${phrase}" (${reason})`).not.toContain(
          phrase,
        );
      }
    }
  });
});
