import { z } from 'zod';
import type { ContractExample } from '../exports-printing/contract-fixtures';

const successResponseSchema = z.object({ data: z.record(z.string(), z.unknown()) });

const errorResponseSchema = z.object({
  error: z.object({
    message: z.string(),
    details: z.array(z.object({ message: z.string() })).optional(),
  }),
});

const fulfillmentRequestSchema = z.object({
  toStatus: z.enum(['confirmed', 'shipping', 'delivered', 'returned']),
  reason: z.string(),
});

// Lấy phần `data` của ví dụ thành công. Schema kiểm hình dạng nên file mẫu hỏng báo lỗi ngay tại đây.
export function exampleData(example: ContractExample): Record<string, unknown> {
  return successResponseSchema.parse(example.response).data;
}

// Lấy khối `error` của ví dụ lỗi, cùng lý do như `exampleData`.
export function exampleError(example: ContractExample) {
  return errorResponseSchema.parse(example.response).error;
}

// Lấy body request của ví dụ fulfillment để test gọi mutation bằng đúng giá trị của file mẫu.
export function exampleFulfillmentRequest(example: ContractExample) {
  return fulfillmentRequestSchema.parse(example.request);
}
