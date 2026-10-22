# Task List: support-desk

Plan và quyết định thiết kế: `tasks/support-desk/plan.md`. Spec: `spec/SPEC-support-desk.md`. Quy ước chung: `spec/SPEC.md`, `tasks/dotnet-migration/plan.md`, `CLAUDE.md`.

Ngày 2026-10-18 người dùng chọn làm `support-desk` độc lập: khách chat trực tiếp với CSKH ở `/support`, không qua bot. Chi tiết ở mục "Thay đổi phạm vi ngày 2026-10-18" của plan. Task bị bỏ giữ nguyên nội dung để tra cứu.

Quy ước:
- Mỗi task backend phải qua `dotnet build` và `dotnet test` trong `apps/backend` trước khi báo xong.
- Mỗi task frontend phải qua `pnpm lint && pnpm typecheck && pnpm test` trong `apps/frontend`.
- Task back-office thêm endpoint mới vào `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`.
- Mỗi test mới đi qua ít nhất một phép thử đột biến trên luật nó bảo vệ.
- Task UI theo `docs/design/design-system.md`. Kiểm Playwright console có 0 lỗi và 0 cảnh báo.
- Kích thước: XS 1 file, S 1-2, M 3-5, L 6-8. Số file không tính file test.
- Route trong file này theo bảng REST ở quyết định 11 của plan. Người dùng chưa chốt bảng này (Câu hỏi mở 1).
- Không thêm package. Không đổi schema của module khác.

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
- [ ] `sales` gọi `GET /api/admin/support/events` nhận 403. Khách nhận 401
- [ ] `PUT /api/admin/support/presence` cần `conversations:reply`. Giá trị khác `available` và `away` trả 400 `VALIDATION_ERROR`
- [ ] Kết nối SSE gửi nhịp tim mỗi 30 giây và cập nhật `last_seen_at`
- [ ] Ngắt kết nối hơn 2 phút thì job chuyển nhân viên sang `away` và phát sự kiện SSE cho người khác
- [ ] Một nhân viên mở 2 tab: đóng 1 tab không chuyển `away`
- [ ] Response SSE có `Content-Type: text/event-stream` và `Cache-Control: no-cache`
- [ ] Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp; test gọi processor với `FakeTimeProvider`
**Phụ thuộc:** SD-02 · **Quy mô:** M

### SD-UI1: Khung màn CSKH, hàng chờ và công tắc sẵn sàng
**Mô tả:** Thay `AdminPlaceholderPage` ở `/admin/conversations` bằng màn CSKH 3 cột. Task này làm cột hàng chờ (tab Đang chờ, Của tôi, Gọi lại), client SSE, `PresenceToggle`, thông báo trình duyệt và âm báo. Hai cột còn lại là khung trống có tiêu đề.
**Tiêu chí nghiệm thu:**
- [ ] Mục menu "CSKH" giữ đường dẫn `/admin/conversations`. Người thiếu `conversations:read` không thấy menu
- [ ] Mở thẳng URL khi thiếu quyền thấy trang không đủ quyền
- [ ] `PresenceToggle` hiện trên thanh trên cùng của `AdminLayout` khi người dùng có `conversations:reply`
- [ ] Hội thoại mới vào hàng chờ hiện trong tab Đang chờ mà không tải lại trang
- [ ] Badge trên tab Đang chờ khớp số phiên đang chờ
- [ ] Trình duyệt chỉ hỏi quyền thông báo khi nhân viên bấm nút bật thông báo
- [ ] Nhân viên tắt được âm báo. Lựa chọn còn sau khi tải lại trang
- [ ] Mất SSE thì client tự kết nối lại và hiện trạng thái "Mất kết nối"
- [ ] Dùng được bằng bàn phím. Tab có vai trò `tablist`
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright console có 0 lỗi và 0 cảnh báo
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

### SD-15: Hội thoại và tin của khách
**Mô tả:** Route phía khách theo quyết định 11, 13, 25, 27, 28 và 29 của plan.
- `POST /api/support/conversations` lấy hội thoại `open` của người gọi hoặc tạo mới.
- `GET /api/support/conversations/{id}/messages` trả lịch sử.
- `POST /api/support/conversations/{id}/messages` lưu tin vai `customer`.

Module nghe `UserLinkedEvent` để gắn hội thoại vãng lai vào tài khoản.
**Tiêu chí nghiệm thu:**
- [ ] Lần đầu trả 201 kèm hội thoại mới trạng thái `open`. Lần sau trả 200 kèm hội thoại đang mở
- [ ] Response lần đầu có cờ `showRetentionNotice` bằng `true`. Lần sau cờ bằng `false`
- [ ] Khách vãng lai khác `pk_anon` đọc lịch sử hoặc gửi tin nhận 404 `SUPPORT_CONVERSATION_NOT_FOUND`
- [ ] Khách đăng nhập khác tài khoản nhận 404 `SUPPORT_CONVERSATION_NOT_FOUND`
- [ ] Khách vãng lai đăng nhập sau: `UserLinkedEvent` gắn hội thoại, và khách thấy hội thoại cũ bằng tài khoản
- [ ] Tin rỗng hoặc dài hơn 2.000 ký tự trả 400 `VALIDATION_ERROR`
- [ ] Gửi tin vào hội thoại `closed` trả 409 `SUPPORT_CONVERSATION_CLOSED`
- [ ] Gửi vượt giới hạn của quyết định 27 trả 429 `SUPPORT_MESSAGE_RATE_LIMITED`. Tin bị chặn không được lưu
- [ ] Lịch sử phân trang theo con trỏ, mới nhất ở cuối
- [ ] Lịch sử không có ghi chú nội bộ của nhân viên
- [ ] Nhân viên gọi route phía khách nhận 403
**Kiểm tra:** test tích hợp với hai `pk_anon` khác nhau; test giới hạn với `FakeTimeProvider`
**Phụ thuộc:** SD-02 · **Quy mô:** M

### SD-16: Kênh SSE phía khách
**Mô tả:** `CustomerEventHub` giữ kết nối SSE của từng hội thoại. `GET /api/support/conversations/{id}/events` gửi tin của nhân viên, tin hệ thống và đổi trạng thái phiên tới trang `/support`.
**Tiêu chí nghiệm thu:**
- [ ] Người không phải chủ hội thoại nhận 404 `SUPPORT_CONVERSATION_NOT_FOUND`
- [ ] Tin của nhân viên tới kết nối SSE trong 2 giây sau commit
- [ ] Sự kiện chỉ gửi sau commit. Transaction bị hủy không sinh sự kiện
- [ ] Kết nối nhận dòng chú thích giữ kết nối mỗi 15 giây
- [ ] Client ngắt kết nối thì hub gỡ `Channel` của kết nối đó
- [ ] Response SSE có `Content-Type: text/event-stream` và `Cache-Control: no-cache`
**Kiểm tra:** test tích hợp đọc stream SSE
**Phụ thuộc:** SD-15 · **Quy mô:** M

### SD-UI7: Trang `/support` của khách
**Mô tả:** Trang chat của khách trong `apps/frontend/src/features/support-desk/customer/`. Trang gọi route của SD-15, nhận sự kiện của SD-16 bằng `EventSource`, và render tin của khách, nhân viên, hệ thống. Tin hệ thống có `action` thì trang hiện form của SD-UI6. Fallback của `chatbot-rag` trỏ `form_url` tới trang này.
**Tiêu chí nghiệm thu:**
- [ ] Khách vãng lai và khách đã đăng nhập đều mở được `/support`
- [ ] Hội thoại mới hiện câu báo hội thoại được lưu 90 ngày khi `showRetentionNotice` bằng `true`
- [ ] Gửi tin đầu tiên đổi trạng thái hiển thị sang "Đang chờ nhân viên"
- [ ] Tin của nhân viên hiện tên ngắn của nhân viên, khác kiểu với tin của khách
- [ ] Lỗi 429 `SUPPORT_MESSAGE_RATE_LIMITED` hiện thông báo tiếng Việt và giữ nội dung ô soạn
- [ ] Mất kết nối `/events` thì trang nối lại và không hiện tin trùng
- [ ] Chân trang có link "Liên hệ CSKH" tới `/support` (chờ Câu hỏi mở 12). App chưa có chân trang thì task này dựng chân trang, vì `knowledge-base` làm sau
- [ ] Ô soạn có `label`. Dùng được bằng bàn phím
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright console có 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest cho hook SSE và ô soạn; ảnh chụp; Playwright `--headed`
**Phụ thuộc:** SD-16 · **Quy mô:** M

### SD-04: Mở phiên chờ và nhắc lại
**Mô tả:** Khách gửi tin khi hội thoại không có phiên mở thì module tạo phiên chờ. Module ghi tin hệ thống vào hội thoại, rồi báo nhân viên sẵn sàng qua SSE. Job mỗi phút nhắc lại phiên chờ quá 10 phút. Ngày 2026-10-18 task thay `HandoffRequestedEvent` bằng tin của khách ở SD-15.
**Tiêu chí nghiệm thu:**
- [ ] Trong giờ: tin hệ thống có số phút chờ dự kiến N theo `WaitEstimator`
- [ ] Ngoài giờ hoặc không ai sẵn sàng: tin hệ thống báo ngoài giờ kèm `action = callback_form`
- [ ] Phiên lưu `handoff_in_hours`, `customer_user_id`, `anonymous_id` đúng lúc mở phiên
- [ ] Sự kiện SSE tới nhân viên sẵn sàng trong 3 giây sau khi khách gửi tin
- [ ] Hai tin của khách gửi cùng lúc chỉ tạo 1 phiên
- [ ] Tin của khách khi đã có phiên mở không tạo phiên mới
- [ ] Phiên chờ quá 10 phút trong giờ: nhân viên sẵn sàng nhận sự kiện nhắc lại, khách nhận tin có `action = callback_form`
- [ ] Mỗi phiên chỉ được nhắc lại 1 lần
**Kiểm tra:** test tích hợp với `FakeTimeProvider`; test 2 request gửi tin song song
**Phụ thuộc:** SD-03, SD-16 · **Quy mô:** M

### SD-05: Yêu cầu gọi lại
**Mô tả:** `POST /api/support/callback-requests` cho khách để lại số điện thoại. `GET` và `PATCH /api/admin/support/callback-requests[/{id}]` cho nhân viên. Job hằng ngày xóa số điện thoại sau 30 ngày. Handler `UserDeletedEvent` xóa số điện thoại của người dùng đó.
**Tiêu chí nghiệm thu:**
- [ ] Người không phải chủ hội thoại nhận 404 `SUPPORT_CONVERSATION_NOT_FOUND`
- [ ] Số điện thoại sai luật của `ContactFieldRules` trả 400 `VALIDATION_ERROR`. Số đúng lưu dạng `0xxxxxxxxx`
- [ ] Yêu cầu thứ 4 trong cùng ngày giờ Việt Nam của một `anonymousId` trả 429 `CALLBACK_LIMIT_REACHED`
- [ ] Yêu cầu mới phát sự kiện SSE cho nhân viên có `conversations:read`
- [ ] `GET` cần `conversations:read`. `PATCH` cần `conversations:reply` và chỉ nhận `done` hoặc `unreachable`
- [ ] `PATCH` ghi `handled_by` và `handled_at`
- [ ] Job xóa số điện thoại của yêu cầu cũ hơn 30 ngày, giữ dòng cho báo cáo
- [ ] `UserDeletedEvent` xóa số điện thoại của mọi yêu cầu gắn `userId` đó
- [ ] Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp; test gọi processor
**Phụ thuộc:** SD-04 · **Quy mô:** M

### SD-06: Hàng chờ và nhận hội thoại
**Mô tả:** `GET /api/admin/support/sessions?view=waiting|mine` trả hàng chờ. `POST /api/admin/support/sessions/{id}/assignment` nhận phiên bằng UPDATE có điều kiện. Ngày 2026-10-18 task bỏ lời gọi `IConversationService.AssignAsync` và bước bù trừ. Nhận phiên và ghi tin hệ thống chạy trong một transaction.
**Tiêu chí nghiệm thu:**
- [ ] `sales` gọi hàng chờ nhận 403. `support` nhận 200
- [ ] Mỗi phiên trong hàng chờ có 200 ký tự đầu của tin đầu tiên trong phiên và số giây đã chờ
- [ ] Hai request nhận song song: đúng 1 request 201, request kia 409 `SUPPORT_SESSION_ALREADY_CLAIMED`
- [ ] Nhận thành công ghi đúng 1 dòng audit `support.session.claim` trong cùng transaction
- [ ] Nhận thành công gửi khách tin hệ thống báo nhân viên đã nhận, qua SSE phía khách
- [ ] Phiên đã kết thúc trả 409 `SUPPORT_SESSION_CLOSED`
- [ ] Nhân viên khác nhận sự kiện SSE để gỡ phiên khỏi tab Đang chờ
- [ ] Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp với 2 request song song
**Phụ thuộc:** SD-04 · **Quy mô:** M

### SD-07: Tin nhân viên và tin mới của khách
**Mô tả:** `POST /api/admin/support/sessions/{id}/messages` lưu tin vai `staff` vào `support_messages` và đẩy tin tới khách qua SD-16. Tin mới của khách ở SD-15 được đẩy tới nhân viên giữ phiên qua `SupportEventHub`. Ngày 2026-10-18 task bỏ `IConversationService` và `CustomerMessageEvent`.
**Tiêu chí nghiệm thu:**
- [ ] Người không giữ phiên gửi tin nhận 403 `SUPPORT_SESSION_NOT_ASSIGNED`
- [ ] Tin rỗng hoặc dài hơn 2.000 ký tự trả 400 `VALIDATION_ERROR`
- [ ] Tin đầu tiên của phiên ghi `first_response_at`. Tin sau không đổi giá trị này
- [ ] Tin hiện tên hiển thị của nhân viên, ví dụ "Lan - CSKH" (chờ Câu hỏi mở 2)
- [ ] Khách nhận tin qua SSE phía khách trong 2 giây
- [ ] Tin mới của khách tới nhân viên giữ phiên qua SSE. Nhân viên khác không nhận
**Kiểm tra:** test tích hợp đọc cả SSE nhân viên và SSE phía khách
**Phụ thuộc:** SD-06 · **Quy mô:** M

### SD-UI2: Khung chat và mẫu trả lời nhanh
**Mô tả:** Cột giữa của màn CSKH: lịch sử tin có nhãn khách, nhân viên, hệ thống. Ô soạn có nút gửi và gõ `/` để chọn mẫu. Ngày 2026-10-18 task bỏ nhãn bot, nguồn bot và nút "Gợi ý trả lời".
**Tiêu chí nghiệm thu:**
- [ ] Gõ `/` mở danh sách mẫu. Mũi tên lên, xuống chọn mẫu. Enter chèn mẫu đã thay biến
- [ ] Ô soạn tắt và có chú thích khi nhân viên không giữ phiên
- [ ] Lỗi 403 `SUPPORT_SESSION_NOT_ASSIGNED` hiện bằng `ErrorAlert`
- [ ] Lịch sử hiện mọi phiên cũ của cùng hội thoại, có dòng phân cách giữa các phiên
- [ ] Có `label` cho ô soạn. Dùng được bằng bàn phím
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright console có 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest cho bộ chọn mẫu và ô soạn; ảnh chụp
**Phụ thuộc:** SD-07, SD-UI1 · **Quy mô:** M

### SD-08: Chuyển, kết thúc và tự đóng phiên
**Mô tả:** `PUT /api/admin/support/sessions/{id}/assignment` chuyển phiên cho đồng nghiệp kèm ghi chú nội bộ. `PATCH /api/admin/support/sessions/{id}` với `status: "closed"` kết thúc phiên. Job mỗi phút tự đóng phiên `human` không có tin trong 30 phút. Ngày 2026-10-18 task bỏ `IConversationService.CloseHumanSessionAsync`.
**Tiêu chí nghiệm thu:**
- [ ] Chỉ người đang giữ phiên chuyển được. Người khác nhận 403 `SUPPORT_SESSION_NOT_ASSIGNED`
- [ ] Người nhận không ở trạng thái `available` trả 409 `SUPPORT_STAFF_UNAVAILABLE`
- [ ] Ghi chú nội bộ không xuất hiện trong lịch sử tin của khách
- [ ] Chuyển ghi đúng 1 dòng audit `support.session.transfer`. Kết thúc ghi đúng 1 dòng `support.session.close`
- [ ] Kết thúc gửi khách tin hệ thống có `action = csat_form`
- [ ] Sau khi kết thúc, tin mới của khách mở phiên chờ mới trong cùng hội thoại
- [ ] Phiên im lặng 30 phút tự đóng với `closed_by = auto`, không ghi audit
- [ ] Phiên đã kết thúc trả 409 `SUPPORT_SESSION_CLOSED` cho cả chuyển và kết thúc
**Kiểm tra:** test tích hợp; test gọi processor với `FakeTimeProvider`
**Phụ thuộc:** SD-07 · **Quy mô:** M

### SD-09: Chấm điểm CSAT
**Mô tả:** `PUT /api/support/conversations/{id}/csat` lưu điểm 1 đến 5 cho phiên vừa kết thúc của hội thoại.
**Tiêu chí nghiệm thu:**
- [ ] Người không phải chủ hội thoại nhận 404 `SUPPORT_CONVERSATION_NOT_FOUND`
- [ ] Điểm ngoài khoảng 1 đến 5 trả 400 `VALIDATION_ERROR`
- [ ] Hội thoại chưa có phiên đã kết thúc trả 409 `SUPPORT_SESSION_OPEN`
- [ ] Gửi lại điểm cho cùng phiên ghi đè điểm cũ. Phiên chỉ có 1 điểm
**Kiểm tra:** test tích hợp
**Phụ thuộc:** SD-08 · **Quy mô:** S

### SD-11: Bảng thông tin khách
**Mô tả:** `GET /api/admin/support/sessions/{id}/customer` trả thông tin cho cột phải. Thông tin gồm tên và email của khách và đơn gần đây. Task chờ Câu hỏi mở 2 và 3 của plan. Ngày 2026-10-18 task bỏ "sản phẩm bot đã gợi ý" và "trang khách đang xem", vì hai trường này đến từ `shopping-assistant`.
**Tiêu chí nghiệm thu:**
- [ ] Khách đã đăng nhập: có tên, email và tối đa 5 đơn gần đây qua `ICommerceReader.GetOrdersForCustomerAsync`
- [ ] Khách vãng lai: có đơn đặt từ cùng `anonymousId`, không có tên và email
- [ ] Response không có dữ liệu sở thích hay lịch sử xem hàng của khách
- [ ] Mỗi đơn có link tới trang đơn trong back-office
- [ ] `sales` nhận 403. Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp với cổng đọc giả của identity và commerce
**Phụ thuộc:** SD-06, quyết định của người dùng ở Câu hỏi mở 2, 3 · **Quy mô:** S

### SD-UI3: Thông tin khách và hộp thoại chuyển phiên
**Mô tả:** Cột phải của màn CSKH hiện dữ liệu của SD-11. Thanh trên của khung chat có nút "Chuyển" và "Kết thúc".
**Tiêu chí nghiệm thu:**
- [ ] Hộp thoại chuyển chỉ liệt kê nhân viên đang sẵn sàng, trừ chính người dùng
- [ ] Hộp thoại có ô ghi chú nội bộ với `label`. Esc đóng hộp thoại và trả focus về nút "Chuyển"
- [ ] "Kết thúc" hỏi xác nhận trước khi gọi API
- [ ] Lỗi 409 hiện bằng `ErrorAlert` với thông báo của API
- [ ] Ở 375 px, cột thông tin khách thu thành ngăn mở bằng nút
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright console có 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; ảnh chụp
**Phụ thuộc:** SD-08, SD-11, SD-UI2 · **Quy mô:** M

### SD-UI4: Tab Gọi lại
**Mô tả:** Tab Gọi lại liệt kê yêu cầu `pending` trước, kèm số điện thoại, ghi chú và thời điểm gửi. Nhân viên đánh dấu "Đã gọi" hoặc "Không liên lạc được".
**Tiêu chí nghiệm thu:**
- [ ] Yêu cầu mới hiện trong tab mà không tải lại trang
- [ ] Người thiếu `conversations:reply` thấy danh sách nhưng không thấy nút đánh dấu
- [ ] Số điện thoại hiện dạng link `tel:`
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright console có 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; ảnh chụp
**Phụ thuộc:** SD-05, SD-UI1 · **Quy mô:** S

### SD-UI6: Form số điện thoại và form CSAT phía khách
**Mô tả:** `CallbackRequestForm` và `CsatForm` trong `apps/frontend/src/features/support-desk/customer/`. Trang `/support` của SD-UI7 render hai form theo `action` của tin hệ thống (quyết định 21). Ngày 2026-10-18 task đổi nơi gắn form từ widget của `shopping-assistant` sang `/support`.
**Tiêu chí nghiệm thu:**
- [ ] Form số điện thoại kiểm bằng Zod cùng luật với backend. Lỗi hiện dưới ô bằng `FieldError`
- [ ] Lỗi 429 `CALLBACK_LIMIT_REACHED` hiện thông báo tiếng Việt, form không gửi lại
- [ ] Gửi thành công thì form đổi thành lời xác nhận
- [ ] Form CSAT có 5 nút chọn điểm dùng được bằng bàn phím, mỗi nút có tên truy cập
- [ ] Ảnh chụp trong khung chat ở 1280 px và 375 px không tràn ngang
- [ ] Playwright console có 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; ảnh chụp
**Phụ thuộc:** SD-05, SD-09, SD-UI7 · **Quy mô:** M

### SD-17: Xóa dữ liệu hỗ trợ
**Mô tả:** Job hằng ngày đóng hội thoại im lặng 24 giờ và xóa hội thoại cũ hơn 90 ngày, theo quyết định 25 và 26. Handler `UserDeletedEvent` xóa hội thoại của người dùng đó. Job theo mẫu `ImageDeletionWorker`.
**Tiêu chí nghiệm thu:**
- [ ] Hội thoại `open` có `last_message_at` cũ hơn 24 giờ chuyển `closed`. Hội thoại 23 giờ còn `open`
- [ ] Hội thoại cũ hơn 90 ngày bị xóa cùng tin và phiên. Hội thoại 89 ngày còn giữ
- [ ] `UserDeletedEvent` xóa mọi hội thoại có `customer_user_id` đó, cùng tin và phiên
- [ ] Xóa hội thoại không xóa dòng `callback_requests`. Báo cáo gọi lại vẫn đếm đúng
- [ ] Test gọi processor trực tiếp với `FakeTimeProvider`, không chờ timer
**Kiểm tra:** test tích hợp; test gọi processor
**Phụ thuộc:** SD-15 · **Quy mô:** S

### Checkpoint SD2
- [ ] Khách gửi tin ở `/support`, nhân viên nhận, trả lời, kết thúc, khách chấm điểm trên `pnpm dev`
- [ ] Ngoài giờ, khách để lại số điện thoại và tab Gọi lại hiện yêu cầu
- [ ] Review với người dùng

---

## Phase 3: Cấu hình và số liệu

### SD-10: Cấu hình và mẫu trả lời nhanh
**Mô tả:** `GET` và `PUT /api/admin/support/settings` cho giờ làm việc, ngày nghỉ, mốc nhắc lại, mốc tự đóng. `/api/admin/support/canned-responses[/{id}]` cho mẫu cá nhân và mẫu dùng chung.
**Tiêu chí nghiệm thu:**
- [ ] `support` gọi `PUT /api/admin/support/settings` nhận 403. Admin nhận 200
- [ ] Giờ mở cửa không trước giờ đóng cửa. Sai thì trả 400 `VALIDATION_ERROR`
- [ ] Mỗi lần sửa cấu hình ghi đúng 1 dòng audit `support.settings.update` trong cùng transaction
- [ ] Mẫu cá nhân: người có `conversations:reply` tạo, sửa, xóa mẫu của chính mình. Sửa mẫu của người khác trả 404
- [ ] Mẫu dùng chung: tạo, sửa, xóa cần `support-settings:manage`, nếu không trả 403
- [ ] Thay đổi mẫu dùng chung ghi 1 dòng audit
- [ ] `shortcut` trùng trong phạm vi của một người trả 409 `CANNED_SHORTCUT_TAKEN`
- [ ] `GET` trả mẫu cá nhân của người gọi và mọi mẫu dùng chung
- [ ] Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp
**Phụ thuộc:** SD-02 · **Quy mô:** M

### SD-UI5: Màn cấu hình CSKH
**Mô tả:** Màn `/admin/support/settings` cho giờ làm việc, ngày nghỉ và mẫu dùng chung. Màn quản lý mẫu cá nhân nằm trong màn CSKH. Menu cấu hình chỉ hiện với người có `support-settings:manage`.
**Tiêu chí nghiệm thu:**
- [ ] Lưu thành công hiện thông báo. Lỗi hiện dưới đúng ô bằng `FieldError`
- [ ] Ngày nghỉ chọn bằng ô ngày có `label`, xóa được từng ngày
- [ ] Người thiếu quyền không thấy menu. Mở thẳng URL thấy trang không đủ quyền
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright console có 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; E2E `--headed`
**Phụ thuộc:** SD-10 · **Quy mô:** M

### SD-12: AI soạn nháp
Đã bỏ ngày 2026-10-18. Pickory gỡ `ai-gateway` (task `AIR`), nên module không còn gọi AI.

**Mô tả:** `POST /api/admin/support/sessions/{id}/drafts` dựng đầu vào theo quyết định 15, gọi `IAiGateway` với `AiFeature.SupportDeskDraftReply`, rồi trả bản nháp kèm nguồn. Module ghi một dòng `draft_logs` và không gửi tin.
**Tiêu chí nghiệm thu:**
- [ ] Người không giữ phiên nhận 403 `SUPPORT_SESSION_NOT_ASSIGNED`
- [ ] Đầu vào có tối đa 10 tin gần nhất và đoạn tài liệu từ `IKnowledgeRetriever` với `audience` là `staff`
- [ ] Fake provider ghi lại request. Request không chứa số điện thoại, email hay địa chỉ của khách, kể cả số trong `callback_requests`
- [ ] Request có thể chứa đoạn tài liệu `internal`
- [ ] Nguồn trả về chỉ gồm `chunkId` có trong kết quả truy xuất của lượt đó
- [ ] Sau khi tạo bản nháp, số tin nhân viên trong hội thoại không đổi
- [ ] Hết ngân sách hoặc AI lỗi trả 503 `SUPPORT_DRAFT_UNAVAILABLE`. Nút trên giao diện hiện "Tạm không khả dụng"
- [ ] Câu hỏi về chính sách đổi trả với tài liệu seed trả bản nháp có ít nhất 1 nguồn
- [ ] Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp với fake provider ghi request
**Phụ thuộc:** SD-07 · **Quy mô:** M

### SD-13: `ISupportMetrics`
**Mô tả:** Cài `ISupportMetrics.GetAsync` và `GetPendingCountsAsync` cho `admin-console`. Khoảng ngày tính theo giờ Việt Nam. Ngày 2026-10-18 task bỏ chỉ số `draftUsageRate`, vì `SD-12` đã bỏ.
**Tiêu chí nghiệm thu:**
- [ ] `handoffs`, `handledInHours`, `callbacksRequested`, `callbacksCompleted` khớp số đếm tay trên bộ dữ liệu dựng sẵn
- [ ] `firstResponseSeconds` p50 và p90 tính từ `handoff_at` tới `first_response_at`, khớp số tính tay
- [ ] `handleMinutes` p50 và p90 tính từ `claimed_at` tới `closed_at`
- [ ] `csat` trả trung bình và số lượt chấm, bỏ phiên chưa chấm
- [ ] `SupportMetricsSnapshot` không có trường `draftUsageRate`
- [ ] Khoảng không có dữ liệu trả số 0 và phân vị rỗng, không ném lỗi
- [ ] Phiên lúc 23:30 giờ Việt Nam thuộc đúng ngày theo giờ Việt Nam
- [ ] `GetPendingCountsAsync` trả số phiên đang chờ và số yêu cầu gọi lại `pending`
**Kiểm tra:** test tích hợp trên dữ liệu dựng sẵn
**Phụ thuộc:** SD-08, SD-09 · **Quy mô:** M

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
**Kiểm tra:** `pnpm test:e2e` ở chế độ ẩn và `--headed`
**Phụ thuộc:** SD-UI3, SD-UI6, SD-UI7 · **Quy mô:** M

### SD-E2E2: Luồng ngoài giờ và hai người nhận
**Mô tả:** E2E file `apps/frontend/e2e/support-desk-callback.spec.ts`. Startup E2E đặt giờ làm việc để thời điểm chạy test nằm ngoài giờ.
**Tiêu chí nghiệm thu:**
- [ ] Khách gửi tin ở `/support` ngoài giờ và thấy thông báo ngoài giờ kèm form số điện thoại
- [ ] Khách gửi số điện thoại. Tab Gọi lại của nhân viên hiện yêu cầu
- [ ] Nhân viên đánh dấu "Đã gọi" và yêu cầu rời nhóm `pending`
- [ ] Hai nhân viên bấm nhận cùng một phiên: một người thấy "Đã có người nhận"
- [ ] Đăng nhập `sales` không thấy menu CSKH
**Kiểm tra:** `pnpm test:e2e`
**Phụ thuộc:** SD-UI4, SD-UI6, SD-UI7 · **Quy mô:** M

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
