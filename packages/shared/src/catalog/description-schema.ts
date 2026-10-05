import { z } from 'zod';

// Giới hạn độ dài text để bảo vệ bộ nhớ và hiệu năng của tác vụ AI embedding.
export const DESCRIPTION_MAX_TEXT_LENGTH = 20_000;

// Giới hạn tổng số nút để tránh phình to cây DOM và tài liệu JSON lưu trữ.
export const DESCRIPTION_MAX_NODE_COUNT = 500;

// Chỉ chấp nhận mark đậm chuẩn của Tiptap. Chặn thuộc tính thừa để chống ô nhiễm dữ liệu.
export const boldMarkSchema = z
  .object({
    type: z.literal('bold'),
  })
  .strict();

// Chỉ chấp nhận mark nghiêng chuẩn của Tiptap. Chặn thuộc tính thừa để chống ô nhiễm dữ liệu.
export const italicMarkSchema = z
  .object({
    type: z.literal('italic'),
  })
  .strict();

// Gom các loại mark được phép sử dụng trong phạm vi MVP.
export const descriptionMarkSchema = z.discriminatedUnion('type', [
  boldMarkSchema,
  italicMarkSchema,
]);

// Nút text chứa chuỗi hiển thị và danh sách mark định dạng tùy chọn.
export const textNodeSchema = z
  .object({
    type: z.literal('text'),
    text: z.string(),
    marks: z.array(descriptionMarkSchema).optional(),
  })
  .strict();

// Giới hạn tiêu đề chỉ từ cấp 1 đến 3 để bảo đảm cấu trúc phân cấp giao diện.
export const headingAttrsSchema = z
  .object({
    level: z.union([z.literal(1), z.literal(2), z.literal(3)]),
  })
  .strict();

// Nút tiêu đề gồm thuộc tính cấp độ và nội dung text con.
export const headingNodeSchema = z
  .object({
    type: z.literal('heading'),
    attrs: headingAttrsSchema,
    content: z.array(textNodeSchema).optional(),
  })
  .strict();

// Nút đoạn văn chứa danh sách nút text con hoặc để trống khi chưa nhập chữ.
export const paragraphNodeSchema = z
  .object({
    type: z.literal('paragraph'),
    content: z.array(textNodeSchema).optional(),
  })
  .strict();

// Gom các khối nội dung cấp block hợp lệ bên trong tài liệu.
export const descriptionBlockNodeSchema = z.discriminatedUnion('type', [
  paragraphNodeSchema,
  headingNodeSchema,
]);

// Schema gốc kiểm tra toàn bộ cấu trúc tài liệu mô tả sản phẩm.
export const descriptionDocSchema = z
  .object({
    type: z.literal('doc'),
    content: z.array(descriptionBlockNodeSchema).optional(),
  })
  .strict()
  .superRefine((doc, ctx) => {
    // Đếm nút doc gốc là 1 nút đầu tiên trong cây tài liệu.
    let nodeCount = 1;
    let textLength = 0;

    if (doc.content) {
      for (const block of doc.content) {
        // Tăng đếm cho mỗi nút block cấp một gồm đoạn văn hoặc tiêu đề.
        nodeCount += 1;
        if (block.content) {
          for (const inline of block.content) {
            // Tăng đếm cho mỗi nút text nội dòng bên trong khối.
            nodeCount += 1;
            // Cộng dồn độ dài ký tự thực tế của toàn bộ bài viết.
            textLength += inline.text.length;
          }
        }
      }
    }

    // Từ chối khi tổng số ký tự vượt ngưỡng cấu hình tối đa.
    if (textLength > DESCRIPTION_MAX_TEXT_LENGTH) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: `Mô tả không được vượt quá ${DESCRIPTION_MAX_TEXT_LENGTH} ký tự text.`,
      });
    }

    // Từ chối khi tổng số nút vượt ngưỡng quy định để bảo vệ hiệu năng hiển thị.
    if (nodeCount > DESCRIPTION_MAX_NODE_COUNT) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: `Mô tả không được vượt quá ${DESCRIPTION_MAX_NODE_COUNT} nút.`,
      });
    }
  });

export const descriptionSchema = descriptionDocSchema;

export type DescriptionMark = z.infer<typeof descriptionMarkSchema>;
export type DescriptionTextNode = z.infer<typeof textNodeSchema>;
export type DescriptionHeadingAttrs = z.infer<typeof headingAttrsSchema>;
export type DescriptionHeadingNode = z.infer<typeof headingNodeSchema>;
export type DescriptionParagraphNode = z.infer<typeof paragraphNodeSchema>;
export type DescriptionBlockNode = z.infer<typeof descriptionBlockNodeSchema>;
export type DescriptionDoc = z.infer<typeof descriptionDocSchema>;
