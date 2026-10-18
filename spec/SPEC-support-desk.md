# Spec: support-desk

> Đã cập nhật ngày 2026-10-18 theo phạm vi mới. Plan: `tasks/support-desk/plan.md`.

Module id: `support-desk` · Phụ thuộc: `identity`, `commerce` · Quy ước chung: `SPEC.md`

## Objective

Kênh chat giữa khách và nhân viên CSKH. Khách mở trang `/support`, gửi tin và chờ nhân viên nhận. Nhân viên nhận hội thoại, trả lời realtime và xem nhanh đơn hàng của khách. Module cũng quản lý giờ làm việc, yêu cầu gọi lại ngoài giờ, mẫu trả lời nhanh và số liệu chất lượng hỗ trợ.

Chatbot của Pickory là dự án riêng `chatbot-rag`. Câu chatbot không trả lời được thì fallback của chatbot dẫn khách tới `/support`. Module này không gọi chatbot và không gọi API AI.

**User stories**

*Nhân viên `support`*
- Tôi thấy hàng chờ hội thoại cần người thật, kèm thời gian chờ. Có hội thoại mới thì nhận được thông báo.
- Tôi bấm "Nhận" một hội thoại; người khác không nhận trùng được.
- Tôi đọc toàn bộ lịch sử chat và xem các đơn gần đây của khách.
- Tôi dùng mẫu câu trả lời nhanh, sửa lại rồi gửi.
- Tôi chuyển hội thoại cho đồng nghiệp, hoặc kết thúc phiên.
- Tôi xử lý danh sách khách để lại SĐT ngoài giờ, đánh dấu đã gọi.

*Khách hàng*
- Tôi mở `/support` từ chân trang, từ `/help` hoặc từ fallback của chatbot, rồi gửi tin cho CSKH. Tôi không phải nhập tên hay SĐT trước khi chat.
- Tôi biết đang chờ bao lâu. Ngoài giờ thì được để lại SĐT để được gọi lại.
- Kết thúc phiên hỗ trợ, tôi chấm điểm mức hài lòng.

*Admin*
- Tôi cấu hình giờ làm việc, ngày nghỉ, mẫu câu trả lời dùng chung.
- Tôi xem số liệu: số phiên hỗ trợ, thời gian phản hồi, điểm hài lòng.

## Thiết kế

### Giờ làm việc

- Mặc định 8:00 đến 21:00 hằng ngày, giờ `Asia/Ho_Chi_Minh`. Admin sửa được, thêm được ngày nghỉ (Tết, lễ).
- **Trong giờ** = trong khung giờ làm việc **và** có ít nhất 1 nhân viên `support` đang ở trạng thái "Sẵn sàng".

### Hội thoại phía khách

- Module tự lưu hội thoại và tin của khách.
- Chủ hội thoại: khách đăng nhập theo tài khoản, khách vãng lai theo cookie `pk_anon`. Người khác đọc hoặc gửi tin nhận 404, để không lộ hội thoại có tồn tại.
- Mỗi chủ có tối đa một hội thoại đang mở. Hội thoại im lặng 24 giờ thì tự đóng. Tin tiếp theo của khách tạo hội thoại mới.
- Khách vãng lai đăng nhập giữa chừng thì hội thoại đang mở gắn vào tài khoản (nghe `UserLinkedEvent`).
- Tin tối đa 2.000 ký tự, cho cả khách và nhân viên.
- Chống spam: mỗi khách gửi tối đa 10 tin mỗi phút và 200 tin mỗi ngày, đếm theo `anonymous_id`.
- Lần đầu tạo hội thoại, trang `/support` hiện câu báo hội thoại được lưu 90 ngày.
- Khách nhận tin của nhân viên và tin hệ thống qua SSE.

### Mở phiên chờ

Khách gửi tin khi hội thoại chưa có phiên mở thì module mở một phiên chờ:

| Tình huống | Tin hệ thống gửi vào khung chat của khách |
|---|---|
| Trong giờ | "Đã chuyển cho nhân viên CSKH. Thời gian chờ dự kiến khoảng N phút." (N tính từ thời gian chờ trung bình 7 ngày gần nhất và số hội thoại đang chờ) |
| Ngoài giờ, hoặc không có ai sẵn sàng | "Hiện ngoài giờ hỗ trợ (8:00 đến 21:00). Bạn để lại SĐT, CSKH sẽ gọi lại." kèm form SĐT trong khung chat |
| Chờ quá 10 phút trong giờ chưa ai nhận | Nhắc lại tới mọi nhân viên sẵn sàng; gửi khách lựa chọn để lại SĐT |

- Hai tin của khách đến cùng lúc chỉ mở một phiên.
- Form SĐT gửi tới `POST /api/support/callback-requests`, lưu trong bảng của `support-desk`, gắn `conversationId`.

### Nhận và xử lý hội thoại

- **Nhận:** cập nhật có điều kiện (chỉ nhận được khi chưa ai nhận), 2 người bấm cùng lúc thì 1 người nhận được, người kia thấy thông báo "Đã có người nhận".
- **Trả lời:** khách thấy tin qua SSE trong vòng 2 giây. Tên hiển thị là tên ngắn của nhân viên (ví dụ "Lan - CSKH").
- **Chuyển cho đồng nghiệp:** chọn người đang sẵn sàng, kèm ghi chú nội bộ (khách không thấy).
- **Kết thúc phiên:** khách nhận lời mời chấm điểm 1 đến 5 (CSAT). Tin tiếp theo của khách mở phiên chờ mới.
- **Tự đóng:** phiên không có tin nhắn nào trong 30 phút thì tự kết thúc.
- Nhận, chuyển, kết thúc đều ghi audit log.

### Bảng thông tin khách (bên cạnh khung chat)

- Khách đã đăng nhập: tên, email, các đơn gần đây (`ICommerceReader.GetOrdersForCustomerAsync`), link sang trang đơn trong back-office.
- Khách vãng lai: các đơn đặt từ cùng `anonymousId` (nếu có).
- **Không** hiện hồ sơ sở thích. CSKH không cần dữ liệu đó để hỗ trợ.

### Mẫu trả lời nhanh

- Gõ `/` trong ô soạn thảo để chọn mẫu, ví dụ `/camon`, `/baohanh`.
- Mẫu cá nhân: mỗi nhân viên tự tạo. Mẫu dùng chung: người có `support-settings:manage` quản lý.
- Mẫu hỗ trợ biến `{ten_khach}`, `{ma_don}`.

### Trạng thái nhân viên và thông báo

- Nhân viên chọn "Sẵn sàng" hoặc "Tạm vắng". Đăng xuất hoặc mất kết nối SSE quá 2 phút thì tự chuyển "Tạm vắng".
- Màn hình CSKH nhận sự kiện qua SSE: hội thoại mới vào hàng chờ, tin mới của khách, hội thoại được chuyển cho mình.
- Thông báo trình duyệt (Notification API, nhân viên cấp quyền) và âm báo, tắt được.

### Interface public

```csharp
// Pickory.Modules.SupportDesk.Contracts
public interface ISupportMetrics
{
    Task<SupportMetricsSnapshot> GetAsync(DateRange range, CancellationToken cancellationToken);
    // Cho badge của admin-console.
    Task<SupportPendingCounts> GetPendingCountsAsync(CancellationToken cancellationToken);
}

// SupportMetricsSnapshot: handoffs, handledInHours, callbacksRequested, callbacksCompleted,
// firstResponseSeconds (p50, p90), handleMinutes (p50, p90), csat (avg, count).
public sealed record SupportPendingCounts(int Waiting, int Callbacks);
```

Quyền `support-settings:manage`, `conversations:read`, `conversations:reply` nằm trong danh mục quyền chung của `identity`.

## API

Route theo REST mức tài nguyên, theo quyết định 11 của plan.

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| POST | `/api/support/conversations` | khách | Lấy hội thoại đang mở hoặc tạo mới |
| GET | `/api/support/conversations/{id}/messages` | chủ hội thoại | Lịch sử tin, phân trang theo con trỏ |
| POST | `/api/support/conversations/{id}/messages` | chủ hội thoại | Khách gửi tin |
| GET | `/api/support/conversations/{id}/events` | chủ hội thoại | SSE phía khách |
| POST | `/api/support/callback-requests` | chủ hội thoại | Khách để lại SĐT, giới hạn 3 lần/ngày |
| PUT | `/api/support/conversations/{id}/csat` | chủ hội thoại | Chấm điểm sau phiên |
| GET | `/api/admin/support/sessions?view=waiting\|mine` | `conversations:read` | Hàng chờ và hội thoại đang xử lý |
| GET | `/api/admin/support/events` | `conversations:read` | SSE cho màn hình CSKH |
| POST | `/api/admin/support/sessions/{id}/assignment` | `conversations:reply` | Nhận hội thoại (201, hoặc 409 khi đã có người nhận) |
| PUT | `/api/admin/support/sessions/{id}/assignment` | `conversations:reply` | Chuyển cho đồng nghiệp |
| PATCH | `/api/admin/support/sessions/{id}` | `conversations:reply` | Kết thúc phiên với `status: "closed"` |
| POST | `/api/admin/support/sessions/{id}/messages` | `conversations:reply` | Gửi tin (người đã nhận mới gửi được) |
| GET | `/api/admin/support/sessions/{id}/customer` | `conversations:read` | Bảng thông tin khách |
| GET · PUT | `/api/admin/support/presence` | `conversations:reply` | Danh sách nhân viên sẵn sàng; đổi trạng thái của mình |
| GET · PATCH | `/api/admin/support/callback-requests[/{id}]` | `conversations:read` · `conversations:reply` | Danh sách gọi lại, đánh dấu đã gọi |
| GET · POST · PATCH · DELETE | `/api/admin/support/canned-responses[/{id}]` | `conversations:reply` (mẫu cá nhân), `support-settings:manage` (mẫu chung) | Mẫu trả lời nhanh |
| GET · PUT | `/api/admin/support/settings` | `support-settings:manage` | Giờ làm việc, ngày nghỉ |

## Data Model (Prisma, rút gọn)

```prisma
model SupportConversation {
  id             String    @id @default(uuid())
  customerUserId String?
  anonymousId    String
  status         String    // open | closed
  createdAt      DateTime
  lastMessageAt  DateTime
  closedAt       DateTime?
  @@map("support_conversations")
}

model SupportMessage { /* conversationId, role(customer|staff|system), staffId?, body, action?, createdAt */ }

model SupportSession {
  id              String    @id @default(uuid())
  conversationId  String
  handoffAt       DateTime
  handoffInHours  Boolean
  customerUserId  String?
  anonymousId     String
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
model SupportSettings { /* workingHours Json, holidays Json, waitNudgeMinutes, autoCloseMinutes */ }
```

- `handoffAt` là lúc module mở phiên chờ.
- Ghi chú nội bộ khi chuyển phiên nằm ở `support_sessions`, không nằm ở `support_messages`.
- Hội thoại, tin và phiên bị xóa sau 90 ngày tính từ tin cuối, hoặc khi nhận `UserDeletedEvent`.
- SĐT trong `callback_requests` bị xóa sau 30 ngày, hoặc khi nhận `UserDeletedEvent`.

## Frontend

- Back-office `features/support-desk/admin/`, bố cục 3 cột:
  1. Hàng chờ: tab Đang chờ, Của tôi, Gọi lại.
  2. Khung chat: lịch sử có đánh dấu tin của khách, nhân viên, hệ thống; ô soạn gõ `/` để chọn mẫu.
  3. Thông tin khách và đơn hàng.
- Công tắc Sẵn sàng / Tạm vắng trên thanh trên cùng; badge số hội thoại đang chờ.
- Phía khách `features/support-desk/customer/`: trang `/support` có khung chat, tin hệ thống, form SĐT và form chấm điểm CSAT.
- Link "Liên hệ CSKH" tới `/support` ở chân trang và trên `/help`. Thanh đầu trang không có link này.

## Testing

- Unit: tính "trong giờ" (biên 8:00, 21:00, ngày nghỉ, giờ Việt Nam); ước tính thời gian chờ; thay biến trong mẫu; phân vị.
- Integration:
  - Tin đầu tiên của khách trong giờ và ngoài giờ ghi đúng tin hệ thống.
  - Hai tin của khách gửi cùng lúc chỉ mở một phiên.
  - 2 nhân viên nhận cùng lúc thì chỉ 1 người thành công.
  - Người chưa nhận hội thoại gửi tin bị từ chối 403.
  - Người không phải chủ hội thoại đọc hoặc gửi tin nhận 404.
  - Khách gửi vượt giới hạn tin nhận 429, và tin bị chặn không được lưu.
  - Phiên tự đóng sau 30 phút. Hội thoại tự đóng sau 24 giờ. Hội thoại cũ hơn 90 ngày bị xóa.
  - Ma trận quyền: `sales` (không có `conversations:read`) gọi API hàng chờ nhận 403.
- E2E, theo thứ tự:
  1. Khách mở `/support` và gửi tin.
  2. Nhân viên `support` thấy thông báo và nhận hội thoại.
  3. Nhân viên chèn mẫu, sửa rồi gửi. Khách thấy tin.
  4. Nhân viên kết thúc phiên. Khách chấm điểm.
  5. Tin tiếp theo của khách mở phiên chờ mới.

## Boundaries (riêng module)

- **Always:**
  - Ghi audit log khi nhận, chuyển và kết thúc phiên.
  - Tính giờ theo `Asia/Ho_Chi_Minh`.
  - Kiểm chủ hội thoại ở mọi route phía khách.
- **Ask first:** tích hợp kênh khác (Zalo OA, Messenger, tổng đài); thêm AI soạn nháp.
- **Never:**
  - Gửi SĐT hay tin của khách sang API AI.
  - Cho nhân viên chưa nhận hội thoại gửi tin vào hội thoại đó.
  - Xóa lịch sử hỗ trợ trước thời hạn lưu 90 ngày.

## Success Criteria

- [ ] Khách gửi tin đầu tiên ở `/support` trong giờ: nhân viên sẵn sàng nhận thông báo trong vòng 3 giây.
- [ ] Ngoài giờ: khách thấy thông báo ngoài giờ và form SĐT; yêu cầu gọi lại xuất hiện trong tab Gọi lại.
- [ ] 2 nhân viên bấm nhận cùng lúc: đúng 1 người nhận được.
- [ ] Tin nhân viên đến khách trong vòng 2 giây; sau khi kết thúc phiên, tin mới của khách mở phiên chờ mới.
- [ ] Người không phải chủ hội thoại không đọc, không gửi được tin.
- [ ] Khách gửi tin vượt giới hạn nhận 429.
- [ ] Hội thoại cũ hơn 90 ngày bị xóa cùng tin và phiên.
- [ ] Báo cáo hiện đúng thời gian phản hồi đầu tiên p50/p90 và CSAT trung bình với dữ liệu test.
- [ ] Coverage dòng của `Pickory.Modules.SupportDesk` ít nhất 80%, gộp unit và tích hợp.

## Phần đã bỏ ngày 2026-10-18

Chatbot chuyển sang dự án riêng `chatbot-rag`, Pickory bỏ `shopping-assistant` và gỡ `ai-gateway`. Các phần sau bị bỏ:

- Nhận chuyển giao từ chatbot qua `onHandoffRequested` và lý do chuyển giao: khách chat thẳng với CSKH ở `/support`.
- Ghi tin qua `conversations.*` của `shopping-assistant`: module tự lưu hội thoại và tin.
- "Bot hoạt động lại" sau khi kết thúc phiên: Pickory không còn bot trong khung chat CSKH.
- Tin của bot và nguồn bot đã dùng trong lịch sử chat: hội thoại không có tin của bot.
- Sản phẩm bot đã gợi ý và trang khách đang xem trong bảng thông tin khách: hai trường này đến từ `shopping-assistant`.
- AI soạn nháp (`support-desk.draft-reply`), route `/draft`, bảng `DraftLog` và chỉ số `draftUsageRate`: Pickory không gọi API AI.
- Hai tiêu chí về bản nháp AI (p95 dưới 8 giây, AI không tự gửi): không còn bản nháp AI.

## Open Questions

1. Thời gian chờ quá 10 phút thì nhắc lại, phiên không có tin trong 30 phút thì tự đóng: các mốc này có hợp không?
2. Có cần phân loại hội thoại (hỏi sản phẩm, khiếu nại, bảo hành) để báo cáo không? Mặc định: chưa.
3. Bảng route REST ở mục API theo quyết định 11 của plan. Người dùng chưa chốt bảng này (Câu hỏi mở 1 của plan).
4. Bảng thông tin khách cần cổng đọc tên, email và tên ngắn của nhân viên ở `identity`. Plan đề xuất `IUserDirectory`, chờ duyệt (Câu hỏi mở 2 của plan).
5. Đơn của khách vãng lai đọc qua `ICommerceReader.GetOrdersForAnonymousAsync` (Câu hỏi mở 3 của plan).
6. Phiên chờ ngoài giờ kéo dài tới khi nào. Plan đề xuất đóng phiên khi khách gửi SĐT, hoặc sau 30 phút (Câu hỏi mở 5 của plan).
7. Ước tính thời gian chờ khi chưa có dữ liệu: plan dùng 5 phút mỗi phiên (Câu hỏi mở 7 của plan).
8. Khách mở `/support` từ fallback của `chatbot-rag` thì nhân viên không thấy câu khách đã hỏi bot. Plan đề xuất MVP chưa chuyển ngữ cảnh (Câu hỏi mở 13 của plan).
