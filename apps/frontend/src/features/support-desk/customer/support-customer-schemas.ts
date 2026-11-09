import { z } from 'zod';
import { ApiError } from '../../../shared/api';

// Hợp đồng dữ liệu của trang `/support`. Bảng trường nằm ở mục "Route phía khách" của `tasks/support-desk/api-contract.md`.

const sessionStatusSchema = z.enum(['waiting', 'assigned', 'closed']);

// Chuỗi giờ của API luôn có múi giờ. Chuỗi hỏng bị loại ở đây để bộ định dạng ngày không ném lỗi.
const apiTimeSchema = z.string().datetime({ offset: true });

export const supportSessionSchema = z.object({
  id: z.string(),
  status: sessionStatusSchema,
  // Điểm CSAT chỉ có ở phiên đã đóng. Phiên đang mở luôn trả `null`.
  csatScore: z.number().nullable(),
});

export const supportMessageSchema = z.object({
  id: z.string(),
  role: z.enum(['customer', 'staff', 'system']),
  body: z.string(),
  // Chỉ tin hệ thống có `action` khác `null`. Trang đặt chỗ form theo giá trị này.
  action: z.enum(['callback_form', 'csat_form']).nullable(),
  // Backend chưa trả khóa `staffDisplayName` tới khi SD-07 gộp. Khóa thiếu đọc thành `null`.
  staffDisplayName: z.preprocess(
    (value) => (value === undefined ? null : value),
    z.string().nullable(),
  ),
  createdAt: apiTimeSchema,
});

export const supportConversationSchema = z.object({
  id: z.string(),
  status: z.literal('open'),
  createdAt: apiTimeSchema,
  showRetentionNotice: z.boolean(),
  // Backend chưa trả khóa `session` tới khi SD-04 gộp. Khóa thiếu đọc thành `null`.
  session: z.preprocess(
    (value) => (value === undefined ? null : value),
    supportSessionSchema.nullable(),
  ),
});

const supportMessagePageSchema = z.object({
  items: z.array(supportMessageSchema),
  nextCursor: z.string().nullable(),
});

const sessionEventSchema = z.object({
  sessionId: z.string(),
});

export const supportCallbackRequestSchema = z.object({
  id: z.string(),
  status: z.literal('pending'),
  createdAt: apiTimeSchema,
});

export const supportCsatResponseSchema = z.object({
  sessionId: z.string(),
  score: z.number().int().min(1).max(5),
});

export type SupportSessionStatus = z.infer<typeof sessionStatusSchema>;
export type SupportSession = z.infer<typeof supportSessionSchema>;
export type SupportMessage = z.infer<typeof supportMessageSchema>;
export type SupportConversation = z.infer<typeof supportConversationSchema>;
export type SupportMessagePage = z.infer<typeof supportMessagePageSchema>;

export type SupportCustomerEvent =
  | { kind: 'message'; message: SupportMessage }
  | { kind: 'session'; status: SupportSessionStatus; sessionId: string };

export type SupportCallbackRequest = z.infer<typeof supportCallbackRequestSchema>;
export type SupportCsatResponse = z.infer<typeof supportCsatResponseSchema>;

// Dữ liệu sai hợp đồng thành `ApiError`, để trang hiện lỗi và nút "Thử lại" thay vì vỡ khi vẽ.
function parseApiData<Schema extends z.ZodType>(schema: Schema, data: unknown): z.infer<Schema> {
  const result = schema.safeParse(data);
  if (result.success) return result.data;
  throw new ApiError('INVALID_RESPONSE', 200, 'Không đọc được dữ liệu từ máy chủ. Hãy thử lại.');
}

export function parseSupportConversation(data: unknown): SupportConversation {
  return parseApiData(supportConversationSchema, data);
}

export function parseSupportMessage(data: unknown): SupportMessage {
  return parseApiData(supportMessageSchema, data);
}

export function parseSupportMessagePage(data: unknown): SupportMessagePage {
  return parseApiData(supportMessagePageSchema, data);
}

export function parseSupportCallbackRequest(data: unknown): SupportCallbackRequest {
  return parseApiData(supportCallbackRequestSchema, data);
}

export function parseSupportCsatResponse(data: unknown): SupportCsatResponse {
  return parseApiData(supportCsatResponseSchema, data);
}

// Sự kiện đến từ luồng SSE, không qua `apiRequest`. Sai dạng thì bỏ qua, không được làm vỡ trang.
export function parseSupportCustomerEvent(
  name: string,
  data: unknown,
): SupportCustomerEvent | null {
  if (name === 'message.created') {
    const result = supportMessageSchema.safeParse(data);
    if (!result.success) return null;
    return { kind: 'message', message: result.data };
  }
  if (name === 'session.waiting' || name === 'session.assigned' || name === 'session.closed') {
    const result = sessionEventSchema.safeParse(data);
    if (!result.success) return null;
    // Tên sự kiện khớp đúng một giá trị của `sessionStatusSchema`, nên ép kiểu này an toàn.
    const status = name.replace('session.', '') as SupportSessionStatus;
    return { kind: 'session', status, sessionId: result.data.sessionId };
  }
  return null;
}
