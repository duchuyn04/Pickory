# Spec: support-desk

Module id: `support-desk` · Phụ thuộc: `identity`, `commerce`, `shopping-assistant`, `knowledge-base`, `ai-gateway` · Quy ước chung: `SPEC.md`

## Objective

Bàn làm việc của nhân viên CSKH: nhận các hội thoại chatbot chuyển sang, trả lời khách realtime trong cùng khung chat, xem nhanh đơn hàng của khách, và được AI soạn nháp câu trả lời dựa trên kho tri thức. Module cũng quản lý giờ làm việc, yêu cầu gọi lại ngoài giờ, và số liệu chất lượng hỗ trợ.

**User stories**

*Nhân viên `support`*
- Tôi thấy hàng chờ hội thoại cần người thật, có lý do chuyển và thời gian chờ. Có hội thoại mới thì nhận được thông báo.
- Tôi bấm "Nhận" một hội thoại; người khác không nhận trùng được.
- Tôi đọc toàn bộ lịch sử chat (cả phần bot đã trả lời và nguồn bot đã dùng), xem các đơn gần đây của khách.
- Tôi bấm "Gợi ý trả lời" để AI soạn nháp, sửa lại rồi gửi. Tôi cũng dùng được mẫu câu trả lời nhanh.
- Tôi chuyển hội thoại cho đồng nghiệp, hoặc kết thúc phiên để bot hoạt động lại.
- Tôi xử lý danh sách khách để lại SĐT ngoài giờ, đánh dấu đã gọi.

*Khách hàng*
- Khi chuyển sang nhân viên, tôi biết đang chờ bao lâu. Ngoài giờ thì được để lại SĐT để được gọi lại.
- Kết thúc phiên hỗ trợ, tôi chấm điểm mức hài lòng.

*Admin*
- Tôi cấu hình giờ làm việc, ngày nghỉ, mẫu câu trả lời dùng chung.
- Tôi xem số liệu: số hội thoại chuyển sang người, thời gian phản hồi, điểm hài lòng.

## Thiết kế

### Giờ làm việc

- Mặc định 8:00 đến 21:00 hằng ngày, giờ `Asia/Ho_Chi_Minh`. Admin sửa được, thêm được ngày nghỉ (Tết, lễ).
- **Trong giờ** = trong khung giờ làm việc **và** có ít nhất 1 nhân viên `support` đang ở trạng thái "Sẵn sàng".

### Tiếp nhận chuyển giao

`support-desk` nghe `onHandoffRequested` từ `shopping-assistant`:

| Tình huống | Tin hệ thống gửi vào khung chat của khách |
|---|---|
| Trong giờ | "Đã chuyển cho nhân viên CSKH. Thời gian chờ dự kiến khoảng N phút." (N tính từ thời gian chờ trung bình 7 ngày gần nhất và số hội thoại đang chờ) |
| Ngoài giờ, hoặc không có ai sẵn sàng | "Hiện ngoài giờ hỗ trợ (8:00 đến 21:00). Bạn để lại SĐT, CSKH sẽ gọi lại." kèm form SĐT trong khung chat |
| Chờ quá 10 phút trong giờ chưa ai nhận | Nhắc lại tới mọi nhân viên sẵn sàng; gửi khách lựa chọn để lại SĐT |

- Tin hệ thống được ghi bằng `conversations.appendSystemMessage` của `shopping-assistant`. *(bổ sung vào spec `shopping-assistant`)*
- Form SĐT gửi tới `POST /api/support/callback-requests`, lưu trong bảng của `support-desk`, gắn `conversationId`. SĐT không bao giờ được gửi sang LLM.

### Nhận và xử lý hội thoại

- **Nhận:** cập nhật có điều kiện (chỉ nhận được khi chưa ai nhận), 2 người bấm cùng lúc thì 1 người nhận được, người kia thấy thông báo "Đã có người nhận". Gọi `conversations.assign`, trạng thái thành `human`.
- **Trả lời:** `conversations.appendStaffMessage`, khách thấy qua SSE trong vòng 2 giây. Tên hiển thị là tên ngắn của nhân viên (ví dụ "Lan - CSKH").
- **Chuyển cho đồng nghiệp:** chọn người đang sẵn sàng, kèm ghi chú nội bộ (khách không thấy).
- **Kết thúc phiên:** `conversations.closeHumanSession`, bot hoạt động lại. Khách nhận lời mời chấm điểm 1 đến 5 (CSAT).
- **Tự đóng:** phiên `human` không có tin nhắn nào trong 30 phút thì tự kết thúc.
- Nhận, chuyển, kết thúc đều ghi `recordAudit`.

### Bảng thông tin khách (bên cạnh khung chat)

- Khách đã đăng nhập: tên, email, các đơn gần đây (`commerceReader.getOrdersForCustomer`), link sang trang đơn trong back-office.
- Khách vãng lai: các đơn đặt từ cùng `anonymousId` (nếu có).
- Sản phẩm bot đã gợi ý trong hội thoại; trang khách đang xem.
- **Không** hiện hồ sơ sở thích. CSKH không cần dữ liệu đó để hỗ trợ.

### AI soạn nháp (`support-desk.draft-reply`)

- Chỉ chạy khi nhân viên bấm "Gợi ý trả lời", không tự chạy theo mỗi tin nhắn, để tiết kiệm chi phí.
- Đầu vào (đều đã qua `redactPII`):
  - 10 tin gần nhất.
  - Các đoạn tài liệu từ `knowledge.retrieve` với `audience: 'staff'` (gồm cả tài liệu `internal` như quy trình khiếu nại).
  - Tóm tắt đơn hàng liên quan: mã, trạng thái, sản phẩm, ngày; không có SĐT, địa chỉ.
- Đầu ra: bản nháp kèm danh sách nguồn đã dùng. Bản nháp hiện trong ô soạn thảo, nhân viên sửa rồi bấm gửi. **AI không bao giờ tự gửi.**
- Ghi lại nhân viên có dùng bản nháp không, và sửa nhiều hay ít (độ giống giữa bản nháp và tin đã gửi), để đánh giá chất lượng.
- Ưu tiên thấp trong ngân sách AI. Hết ngân sách thì nút báo "Tạm không khả dụng".

### Mẫu trả lời nhanh

- Gõ `/` trong ô soạn thảo để chọn mẫu, ví dụ `/camon`, `/baohanh`.
- Mẫu cá nhân: mỗi nhân viên tự tạo. Mẫu dùng chung: người có `support-settings:manage` quản lý.
- Mẫu hỗ trợ biến `{ten_khach}`, `{ma_don}`.

### Trạng thái nhân viên và thông báo

- Nhân viên chọn "Sẵn sàng" hoặc "Tạm vắng". Đăng xuất hoặc mất kết nối SSE quá 2 phút thì tự chuyển "Tạm vắng".
- Màn hình CSKH nhận sự kiện qua SSE: hội thoại mới vào hàng chờ, tin mới của khách, hội thoại được chuyển cho mình.
- Thông báo trình duyệt (Notification API, nhân viên cấp quyền) và âm báo, tắt được.

### Interface public

```ts
// apps/backend/src/modules/support-desk/index.ts
export const supportMetrics: {
  get(range: DateRange): Promise<{
    handoffs: number;
    handledInHours: number;
    callbacksRequested: number;
    callbacksCompleted: number;
    firstResponseSeconds: { p50: number; p90: number };
    handleMinutes: { p50: number; p90: number };
    csat: { avg: number; count: number };
    draftUsageRate: number;          // tỉ lệ tin gửi đi có dùng bản nháp AI
  }>;
  pendingCount(): Promise<{ waiting: number; callbacks: number }>;   // cho badge của admin-console
};
export { supportPermissions } from './permissions';   // ['support-settings:manage']
```

## API

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| POST | `/api/support/callback-requests` | chủ hội thoại | Khách để lại SĐT, giới hạn 3 lần/ngày |
| POST | `/api/support/conversations/:id/csat` | chủ hội thoại | Chấm điểm sau phiên |
| GET | `/api/admin/support/queue` | `conversations:read` | Hàng chờ và hội thoại đang xử lý |
| GET | `/api/admin/support/events` | `conversations:read` | SSE cho màn hình CSKH |
| POST | `/api/admin/support/conversations/:id/claim` | `conversations:reply` | Nhận hội thoại |
| POST | `/api/admin/support/conversations/:id/messages` | `conversations:reply` | Gửi tin (người đã nhận mới gửi được) |
| POST | `/api/admin/support/conversations/:id/draft` | `conversations:reply` | AI soạn nháp |
| POST | `/api/admin/support/conversations/:id/transfer` | `conversations:reply` | Chuyển cho đồng nghiệp |
| POST | `/api/admin/support/conversations/:id/close` | `conversations:reply` | Kết thúc phiên |
| PUT | `/api/admin/support/presence` | `conversations:reply` | Sẵn sàng, tạm vắng |
| GET · PATCH | `/api/admin/support/callbacks[/:id]` | `conversations:read` · `conversations:reply` | Danh sách gọi lại, đánh dấu đã gọi |
| GET · POST · PATCH · DELETE | `/api/admin/support/canned-responses[/:id]` | `conversations:reply` (mẫu cá nhân), `support-settings:manage` (mẫu chung) | Mẫu trả lời nhanh |
| GET · PUT | `/api/admin/support/settings` | `support-settings:manage` | Giờ làm việc, ngày nghỉ |

## Data Model (Prisma, rút gọn)

```prisma
model SupportSession {
  id              String    @id @default(uuid())
  conversationId  String
  handoffAt       DateTime
  handoffReason   String
  staffId         String?
  claimedAt       DateTime?
  firstResponseAt DateTime?
  closedAt        DateTime?
  closedBy        String?   // staffId | 'auto'
  csat            Int?
  @@index([closedAt, handoffAt])
  @@map("support_sessions")
}

model CallbackRequest { /* conversationId, phone, note, status(pending|done|unreachable), handledBy, createdAt, handledAt */ }
model CannedResponse  { /* ownerId?, shortcut, body, shared Boolean */ }
model StaffPresence   { /* staffId @id, status(available|away), lastSeenAt */ }
model DraftLog        { /* sessionId, staffId, draft, sources Json, sentMessageId?, similarity Float?, createdAt */ }
model SupportSettings { /* workingHours Json, holidays Json, waitNudgeMinutes, autoCloseMinutes */ }
```

- SĐT trong `callback_requests` bị xóa sau 30 ngày, hoặc khi nhận `UserDeletedEvent`.

## Frontend

- Back-office `features/support-desk/admin/`, bố cục 3 cột:
  1. Hàng chờ: tab Đang chờ, Của tôi, Gọi lại.
  2. Khung chat: lịch sử có đánh dấu tin của bot, khách, nhân viên, hệ thống; ô soạn có nút "Gợi ý trả lời" và gõ `/` để chọn mẫu.
  3. Thông tin khách và đơn hàng.
- Công tắc Sẵn sàng / Tạm vắng trên thanh trên cùng; badge số hội thoại đang chờ.
- Phía khách: tin hệ thống và form SĐT trong khung chat (render bởi widget của `shopping-assistant`), form chấm điểm CSAT.

## Testing

- Unit: tính "trong giờ" (biên 8:00, 21:00, ngày nghỉ, giờ Việt Nam); ước tính thời gian chờ; thay biến trong mẫu; độ giống bản nháp và tin đã gửi.
- Integration:
  - `onHandoffRequested` trong giờ và ngoài giờ ghi đúng tin hệ thống.
  - 2 nhân viên nhận cùng lúc thì chỉ 1 người thành công.
  - Người chưa nhận hội thoại gửi tin bị từ chối 403.
  - Phiên tự đóng sau 30 phút.
  - Bản nháp dùng tài liệu `internal` nhưng không chứa SĐT hay địa chỉ của khách (fake provider ghi lại request).
  - Ma trận quyền: `sales` (không có `conversations:read`) gọi API hàng chờ nhận 403.
- E2E: khách yêu cầu gặp nhân viên → nhân viên `support` thấy thông báo → nhận → bấm gợi ý trả lời → sửa và gửi → khách thấy tin → nhân viên kết thúc → khách chấm điểm → bot trả lời lại tin tiếp theo.

## Boundaries (riêng module)

- **Always:** để nhân viên là người bấm gửi; ghi audit log khi nhận, chuyển, kết thúc; tính giờ theo `Asia/Ho_Chi_Minh`.
- **Ask first:** cho AI tự gửi câu trả lời; hiện hồ sơ sở thích cho CSKH; tích hợp kênh khác (Zalo OA, Messenger, tổng đài).
- **Never:** gửi SĐT khách để lại sang LLM; cho nhân viên chưa nhận hội thoại gửi tin vào đó; xóa lịch sử hỗ trợ trước thời hạn lưu của hội thoại.

## Success Criteria

- [ ] Khách yêu cầu gặp nhân viên trong giờ: nhân viên sẵn sàng nhận thông báo trong vòng 3 giây.
- [ ] Ngoài giờ: khách thấy thông báo ngoài giờ và form SĐT; yêu cầu gọi lại xuất hiện trong tab Gọi lại.
- [ ] 2 nhân viên bấm nhận cùng lúc: đúng 1 người nhận được.
- [ ] Tin nhân viên đến khách trong vòng 2 giây; sau khi kết thúc phiên, bot trả lời lại.
- [ ] "Gợi ý trả lời" cho câu hỏi về chính sách đổi trả trả bản nháp có nguồn từ kho tri thức, trong dưới 8 giây (p95).
- [ ] Không có bản nháp nào được gửi mà nhân viên không bấm gửi.
- [ ] Báo cáo hiện đúng thời gian phản hồi đầu tiên p50/p90, CSAT trung bình, tỉ lệ dùng bản nháp với dữ liệu test.
- [ ] Coverage `service.ts` ≥ 80%.

## Open Questions

1. Thời gian chờ quá 10 phút thì nhắc lại, phiên không có tin trong 30 phút thì tự đóng: các mốc này có hợp không?
2. Có cần phân loại hội thoại (hỏi sản phẩm, khiếu nại, bảo hành) để báo cáo không? Mặc định: chưa.
