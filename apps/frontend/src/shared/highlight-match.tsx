import { findSearchMatches } from './search-text';
import './highlight-match.css';

interface HighlightMatchProps {
  text: string;
  // Từ khóa đang lọc. Rỗng hoặc toàn khoảng trắng thì không tô gì.
  keyword: string;
}

// Hiện chữ gốc có dấu, bọc phần khớp từ khóa trong `<mark>`. Trình đọc màn hình đọc chữ liền mạch, không ngắt giữa chừng.
export function HighlightMatch({ text, keyword }: HighlightMatchProps) {
  const matches = findSearchMatches(text, keyword);
  // Không có đoạn khớp thì trả chuỗi nguyên. Dòng bảng không thêm phần tử thừa.
  if (matches.length === 0) return <>{text}</>;

  const parts: React.ReactNode[] = [];
  let cursor = 0;
  for (const match of matches) {
    // Chữ nằm giữa đoạn khớp trước và đoạn khớp này giữ nguyên, không tô.
    if (match.start > cursor) parts.push(text.slice(cursor, match.start));
    parts.push(
      <mark key={match.start} className="search-match">
        {text.slice(match.start, match.end)}
      </mark>,
    );
    cursor = match.end;
  }
  // Chữ sau đoạn khớp cuối cũng phải hiện, nếu không tên sản phẩm bị cụt.
  if (cursor < text.length) parts.push(text.slice(cursor));

  return <>{parts}</>;
}
