import { describe, expect, it } from 'vitest';
import {
  DESCRIPTION_MAX_NODE_COUNT,
  DESCRIPTION_MAX_TEXT_LENGTH,
  descriptionSchema,
} from '../../../src/catalog/description-schema';
import {
  descriptionToPlainText,
  plainTextToDescription,
} from '../../../src/catalog/description-text';

describe('descriptionSchema', () => {
  it('chấp nhận tài liệu hợp lệ gồm đoạn văn, tiêu đề cấp 1 đến 3, chữ đậm và chữ nghiêng', () => {
    // Chứng minh tài liệu chứa đủ các nút và mark hợp lệ theo danh sách trắng đều được chấp nhận.
    const validDoc = {
      type: 'doc',
      content: [
        {
          type: 'heading',
          attrs: { level: 1 },
          content: [{ type: 'text', text: 'Tiêu đề cấp 1' }],
        },
        {
          type: 'heading',
          attrs: { level: 2 },
          content: [{ type: 'text', text: 'Tiêu đề cấp 2' }],
        },
        {
          type: 'heading',
          attrs: { level: 3 },
          content: [{ type: 'text', text: 'Tiêu đề cấp 3' }],
        },
        {
          type: 'paragraph',
          content: [
            { type: 'text', text: 'Chữ thường ' },
            { type: 'text', text: 'chữ đậm ', marks: [{ type: 'bold' }] },
            { type: 'text', text: 'chữ nghiêng ', marks: [{ type: 'italic' }] },
            {
              type: 'text',
              text: 'vừa đậm vừa nghiêng',
              marks: [{ type: 'bold' }, { type: 'italic' }],
            },
          ],
        },
      ],
    };

    const parsed = descriptionSchema.safeParse(validDoc);
    expect(parsed.success).toBe(true);
  });

  it('chấp nhận tài liệu rỗng chỉ có một đoạn văn không có text', () => {
    // Bảo đảm người dùng chưa nhập nội dung vẫn lưu được bản nháp ban đầu.
    const emptyDoc = {
      type: 'doc',
      content: [{ type: 'paragraph' }],
    };

    const parsed = descriptionSchema.safeParse(emptyDoc);
    expect(parsed.success).toBe(true);
  });
  it('từ chối tài liệu thiếu mảng content hoặc có mảng content rỗng', () => {
    // ProseMirror yêu cầu nút doc phải chứa ít nhất một khối nội dung hợp lệ.
    const docWithoutContent = {
      type: 'doc',
    };
    expect(descriptionSchema.safeParse(docWithoutContent).success).toBe(false);

    const docWithEmptyContent = {
      type: 'doc',
      content: [],
    };
    expect(descriptionSchema.safeParse(docWithEmptyContent).success).toBe(false);
  });

  it('từ chối nút text có chuỗi text rỗng', () => {
    // ProseMirror cấm nút text rỗng để tránh suy biến cây cấu trúc văn bản.
    const docWithEmptyText = {
      type: 'doc',
      content: [
        {
          type: 'paragraph',
          content: [{ type: 'text', text: '' }],
        },
      ],
    };
    expect(descriptionSchema.safeParse(docWithEmptyText).success).toBe(false);
  });

  it('từ chối danh sách mark chứa các mark trùng lặp cùng một loại', () => {
    // ProseMirror quy định tập hợp mark trên một nút text phải là duy nhất.
    const docWithDuplicateMarks = {
      type: 'doc',
      content: [
        {
          type: 'paragraph',
          content: [
            {
              type: 'text',
              text: 'Chữ kiểm tra',
              marks: [{ type: 'bold' }, { type: 'bold' }],
            },
          ],
        },
      ],
    };
    expect(descriptionSchema.safeParse(docWithDuplicateMarks).success).toBe(false);
  });

  it('từ chối các nút ngoài danh sách trắng như script, image, bulletList, codeBlock', () => {
    // Ngăn chặn tấn công XSS và cấu trúc dữ liệu không được hệ thống hỗ trợ.
    const invalidNodes = [
      { type: 'script', content: [{ type: 'text', text: 'alert(1)' }] },
      { type: 'image', attrs: { src: 'https://example.com/evil.png' } },
      { type: 'bulletList', content: [] },
      { type: 'codeBlock', content: [{ type: 'text', text: 'const x = 1;' }] },
    ];

    for (const node of invalidNodes) {
      const doc = {
        type: 'doc',
        content: [node],
      };
      const parsed = descriptionSchema.safeParse(doc);
      expect(parsed.success).toBe(false);
    }
  });

  it('từ chối các mark ngoài danh sách trắng như link hoặc underline', () => {
    // Giới hạn định dạng rich text theo đúng phạm vi MVP đã phê duyệt.
    const invalidMarks = [
      [{ type: 'link', attrs: { href: 'https://example.com' } }],
      [{ type: 'underline' }],
    ];

    for (const marks of invalidMarks) {
      const doc = {
        type: 'doc',
        content: [
          {
            type: 'paragraph',
            content: [{ type: 'text', text: 'Nội dung', marks }],
          },
        ],
      };
      const parsed = descriptionSchema.safeParse(doc);
      expect(parsed.success).toBe(false);
    }
  });

  it('từ chối tiêu đề cấp 4', () => {
    // Kiểm soát cấu trúc phân cấp bài viết chỉ tối đa 3 cấp tiêu đề.
    const docWithH4 = {
      type: 'doc',
      content: [
        {
          type: 'heading',
          attrs: { level: 4 },
          content: [{ type: 'text', text: 'Tiêu đề quá sâu' }],
        },
      ],
    };

    const parsed = descriptionSchema.safeParse(docWithH4);
    expect(parsed.success).toBe(false);
  });

  it('từ chối các thuộc tính thừa trên mọi cấp nút và mark', () => {
    // Chặn dữ liệu rác hoặc mã độc chèn thuộc tính lạ vào cấu trúc JSON.
    const docsWithExtraAttrs = [
      {
        type: 'doc',
        extraField: 'malicious',
        content: [{ type: 'paragraph' }],
      },
      {
        type: 'doc',
        content: [
          {
            type: 'paragraph',
            extraAttr: 123,
          },
        ],
      },
      {
        type: 'doc',
        content: [
          {
            type: 'heading',
            attrs: { level: 2, className: 'danger' },
          },
        ],
      },
      {
        type: 'doc',
        content: [
          {
            type: 'paragraph',
            content: [{ type: 'text', text: 'A', extraProp: true }],
          },
        ],
      },
      {
        type: 'doc',
        content: [
          {
            type: 'paragraph',
            content: [
              {
                type: 'text',
                text: 'A',
                marks: [{ type: 'bold', extraMarkProp: 'test' }],
              },
            ],
          },
        ],
      },
    ];

    for (const doc of docsWithExtraAttrs) {
      const parsed = descriptionSchema.safeParse(doc);
      expect(parsed.success).toBe(false);
    }
  });

  it('từ chối tài liệu vượt quá 20.000 ký tự text', () => {
    // Giới hạn dung lượng text để bảo vệ bộ nhớ và tốc độ xử lý embedding.
    const longText = 'a'.repeat(DESCRIPTION_MAX_TEXT_LENGTH + 1);
    const doc = {
      type: 'doc',
      content: [
        {
          type: 'paragraph',
          content: [{ type: 'text', text: longText }],
        },
      ],
    };

    const parsed = descriptionSchema.safeParse(doc);
    expect(parsed.success).toBe(false);
  });

  it('chấp nhận tài liệu đạt đúng ngưỡng 20.000 ký tự text', () => {
    // Xác minh giá trị biên trên của độ dài text hợp lệ.
    const boundaryText = 'a'.repeat(DESCRIPTION_MAX_TEXT_LENGTH);
    const doc = {
      type: 'doc',
      content: [
        {
          type: 'paragraph',
          content: [{ type: 'text', text: boundaryText }],
        },
      ],
    };

    const parsed = descriptionSchema.safeParse(doc);
    expect(parsed.success).toBe(true);
  });

  it('từ chối tài liệu vượt quá 500 nút', () => {
    // Tránh quá tải cây DOM trình duyệt và kích thước bản ghi cơ sở dữ liệu.
    // 1 nút doc + 500 nút paragraph = 501 nút, vượt ngưỡng DESCRIPTION_MAX_NODE_COUNT.
    const paragraphs = Array.from({ length: DESCRIPTION_MAX_NODE_COUNT }, () => ({
      type: 'paragraph' as const,
    }));
    const doc = {
      type: 'doc',
      content: paragraphs,
    };

    const parsed = descriptionSchema.safeParse(doc);
    expect(parsed.success).toBe(false);
  });

  it('chấp nhận tài liệu đạt đúng ngưỡng 500 nút', () => {
    // Xác minh giá trị biên trên của tổng số nút hợp lệ trong tài liệu.
    // 1 nút doc + 499 nút paragraph = 500 nút, vừa đúng ngưỡng tối đa.
    const paragraphs = Array.from({ length: DESCRIPTION_MAX_NODE_COUNT - 1 }, () => ({
      type: 'paragraph' as const,
    }));
    const doc = {
      type: 'doc',
      content: paragraphs,
    };

    const parsed = descriptionSchema.safeParse(doc);
    expect(parsed.success).toBe(true);
  });
});

describe('descriptionToPlainText và plainTextToDescription', () => {
  it('descriptionToPlainText giữ thứ tự, cách nhau một dòng trống và bỏ mark', () => {
    // Tạo text sạch cho tác vụ AI embedding mà không dính cấu trúc định dạng.
    const doc = {
      type: 'doc' as const,
      content: [
        {
          type: 'heading' as const,
          attrs: { level: 1 as const },
          content: [{ type: 'text' as const, text: 'Tiêu đề sản phẩm' }],
        },
        {
          type: 'paragraph' as const,
          content: [
            { type: 'text' as const, text: 'Đoạn một có ' },
            {
              type: 'text' as const,
              text: 'chữ đậm',
              marks: [{ type: 'bold' as const }],
            },
            { type: 'text' as const, text: ' và ' },
            {
              type: 'text' as const,
              text: 'chữ nghiêng',
              marks: [{ type: 'italic' as const }],
            },
            { type: 'text' as const, text: '.' },
          ],
        },
        {
          type: 'heading' as const,
          attrs: { level: 2 as const },
          content: [{ type: 'text' as const, text: 'Thông số nổi bật' }],
        },
        {
          type: 'paragraph' as const,
          content: [{ type: 'text' as const, text: 'Đoạn kết thúc.' }],
        },
      ],
    };

    const plainText = descriptionToPlainText(doc);
    expect(plainText).toBe(
      'Tiêu đề sản phẩm\n\nĐoạn một có chữ đậm và chữ nghiêng.\n\nThông số nổi bật\n\nĐoạn kết thúc.',
    );
  });

  it('descriptionToPlainText trả chuỗi rỗng khi tài liệu không có chữ', () => {
    // Tránh sinh khoảng trắng thừa khi tài liệu chỉ chứa đoạn văn trống.
    const emptyDoc = {
      type: 'doc' as const,
      content: [{ type: 'paragraph' as const }],
    };

    expect(descriptionToPlainText(emptyDoc)).toBe('');
  });

  it('plainTextToDescription tách đoạn theo dòng trống và tạo tài liệu hợp lệ qua schema', () => {
    // Chuyển đổi dữ liệu seed dạng text thường thành cấu trúc Rich Text chuẩn.
    const input = 'Đoạn một\n\nĐoạn hai có nhiều câu.\n\nĐoạn ba';
    const doc = plainTextToDescription(input);

    const parsed = descriptionSchema.safeParse(doc);
    expect(parsed.success).toBe(true);
    expect(doc.content).toHaveLength(3);
    expect(doc.content?.[0]).toEqual({
      type: 'paragraph',
      content: [{ type: 'text', text: 'Đoạn một' }],
    });
  });

  it('plainTextToDescription tạo tài liệu rỗng hợp lệ khi nhận chuỗi trống', () => {
    // Đảm bảo dữ liệu rỗng không tạo ra tài liệu vi phạm schema.
    const doc = plainTextToDescription('   ');
    const parsed = descriptionSchema.safeParse(doc);
    expect(parsed.success).toBe(true);
    expect(doc.content).toHaveLength(1);
    expect(doc.content?.[0]?.type).toBe('paragraph');
  });

  it('hai hàm đổi qua lại giữ nguyên toàn bộ nội dung text', () => {
    // Đảm bảo không mất mát thông tin khi chuyển đổi hai chiều giữa text và JSON.
    const originalText = 'Dòng đầu tiên\n\nDòng thứ hai tiếp theo\n\nDòng kết';
    const doc = plainTextToDescription(originalText);
    const roundTripText = descriptionToPlainText(doc);

    expect(roundTripText).toBe(originalText);
  });

  it('plainTextToDescription ném lỗi khi text có 20.001 ký tự vượt giới hạn', () => {
    // Ngăn chặn tạo tài liệu vi phạm giới hạn độ dài ký tự tối đa.
    const longText = 'a'.repeat(DESCRIPTION_MAX_TEXT_LENGTH + 1);

    expect(() => plainTextToDescription(longText)).toThrow(
      `Mô tả không được vượt quá ${DESCRIPTION_MAX_TEXT_LENGTH} ký tự text.`,
    );
  });

  it('plainTextToDescription ném lỗi khi có từ 500 đoạn văn trở lên vượt giới hạn nút', () => {
    // Ngăn chặn tạo tài liệu phình to số lượng nút vượt ngưỡng cho phép.
    const manyParagraphs = Array.from({ length: 500 }, (_, index) => `Đoạn ${index + 1}`).join(
      '\n\n',
    );

    expect(() => plainTextToDescription(manyParagraphs)).toThrow(
      `Mô tả không được vượt quá ${DESCRIPTION_MAX_NODE_COUNT} nút.`,
    );
  });

  it('plainTextToDescription trả tài liệu qua schema khi text đúng ngưỡng 20.000 ký tự', () => {
    // Cho phép lưu trữ tài liệu chạm đúng giới hạn ký tự tối đa của hệ thống.
    const thresholdText = 'a'.repeat(DESCRIPTION_MAX_TEXT_LENGTH);
    const doc = plainTextToDescription(thresholdText);

    const parsed = descriptionSchema.safeParse(doc);
    expect(parsed.success).toBe(true);
  });

  it('plainTextToDescription trả tài liệu qua schema khi số đoạn văn nằm trong giới hạn nút', () => {
    // Cho phép lưu trữ tài liệu có số nút nằm trong ngưỡng an toàn cho phép.
    const validParagraphs = Array.from({ length: 100 }, (_, index) => `Đoạn ${index + 1}`).join(
      '\n\n',
    );
    const doc = plainTextToDescription(validParagraphs);

    const parsed = descriptionSchema.safeParse(doc);
    expect(parsed.success).toBe(true);
  });
});
