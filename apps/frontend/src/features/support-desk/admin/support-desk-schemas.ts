import { z } from 'zod';
import { ApiError } from '../../../shared/api';

// Hợp đồng dữ liệu của màn CSKH. Plan `tasks/support-desk/plan.md` ghi lại hợp đồng này cho SD-04, SD-05, SD-06.

const presenceStatusSchema = z.enum(['available', 'away']);

const staffPresenceSchema = z.object({
  staffId: z.string(),
  status: presenceStatusSchema,
  lastSeenAt: z.string(),
  // Bổ sung `displayName` theo quyết định ngày 2026-11-09. Trường này là chuỗi, `null` hoặc không có khi backend chưa hỗ trợ.
  displayName: z.string().nullable().optional(),
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

// Thông tin người dùng của phiên CSKH. Khách vãng lai có tên và email bằng `null`.
const customerInfoSchema = z.object({
  isGuest: z.boolean(),
  name: z.string().nullable(),
  email: z.string().nullable(),
});

// Đơn hàng gần nhất của khách hàng. Chuỗi giờ `createdAt` dùng `apiTimeSchema` để chặn định dạng sai.
const sessionCustomerOrderSchema = z.object({
  code: z.string(),
  status: z.string(),
  total: z.number(),
  itemCount: z.number(),
  createdAt: apiTimeSchema,
});

// Dữ liệu bảng thông tin khách. Cấu trúc gồm hồ sơ người dùng và danh sách tối đa 5 đơn hàng.
const sessionCustomerSchema = z.object({
  customer: customerInfoSchema,
  orders: z.array(sessionCustomerOrderSchema),
});

// Kết quả kết thúc phiên hỗ trợ. Máy chủ phản hồi mã phiên, thời điểm đóng và định danh người đóng.
const closedSessionSchema = z.object({
  sessionId: z.string(),
  closedAt: apiTimeSchema,
  closedBy: z.string(),
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
export type CustomerInfo = z.infer<typeof customerInfoSchema>;
export type SessionCustomerOrder = z.infer<typeof sessionCustomerOrderSchema>;
export type SessionCustomer = z.infer<typeof sessionCustomerSchema>;
export type ClosedSession = z.infer<typeof closedSessionSchema>;

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

// Đọc dữ liệu bảng thông tin khách. Hàm chuyển cấu trúc sai quy cách thành `ApiError`.
export function parseSessionCustomer(data: unknown): SessionCustomer {
  return parseApiData(sessionCustomerSchema, data);
}

// Đọc phản hồi kết thúc phiên. Hàm ném `ApiError` khi máy chủ trả dữ liệu sai quy cách.
export function parseClosedSession(data: unknown): ClosedSession {
  return parseApiData(closedSessionSchema, data);
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
