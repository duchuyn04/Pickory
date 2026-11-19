import { z } from 'zod';

// Chuỗi giờ của API luôn có múi giờ. Schema loại chuỗi hỏng để bộ định dạng ngày không ném lỗi.
const timestampSchema = z.string().datetime({ offset: true });

const ownReviewSchema = z.object({
  id: z.string(),
  // Điểm sao là số nguyên từ 1 đến 5.
  rating: z.number().int().min(1).max(5),
  body: z.string().nullable(),
  hidden: z.boolean(),
  createdAt: timestampSchema,
  updatedAt: timestampSchema,
  editableUntil: timestampSchema,
});

// Hợp đồng dữ liệu của `GET /api/products/{id}/reviews/mine`. Schema kiểm phần `data` đã bóc khỏi response.
export const myReviewStatusSchema = z.object({
  eligible: z.boolean(),
  // Backend chỉ trả hai mã lý do. Mã lạ báo lỗi để giao diện không hiện nhãn sai.
  reason: z.enum(['NOT_PURCHASED', 'ALREADY_REVIEWED']).nullable(),
  // Khách chưa viết đánh giá thì `review` là `null`. Backend luôn gửi khóa này.
  review: ownReviewSchema.nullable(),
});
