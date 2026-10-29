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
});

const callbackRequestSchema = z.object({
  id: z.string(),
  phone: z.string(),
  status: z.string(),
  createdAt: z.string(),
});

export type PresenceStatus = z.infer<typeof presenceStatusSchema>;
export type StaffPresence = z.infer<typeof staffPresenceSchema>;
export type PresenceChange = z.infer<typeof presenceChangeSchema>;
export type QueueSession = z.infer<typeof queueSessionSchema>;
export type CallbackRequest = z.infer<typeof callbackRequestSchema>;

// Dữ liệu sai hợp đồng thành `ApiError`, để cột hàng chờ hiện lỗi và nút "Thử lại" thay vì vỡ khi vẽ.
function parseApiData<Schema extends z.ZodType>(schema: Schema, data: unknown): z.infer<Schema> {
  const result = schema.safeParse(data);
  if (result.success) return result.data;
  throw new ApiError('INVALID_RESPONSE', 200, 'Phản hồi từ máy chủ không đúng hợp đồng.');
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

// Dữ liệu sự kiện đến từ luồng SSE, không qua `apiRequest`. Sai dạng thì bỏ qua, không được làm vỡ trang.
export function parsePresenceChange(data: unknown): PresenceChange | null {
  const result = presenceChangeSchema.safeParse(data);
  return result.success ? result.data : null;
}
