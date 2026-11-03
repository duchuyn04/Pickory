# Task List: support-desk

Plan và quyết định thiết kế: `tasks/support-desk/plan.md`. Spec: `spec/SPEC-support-desk.md`. Quy ước chung: `spec/SPEC.md`, `tasks/dotnet-migration/plan.md`, `CLAUDE.md`.

Ngày 2026-10-18 người dùng chọn làm `support-desk` độc lập: khách chat trực tiếp với CSKH ở `/support`, không qua bot. Chi tiết ở mục "Thay đổi phạm vi ngày 2026-10-18" của plan. Task bị bỏ giữ nguyên nội dung để tra cứu.

## Cách chia file

Từ ngày 2026-11-03, task chi tiết nằm ở ba file. Mỗi nhóm chỉ cần đọc file của nhánh mình và file hợp đồng.

| File | Nội dung | Ai đọc |
|---|---|---|
| `tasks/support-desk/todo.md` | Quy ước chung, Phase 1, checkpoint, E2E và nghiệm thu | Cả hai nhóm |
| `tasks/support-desk/todo-backend.md` | Task của nhánh backend | Nhóm backend |
| `tasks/support-desk/todo-frontend.md` | Task của nhánh frontend | Nhóm frontend |
| `tasks/support-desk/api-contract.md` | Dạng dữ liệu của mọi route và sự kiện SSE | Cả hai nhóm |

Bảng tiến độ chung là mục "Task List" của `tasks/support-desk/plan.md`.

Cách tick khi một task đạt:

1. Tick tiêu chí nghiệm thu trong file task của nhánh.
2. Tick dòng của task trong mục "Task List" của `plan.md`.
3. Sửa ô "Có từ" của route trong bảng tổng hợp của `api-contract.md`, khi task làm xong một route backend.

Mỗi task nằm trên một dòng riêng trong `plan.md`. Hai nhóm tick trên hai nhánh git khác nhau mà không đụng cùng một dòng, nên git gộp được mà không xung đột.

## Quy ước chung

- Mỗi task backend phải qua `dotnet build` và `dotnet test` trong `apps/backend` trước khi báo xong.
- Mỗi task frontend phải qua `pnpm lint && pnpm typecheck && pnpm test` trong `apps/frontend`.
- Mỗi test mới đi qua ít nhất một phép thử đột biến trên luật nó bảo vệ.
- Kích thước: XS 1 file, S 1-2, M 3-5, L 6-8. Số file không tính file test.
- Route trong các file task theo bảng REST ở quyết định 11 của plan. Người dùng chưa chốt bảng này (Câu hỏi mở 1).
- Không thêm package. Không đổi schema của module khác.

Quy ước làm song song (quyết định 31 của plan):
- Hợp đồng của mọi route và sự kiện còn lại nằm ở `tasks/support-desk/api-contract.md`.
- Dữ liệu mẫu (fixture) nằm ở `spec/fixtures/support-desk/`.
- Không đổi dạng dữ liệu trong code trước khi sửa hợp đồng. Sửa hợp đồng và dữ liệu mẫu trong cùng một commit.
- Hai nhánh gặp nhau ở SD-E2E1 và SD-E2E2. Hai task này chạy với backend thật.

---

## Phase 1: Nền tảng

### SD-01: Logic thuần
**Mô tả:** Bốn thành phần thuần, không gọi DB và không gọi HTTP. Ngày 2026-10-18 task bỏ `DraftSimilarity`, vì `SD-12` đã bỏ.
- `WorkingHoursCalendar.IsWithinHours(utcNow, settings)`: trong khung giờ và không phải ngày nghỉ, theo `Asia/Ho_Chi_Minh`.
- `WaitEstimator.Estimate(averageWait, waitingCount, availableStaff)`: số phút chờ dự kiến theo quyết định 9.
- `CannedResponseTemplate.Render(body, values)`: thay `{ten_khach}` và `{ma_don}`.
- `Percentile.Of(values, p)`: phân vị cho p50 và p90.

**Tiêu chí nghiệm thu:**
- [x] 7:59 và 21:00 giờ Việt Nam là ngoài giờ. 8:00 và 20:59 là trong giờ
- [x] Ngày nghỉ trong `holidays` là ngoài giờ cả ngày theo giờ Việt Nam
- [x] Đầu vào UTC lúc 00:30 ngày D là 7:30 ngày D giờ Việt Nam
- [x] Không có nhân viên sẵn sàng thì `WaitEstimator` không chia cho 0
- [x] Chưa có dữ liệu 7 ngày thì ước tính dùng 5 phút cho mỗi phiên
- [x] Biến không có giá trị giữ nguyên chuỗi `{ten_khach}`, không ném lỗi
- [x] `Percentile.Of` trả `null` với danh sách rỗng và đúng giá trị với danh sách 1 phần tử
**Kiểm tra:** unit test với `FakeTimeProvider`
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.SupportDesk/{Scheduling/WorkingHoursCalendar.cs, Scheduling/WaitEstimator.cs, CannedResponses/CannedResponseTemplate.cs, Metrics/Percentile.cs}`, test trong `apps/backend/tests/Pickory.UnitTests/Modules/SupportDesk/`

### SD-02: Khung module, Contracts và schema
**Mô tả:** Tạo `Pickory.Modules.SupportDesk` và `Pickory.Modules.SupportDesk.Contracts`. Thêm `SupportDeskDbContext` với schema lịch sử `ef_support_desk`, migration đầu tiên và `SupportDeskModule.cs`. Contracts có `ISupportMetrics`, `SupportMetricsSnapshot`, `SupportPendingCounts`. Ngày 2026-10-18 task bỏ bảng `draft_logs` và thêm hai bảng hội thoại theo quyết định 24.
**Tiêu chí nghiệm thu:**
- [x] Bảng `support_sessions`, `callback_requests`, `canned_responses`, `staff_presence`, `support_settings` theo Data Model của spec
- [x] Bảng `support_conversations` và `support_messages` theo quyết định 24
- [x] `support_sessions` có thêm `handoff_in_hours`, `customer_user_id`, `anonymous_id` theo quyết định 10
- [x] Chỉ mục unique một phần chặn hai phiên mở (`closed_at` rỗng) cho cùng `conversation_id`
- [x] Chỉ mục unique một phần chặn hai hội thoại `open` cho cùng `anonymous_id`
- [x] `support_settings` có một dòng mặc định: 8:00 đến 21:00 mỗi ngày, nhắc lại 10 phút, tự đóng 30 phút
- [x] Migration chạy trên DB sạch. Lệnh `e2e:start` và fixture test tích hợp dựng được schema mới
- [x] `Pickory.ArchitectureTests` chặn `SupportDesk` tham chiếu module khác ngoài project Contracts
- [x] `SupportDesk` không tham chiếu `Pickory.Modules.AiGateway.Contracts`
- [x] `Program.cs` chỉ gọi `AddSupportDeskModule` và `MapSupportDeskEndpoints`
**Kiểm tra:** build, test kiến trúc, test tích hợp schema
**Phụ thuộc:** SD-01 · **Quy mô:** M

### SD-03: Hub SSE nhân viên và trạng thái sẵn sàng
**Mô tả:** `SupportEventHub` giữ kết nối SSE của từng nhân viên. `GET /api/admin/support/events` mở luồng SSE. `PUT /api/admin/support/presence` đổi `available` hoặc `away`. `GET /api/admin/support/presence` trả nhân viên đang sẵn sàng. Job mỗi 30 giây chuyển `away` khi mất nhịp tim quá 2 phút.
**Tiêu chí nghiệm thu:**
- [x] `sales` gọi `GET /api/admin/support/events` nhận 403. Khách nhận 401
- [x] `PUT /api/admin/support/presence` cần `conversations:reply`. Giá trị khác `available` và `away` trả 400 `VALIDATION_ERROR`
- [x] Kết nối SSE gửi nhịp tim mỗi 30 giây và cập nhật `last_seen_at`
- [x] Ngắt kết nối hơn 2 phút thì job chuyển nhân viên sang `away` và phát sự kiện SSE cho người khác
- [x] Một nhân viên mở 2 tab: đóng 1 tab không chuyển `away`
- [x] Response SSE có `Content-Type: text/event-stream` và `Cache-Control: no-cache`
- [x] Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp; test gọi processor với `FakeTimeProvider`
**Phụ thuộc:** SD-02 · **Quy mô:** M

### SD-UI1: Khung màn CSKH, hàng chờ và công tắc sẵn sàng
**Mô tả:** Thay `AdminPlaceholderPage` ở `/admin/conversations` bằng màn CSKH 3 cột. Task này làm cột hàng chờ (tab Đang chờ, Của tôi, Gọi lại), client SSE, `PresenceToggle`, thông báo trình duyệt và âm báo. Hai cột còn lại là khung trống có tiêu đề.
**Tiêu chí nghiệm thu:**
- [x] Mục menu "CSKH" giữ đường dẫn `/admin/conversations`. Người thiếu `conversations:read` không thấy menu
- [x] Mở thẳng URL khi thiếu quyền thấy trang không đủ quyền
- [x] `PresenceToggle` hiện trên thanh trên cùng của `AdminLayout` khi người dùng có `conversations:reply`
- [x] Hội thoại mới vào hàng chờ hiện trong tab Đang chờ mà không tải lại trang
  Kiểm đầu cuối với backend thật ở SD-E2E1.
- [x] Badge trên tab Đang chờ khớp số phiên đang chờ
  Kiểm đầu cuối với backend thật ở SD-E2E1.
- [x] Trình duyệt chỉ hỏi quyền thông báo khi nhân viên bấm nút bật thông báo
- [x] Nhân viên tắt được âm báo. Lựa chọn còn sau khi tải lại trang
- [x] Mất SSE thì client tự kết nối lại và hiện trạng thái "Mất kết nối"
- [x] Dùng được bằng bàn phím. Tab có vai trò `tablist`
- [x] Ảnh chụp 1280 px và 375 px không tràn ngang
- [x] Playwright console có 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest cho hook SSE và bộ lọc tab; ảnh chụp; Playwright `--headed`
**Phụ thuộc:** SD-03 · **Quy mô:** M
**File:** `apps/frontend/src/features/support-desk/admin/`, `apps/frontend/src/app.tsx`

### Checkpoint SD1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `SupportDesk` tham chiếu module khác ngoài Contracts
- [ ] Nhân viên bật "Sẵn sàng" trên màn CSKH, mất kết nối 2 phút thì thành "Tạm vắng"
- [ ] Review với người dùng

---

## Phase 2: Hội thoại phía khách, tiếp nhận và trả lời

Task của phase này nằm ở `todo-backend.md` và `todo-frontend.md`. Hai nhánh chạy song song.

### Checkpoint SD2
- [ ] Khách gửi tin ở `/support`, nhân viên nhận, trả lời, kết thúc, khách chấm điểm trên `pnpm dev`
- [ ] Ngoài giờ, khách để lại số điện thoại và tab Gọi lại hiện yêu cầu
- [ ] Review với người dùng

---

## Phase 3: Cấu hình và số liệu

Task của phase này nằm ở `todo-backend.md` (SD-10, SD-13) và `todo-frontend.md` (SD-UI5).

### Checkpoint SD3
- [ ] `ISupportMetrics` trả đúng số đã tính tay trên bộ dữ liệu test
- [ ] Review với người dùng

---

## Phase 4: E2E và nghiệm thu

### SD-E2E1: Luồng CSKH trong giờ
**Mô tả:** E2E theo phạm vi mới, file `apps/frontend/e2e/support-desk-chat.spec.ts`. Test không cần AI. Ngày 2026-10-18 task bỏ bước chatbot và bước "Gợi ý trả lời".
**Tiêu chí nghiệm thu:**
- [ ] Khách vãng lai mở `/support` và gửi tin đầu tiên
- [ ] Nhân viên `support` đang sẵn sàng thấy hội thoại mới trong 3 giây
- [ ] Nhân viên nhận, chèn một mẫu bằng `/`, sửa, rồi bấm gửi
- [ ] Khách thấy tin của nhân viên trong 2 giây
- [ ] Nhân viên kết thúc phiên. Khách chấm điểm CSAT
- [ ] Khách gửi tin tiếp theo và hàng chờ của nhân viên hiện phiên chờ mới
- [ ] Console của cả hai trang có 0 lỗi và 0 cảnh báo
- [ ] Spec chạy với backend thật. File spec không có `page.route` cho route `/api/support/` hay `/api/admin/support/`
- [ ] Các tiêu chí "Kiểm đầu cuối ở SD-E2E1" của task UI đều đạt
**Kiểm tra:** `pnpm test:e2e` ở chế độ ẩn và `--headed`
**Phụ thuộc:** nhánh backend SD-04, SD-06, SD-07, SD-08, SD-09, SD-10; nhánh frontend SD-UI2, SD-UI3, SD-UI6, SD-UI7 · **Quy mô:** M

### SD-E2E2: Luồng ngoài giờ và hai người nhận
**Mô tả:** E2E file `apps/frontend/e2e/support-desk-callback.spec.ts`. Startup E2E đặt giờ làm việc để thời điểm chạy test nằm ngoài giờ.
**Tiêu chí nghiệm thu:**
- [ ] Khách gửi tin ở `/support` ngoài giờ và thấy thông báo ngoài giờ kèm form số điện thoại
- [ ] Khách gửi số điện thoại. Tab Gọi lại của nhân viên hiện yêu cầu
- [ ] Nhân viên đánh dấu "Đã gọi" và yêu cầu rời nhóm `pending`
- [ ] Hai nhân viên bấm nhận cùng một phiên: một người thấy "Đã có người nhận"
- [ ] Đăng nhập `sales` không thấy menu CSKH
- [ ] Spec chạy với backend thật. File spec không có `page.route` cho route `/api/support/` hay `/api/admin/support/`
- [ ] Các tiêu chí "Kiểm đầu cuối ở SD-E2E2" của task UI đều đạt
- [ ] `support-desk-workspace.spec.ts` gỡ `page.route` của `sessions` và `callback-requests`, và vẫn xanh
**Kiểm tra:** `pnpm test:e2e`
**Phụ thuộc:** nhánh backend SD-05, SD-06; nhánh frontend SD-UI4, SD-UI6, SD-UI7 · **Quy mô:** M

### SD-14: Nghiệm thu support-desk
**Mô tả:** Kiểm toàn module trước khi sang `admin-console`. Ngày 2026-10-18 task bỏ hai tiêu chí đo p95 của bản nháp AI.
**Tiêu chí nghiệm thu:**
- [ ] Ma trận quyền có mọi endpoint back-office của `support-desk`
- [ ] Coverage dòng của `Pickory.Modules.SupportDesk` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
- [ ] Mọi route phía khách có test chủ hội thoại trả 404 với người khác
**Kiểm tra:** `dotnet test`, script gộp coverage
**Phụ thuộc:** SD-E2E1, SD-E2E2, SD-13, SD-17 · **Quy mô:** S

## Checkpoint cuối
- [ ] Mọi dòng Traceability đạt
- [ ] Coverage dòng của `Pickory.Modules.SupportDesk` ít nhất 80%, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `admin-console`
