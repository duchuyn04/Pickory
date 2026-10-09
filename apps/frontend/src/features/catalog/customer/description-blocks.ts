// Đọc tài liệu mô tả JSON thành danh sách khối đã lọc. Nút hoặc mark không có trong schema bị bỏ.

export interface DescriptionRun {
  text: string;
  bold: boolean;
  italic: boolean;
}

export type DescriptionBlock =
  | { kind: 'paragraph'; runs: DescriptionRun[] }
  // `level` là cấp tiêu đề của tài liệu (1 đến 3), chưa đổi sang cấp thẻ HTML.
  | { kind: 'heading'; level: 1 | 2 | 3; runs: DescriptionRun[] };

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function readMarkTypes(marks: unknown): Set<string> {
  if (!Array.isArray(marks)) return new Set();
  const markTypes = marks.flatMap((mark) =>
    isRecord(mark) && typeof mark.type === 'string' ? [mark.type] : [],
  );
  return new Set(markTypes);
}

function readRuns(content: unknown): DescriptionRun[] {
  if (!Array.isArray(content)) return [];
  const runs: DescriptionRun[] = [];
  for (const node of content) {
    // Chỉ nút `text` được vẽ. Nút khác bị bỏ cùng mọi nội dung con, vì schema không cho phép nút đó.
    if (!isRecord(node) || node.type !== 'text') continue;
    if (typeof node.text !== 'string' || node.text === '') continue;
    // Mark lạ như `link` bị bỏ qua nhưng chữ vẫn hiện, để khách không mất nội dung.
    const markTypes = readMarkTypes(node.marks);
    runs.push({ text: node.text, bold: markTypes.has('bold'), italic: markTypes.has('italic') });
  }
  return runs;
}

function readHeadingLevel(attrs: unknown): 1 | 2 | 3 | null {
  if (!isRecord(attrs)) return null;
  const level = attrs.level;
  return level === 1 || level === 2 || level === 3 ? level : null;
}

function readBlock(node: unknown): DescriptionBlock | null {
  if (!isRecord(node)) return null;
  const runs = readRuns(node.content);
  // Khối không có chữ nào hiện được thì bỏ, để không tạo thẻ rỗng trên trang.
  if (runs.length === 0) return null;

  if (node.type === 'paragraph') return { kind: 'paragraph', runs };
  if (node.type === 'heading') {
    const level = readHeadingLevel(node.attrs);
    // Cấp ngoài 1 đến 3 không có trong schema, nên bỏ cả tiêu đề.
    return level === null ? null : { kind: 'heading', level, runs };
  }
  return null;
}

export function readDescriptionBlocks(description: unknown): DescriptionBlock[] {
  if (!isRecord(description) || description.type !== 'doc') return [];
  if (!Array.isArray(description.content)) return [];

  const blocks: DescriptionBlock[] = [];
  for (const node of description.content) {
    const block = readBlock(node);
    if (block) blocks.push(block);
  }
  return blocks;
}
