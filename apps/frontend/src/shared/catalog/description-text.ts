import { descriptionSchema, type DescriptionDoc } from './description-schema';

/**
 * Trích xuất toàn bộ chuỗi text từ tài liệu rich text sang dạng văn bản thuần.
 */
export function descriptionToPlainText(doc: DescriptionDoc): string {
  // Trả về chuỗi rỗng khi tài liệu không có danh sách khối nội dung nào.
  if (!doc.content || doc.content.length === 0) {
    return '';
  }

  const blocks: string[] = [];
  for (const block of doc.content) {
    // Bỏ qua các khối không có phần tử con để tránh sinh dòng trống dư thừa.
    if (!block.content || block.content.length === 0) {
      continue;
    }

    // Ghép các chuỗi text nội dòng và loại bỏ cấu trúc mark định dạng.
    const text = block.content
      .map((inline) => inline.text)
      .join('')
      .trim();
    if (text.length > 0) {
      blocks.push(text);
    }
  }

  // Nối các đoạn văn bằng một dòng trống để phân biệt ranh giới khối văn bản.
  return blocks.join('\n\n');
}

/**
 * Chuyển đổi văn bản thuần thành cấu trúc tài liệu rich text hợp lệ cho dữ liệu seed.
 */
export function plainTextToDescription(text: string): DescriptionDoc {
  const trimmed = text.trim();

  // Khởi tạo một đoạn văn trống khi dữ liệu đầu vào không chứa ký tự chữ nào.
  if (trimmed.length === 0) {
    return {
      type: 'doc',
      content: [{ type: 'paragraph' }],
    };
  }

  // Chuẩn hóa ký tự xuống dòng của Windows và Unix về một kiểu duy nhất.
  const normalized = trimmed.replace(/\r\n/g, '\n').replace(/\r/g, '\n');

  // Phân tách các khối văn bản khi phát hiện một hoặc nhiều dòng trống liên tiếp.
  const chunks = normalized.split(/\n\s*\n+/);

  const content = chunks
    .map((chunk) => chunk.trim())
    .filter((chunk) => chunk.length > 0)
    .map((chunk) => ({
      type: 'paragraph' as const,
      // Đặt toàn bộ chuỗi của đoạn vào một nút text con duy nhất.
      content: [{ type: 'text' as const, text: chunk }],
    }));

  // Tránh trả về mảng rỗng khi các khối sau khi cắt chỉ chứa khoảng trắng.
  if (content.length === 0) {
    return {
      type: 'doc',
      content: [{ type: 'paragraph' }],
    };
  }

  const doc: DescriptionDoc = {
    type: 'doc',
    content,
  };

  // Xác thực tài liệu qua schema để bảo đảm tính toàn vẹn dữ liệu.
  const parseResult = descriptionSchema.safeParse(doc);
  if (!parseResult.success) {
    // Ném lỗi chi tiết khi nội dung vượt quá giới hạn ký tự hoặc số lượng nút.
    const errorMessage = parseResult.error.issues.map((issue) => issue.message).join('; ');
    throw new Error(errorMessage);
  }

  return parseResult.data;
}
