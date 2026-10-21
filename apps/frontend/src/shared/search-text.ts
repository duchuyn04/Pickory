// Bỏ dấu và hạ chữ thường. Hàm này là luật so khớp chung, nên chữ tìm kiếm và chữ tô đậm không lệch nhau.
function foldLetters(text: string): string {
  return (
    text
      .toLowerCase()
      // NFD tách chữ có dấu thành chữ gốc cộng dấu rời.
      .normalize('NFD')
      // Xóa khoảng `\u0300-\u036f` (nhóm "Combining Diacritical Marks") để còn chữ gốc không dấu.
      .replace(/[\u0300-\u036f]/g, '')
      // Chữ `đ` là một chữ riêng, không phải `d` cộng dấu, nên NFD không tách được. Phải đổi riêng.
      .replace(/đ/g, 'd')
  );
}

// Đưa chữ về dạng so sánh được: thường, không dấu, một khoảng trắng giữa các từ.
export function normalizeForSearch(text: string): string {
  return (
    foldLetters(text)
      // Khách gõ thừa khoảng trắng vẫn tìm được. Nhiều khoảng trắng liền nhau thành một.
      .replace(/\s+/g, ' ')
      .trim()
  );
}

// Đoạn chữ khớp từ khóa, tính theo vị trí trong chuỗi gốc. `start` gồm, `end` không gồm.
export interface SearchMatch {
  start: number;
  end: number;
}

const COMBINING_MARK = /^[\u0300-\u036f]+$/;
const WHITESPACE = /^\s+$/;

// Tìm mọi chỗ chữ khớp từ khóa, so như `normalizeForSearch`: không dấu, không phân biệt hoa thường.
// Từ khóa được so như một cụm liền, giống backend (`ILIKE '%từ khóa%'`).
export function findSearchMatches(text: string, keyword: string): SearchMatch[] {
  const normalizedKeyword = normalizeForSearch(keyword);
  // Từ khóa rỗng khớp mọi nơi. Trả rỗng để không tô cả chuỗi.
  if (normalizedKeyword === '') return [];

  // Mỗi ký tự đã chuẩn hóa nhớ đoạn của nó trong chuỗi gốc. Nhờ đó vị trí tìm được đổi ngược về chuỗi gốc có dấu.
  let normalizedText = '';
  const spans: SearchMatch[] = [];
  let index = 0;
  for (const character of text) {
    const start = index;
    const end = index + character.length;
    index = end;

    // Dấu rời (chuỗi gốc ở dạng NFD) thuộc về chữ đứng trước. Mở rộng đoạn của chữ đó để đoạn tô bao cả dấu.
    if (COMBINING_MARK.test(character)) {
      const previousSpan = spans.at(-1);
      if (previousSpan) previousSpan.end = end;
      continue;
    }

    // Nhiều khoảng trắng liền nhau thành một, giống `normalizeForSearch`. Đoạn của khoảng trắng đó bao cả dãy.
    if (WHITESPACE.test(character) && normalizedText.endsWith(' ')) {
      spans.at(-1)!.end = end;
      continue;
    }

    const folded = WHITESPACE.test(character) ? ' ' : foldLetters(character);
    // Một ký tự gốc có thể ra nhiều ký tự chuẩn hóa. Mỗi ký tự đó dùng chung đoạn của ký tự gốc.
    for (const foldedCharacter of folded) {
      normalizedText += foldedCharacter;
      spans.push({ start, end });
    }
  }

  const matches: SearchMatch[] = [];
  let from = 0;
  for (;;) {
    const found = normalizedText.indexOf(normalizedKeyword, from);
    if (found === -1) break;
    const last = found + normalizedKeyword.length - 1;
    matches.push({ start: spans[found]!.start, end: spans[last]!.end });
    // Bắt đầu tìm tiếp sau đoạn vừa khớp, để các đoạn không chồng nhau.
    from = last + 1;
  }
  return matches;
}
