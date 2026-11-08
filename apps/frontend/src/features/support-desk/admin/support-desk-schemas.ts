import { z } from 'zod';
import { ApiError } from '../../../shared/api';

// Hợp đồng dữ liệu của màn CSKH. Plan `tasks/support-desk/plan.md` ghi lại hợp đồng này cho SD-04, SD-05, SD-06.

const presenceStatusSchema = z.enum(['available', 'away']);

const staffPresenceSchema = z.object({
  staffId: z.string(),
  status: presenceStatusSchema,
  lastSeenAt: z.string(),
});

const presenceChangeSchema = z.object({
  staffId: z.string(),
  status: presenceStatusSchema,
});

// Một phiên trong hàng chờ. `preview` là 200 ký tự đầu của tin đầu tiên.
const queueSessionSchema = z.object({
  id: z.string(),
  conversationId: z.string(),
  preview: z.string(),
  waitingSeconds: z.number(),
  handoffAt: z.string(),
  // `null` ở tab Đang chờ. Ghi chú nội bộ chỉ có sau khi đồng nghiệp chuyển phiên.
  claimedAt: z.string().nullable(),
  transferNote: z.string().nullable(),
});

const callbackRequestSchema = z.object({
  id: z.string(),
  conversationId: z.string(),
  // Job xóa số sau 30 ngày, nên `phone` có thể bằng `null`.
  phone: z.string().nullable(),
  note: z.string().nullable(),
  status: z.string(),
  createdAt: z.string(),
  handledAt: z.string().nullable(),
});

// Chuỗi giờ của API luôn có múi giờ. Chuỗi hỏng bị loại ở đây để bộ định dạng ngày không ném lỗi.
const apiTimeSchema = z.string().datetime({ offset: true });

// Một tin trong lịch sử của nhân viên. Là `Message` phía khách thêm `sessionId` theo hợp đồng của SD-UI2.
const staffViewMessageSchema = z.object({
  id: z.string(),
  // Tin trước phiên đầu tiên của hội thoại không thuộc phiên nào, nên hợp đồng cho phép `null`.
  sessionId: z.string().nullable(),
  role: z.enum(['customer', 'staff', 'system']),
  body: z.string(),
  // Nhân viên chỉ đọc nội dung tin hệ thống, không đặt form theo `action`.
  action: z.enum(['callback_form', 'csat_form']).nullable(),
  // Backend chưa trả khóa `staffDisplayName` tới khi SD-07 gộp. Khóa thiếu đọc thành `null`.
  staffDisplayName: z.preprocess(
    (value) => (value === undefined ? null : value),
    z.string().nullable(),
  ),
  createdAt: apiTimeSchema,
});

const staffMessagePageSchema = z.object({
  items: z.array(staffViewMessageSchema),
  nextCursor: z.string().nullable(),
});

// Kết quả nhận phiên. Route không có body, nên response là nơi duy nhất có `claimedAt`.
const sessionAssignmentSchema = z.object({
  sessionId: z.string(),
  staffId: z.string(),
  claimedAt: apiTimeSchema,
});

const cannedResponseSchema = z.object({
  id: z.string(),
  shortcut: z.string(),
  body: z.string(),
  shared: z.boolean(),
  editable: z.boolean(),
});

// Dữ liệu sự kiện `message.created` phía nhân viên. Client chỉ đọc lại lịch sử, không vẽ từ dữ liệu này.
const messageCreatedSchema = z.object({
  sessionId: z.string(),
  conversationId: z.string(),
});

// Ba sự kiện phiên đều mang `{ sessionId }` theo hợp đồng SSE phía nhân viên.
const sessionEventSchema = z.object({
  sessionId: z.string(),
});

export type PresenceStatus = z.infer<typeof presenceStatusSchema>;
export type StaffPresence = z.infer<typeof staffPresenceSchema>;
export type PresenceChange = z.infer<typeof presenceChangeSchema>;
export type QueueSession = z.infer<typeof queueSessionSchema>;
export type CallbackRequest = z.infer<typeof callbackRequestSchema>;
export type StaffViewMessage = z.infer<typeof staffViewMessageSchema>;
export type StaffMessagePage = z.infer<typeof staffMessagePageSchema>;
export type SessionAssignment = z.infer<typeof sessionAssignmentSchema>;
export type CannedResponse = z.infer<typeof cannedResponseSchema>;
export type MessageCreatedEvent = z.infer<typeof messageCreatedSchema>;

// Dữ liệu sai hợp đồng thành `ApiError`, để cột hàng chờ hiện lỗi và nút "Thử lại" thay vì vỡ khi vẽ.
function parseApiData<Schema extends z.ZodType>(schema: Schema, data: unknown): z.infer<Schema> {
  const result = schema.safeParse(data);
  if (result.success) return result.data;
  throw new ApiError('INVALID_RESPONSE', 200, 'Không đọc được dữ liệu từ máy chủ. Hãy thử lại.');
}

export function parseStaffPresence(data: unknown): StaffPresence {
  return parseApiData(staffPresenceSchema, data);
}

export function parseStaffPresenceList(data: unknown): StaffPresence[] {
  return parseApiData(z.array(staffPresenceSchema), data);
}

export function parseQueueSessions(data: unknown): QueueSession[] {
  return parseApiData(z.array(queueSessionSchema), data);
}

export function parseCallbackRequests(data: unknown): CallbackRequest[] {
  return parseApiData(z.array(callbackRequestSchema), data);
}

export function parseStaffViewMessage(data: unknown): StaffViewMessage {
  return parseApiData(staffViewMessageSchema, data);
}

export function parseStaffMessagePage(data: unknown): StaffMessagePage {
  return parseApiData(staffMessagePageSchema, data);
}

export function parseSessionAssignment(data: unknown): SessionAssignment {
  return parseApiData(sessionAssignmentSchema, data);
}

export function parseCannedResponses(data: unknown): CannedResponse[] {
  return parseApiData(z.array(cannedResponseSchema), data);
}

// Dữ liệu sự kiện đến từ luồng SSE, không qua `apiRequest`. Sai dạng thì bỏ qua, không được làm vỡ trang.
export function parseMessageCreatedEvent(data: unknown): MessageCreatedEvent | null {
  const result = messageCreatedSchema.safeParse(data);
  return result.success ? result.data : null;
}

// Sự kiện `session.closed` mang mã phiên. Sai dạng thì bỏ qua, để trang không vỡ vì một gói tin lạ.
export function parseSessionClosedEvent(data: unknown): { sessionId: string } | null {
  const result = sessionEventSchema.safeParse(data);
  return result.success ? result.data : null;
}

// Dữ liệu sự kiện đến từ luồng SSE, không qua `apiRequest`. Sai dạng thì bỏ qua, không được làm vỡ trang.
export function parsePresenceChange(data: unknown): PresenceChange | null {
  const result = presenceChangeSchema.safeParse(data);
  return result.success ? result.data : null;
}
