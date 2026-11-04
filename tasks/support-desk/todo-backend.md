# Task List: support-desk, nhánh backend

Quy ước chung, Phase 1, checkpoint và E2E nằm ở `tasks/support-desk/todo.md`. Dạng dữ liệu của route nằm ở `tasks/support-desk/api-contract.md`. Tiến độ chung nằm ở mục "Task List" của `tasks/support-desk/plan.md`.

Quy ước riêng của nhánh backend:
- Task back-office thêm endpoint mới vào `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`.
- Task có route mới thêm test hợp đồng. Test so response thật với ví dụ trong dữ liệu mẫu bằng helper của SD-C1.
- Task làm xong một route thì sửa ô "Có từ" của route đó trong `api-contract.md`. Nhóm frontend đọc ô này để biết route đã có.
- Route mới phải có trong `/api/openapi/v1.json`. Helper của SD-C2 đọc tài liệu này để báo nhóm frontend gỡ dữ liệu giả.
- Gộp task vào `main` ngay sau khi người dùng duyệt task. Nhánh frontend chỉ thấy route mới sau bước gộp này.
- Nhánh backend không chờ task nào của nhánh frontend.

---

## Phase 2: Hội thoại phía khách, tiếp nhận và trả lời

### SD-C1: Bộ kiểm hợp đồng phía backend
**Mô tả:** Helper `SupportDeskContract` trong `apps/backend/tests/Pickory.IntegrationTests/SupportDesk/`. Helper đọc file trong `spec/fixtures/support-desk/` theo cách của `SharedFixtures`. Helper so JSON của response thật với một ví dụ theo `name`.
**Tiêu chí nghiệm thu:**
- [x] Helper nhận JSON thật, tên file mẫu và tên ví dụ
- [x] Thiếu khóa, thừa khóa hoặc khác kiểu thì test lỗi, và thông báo ghi đường dẫn khóa, ví dụ `data.session.status`
- [x] Giá trị `null` ở một bên khớp mọi kiểu ở bên kia. Mảng so kiểu theo phần tử đầu của ví dụ
- [x] Helper so thêm mã HTTP của response với `status` của ví dụ
- [x] Test của helper chạy mọi ví dụ trong mọi file mẫu với chính ví dụ đó, và đều khớp
- [x] Phép thử đột biến: xóa một khóa khỏi JSON thật thì test lỗi
**Kiểm tra:** test tích hợp của helper. Thư mục `SupportDesk/` không thuộc `Infrastructure/`, nên task chỉ chạy bộ lọc `FullyQualifiedName~.SupportDesk.`
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/backend/tests/Pickory.IntegrationTests/SupportDesk/SupportDeskContract.cs`

### SD-15: Hội thoại và tin của khách
**Mô tả:** Route phía khách theo quyết định 11, 13, 25, 27, 28 và 29 của plan.
- `POST /api/support/conversations` lấy hội thoại `open` của người gọi hoặc tạo mới.
- `GET /api/support/conversations/{id}/messages` trả lịch sử.
- `POST /api/support/conversations/{id}/messages` lưu tin vai `customer`.

Module nghe `UserLinkedEvent` để gắn hội thoại vãng lai vào tài khoản.
**Tiêu chí nghiệm thu:**
- [x] Lần đầu trả 201 kèm hội thoại mới trạng thái `open`. Lần sau trả 200 kèm hội thoại đang mở
- [x] Response lần đầu có cờ `showRetentionNotice` bằng `true`. Lần sau cờ bằng `false`
- [x] Khách vãng lai khác `pk_anon` đọc lịch sử hoặc gửi tin nhận 404 `SUPPORT_CONVERSATION_NOT_FOUND`
- [x] Khách đăng nhập khác tài khoản nhận 404 `SUPPORT_CONVERSATION_NOT_FOUND`
- [x] Khách vãng lai đăng nhập sau: `UserLinkedEvent` gắn hội thoại, và khách thấy hội thoại cũ bằng tài khoản
- [x] Tin rỗng hoặc dài hơn 2.000 ký tự trả 400 `VALIDATION_ERROR`
- [x] Gửi tin vào hội thoại `closed` trả 409 `SUPPORT_CONVERSATION_CLOSED`
- [x] Gửi vượt giới hạn của quyết định 27 trả 429 `SUPPORT_MESSAGE_RATE_LIMITED`. Tin bị chặn không được lưu
- [x] Lịch sử phân trang theo con trỏ, mới nhất ở cuối
- [x] Lịch sử không có ghi chú nội bộ của nhân viên
- [x] Nhân viên gọi route phía khách nhận 403
**Kiểm tra:** test tích hợp với hai `pk_anon` khác nhau; test giới hạn với `FakeTimeProvider`
**Phụ thuộc:** SD-02 · **Quy mô:** M

### SD-16: Kênh SSE phía khách
**Mô tả:** `CustomerEventHub` giữ kết nối SSE của từng hội thoại. `GET /api/support/conversations/{id}/events` gửi tin của nhân viên, tin hệ thống và đổi trạng thái phiên tới trang `/support`.
**Tiêu chí nghiệm thu:**
- [x] Người không phải chủ hội thoại nhận 404 `SUPPORT_CONVERSATION_NOT_FOUND`
- [x] Tin của nhân viên tới kết nối SSE trong 2 giây sau commit
- [x] Sự kiện chỉ gửi sau commit. Transaction bị hủy không sinh sự kiện
- [x] Kết nối nhận dòng chú thích giữ kết nối mỗi 15 giây
- [x] Client ngắt kết nối thì hub gỡ `Channel` của kết nối đó
- [x] Response SSE có `Content-Type: text/event-stream` và `Cache-Control: no-cache`
**Kiểm tra:** test tích hợp đọc stream SSE
**Phụ thuộc:** SD-15 · **Quy mô:** M

### SD-04: Mở phiên chờ và nhắc lại
**Mô tả:** Khách gửi tin khi hội thoại không có phiên mở thì module tạo phiên chờ. Module ghi tin hệ thống vào hội thoại, rồi báo nhân viên sẵn sàng qua SSE. Job mỗi phút nhắc lại phiên chờ quá 10 phút. Ngày 2026-10-18 task thay `HandoffRequestedEvent` bằng tin của khách ở SD-15.
**Tiêu chí nghiệm thu:**
- [x] Trong giờ: tin hệ thống có số phút chờ dự kiến N theo `WaitEstimator`
- [x] Ngoài giờ hoặc không ai sẵn sàng: tin hệ thống báo ngoài giờ kèm `action = callback_form`
- [x] Phiên lưu `handoff_in_hours`, `customer_user_id`, `anonymous_id` đúng lúc mở phiên
- [x] Sự kiện SSE tới nhân viên sẵn sàng trong 3 giây sau khi khách gửi tin
- [x] Hai tin của khách gửi cùng lúc chỉ tạo 1 phiên
- [x] Tin của khách khi đã có phiên mở không tạo phiên mới
- [x] Phiên chờ quá 10 phút trong giờ: nhân viên sẵn sàng nhận sự kiện nhắc lại, khách nhận tin có `action = callback_form`
- [x] Mỗi phiên chỉ được nhắc lại 1 lần
- [x] Response của `POST /api/support/conversations` có trường `session` theo hợp đồng
- [x] Mở phiên phát `session.waiting` tới khách và tới nhân viên sẵn sàng. Nhắc lại phát `session.reminded` tới nhân viên sẵn sàng
- [x] Test hợp đồng: response khớp ví dụ "mở lại hội thoại đang có phiên chờ" của `customer-api.json`
**Kiểm tra:** test tích hợp với `FakeTimeProvider`; test 2 request gửi tin song song
**Phụ thuộc:** SD-03, SD-16, SD-C1 · **Quy mô:** M

### SD-05: Yêu cầu gọi lại
**Mô tả:** `POST /api/support/callback-requests` cho khách để lại số điện thoại. `GET` và `PATCH /api/admin/support/callback-requests[/{id}]` cho nhân viên. Job hằng ngày xóa số điện thoại sau 30 ngày. Handler `UserDeletedEvent` xóa số điện thoại của người dùng đó.
**Tiêu chí nghiệm thu:**
- [ ] Người không phải chủ hội thoại nhận 404 `SUPPORT_CONVERSATION_NOT_FOUND`
- [ ] Số điện thoại sai luật của `ContactFieldRules` trả 400 `VALIDATION_ERROR`. Số đúng lưu dạng `0xxxxxxxxx`
- [ ] Yêu cầu thứ 4 trong cùng ngày giờ Việt Nam của một `anonymousId` trả 429 `CALLBACK_LIMIT_REACHED`
- [ ] Yêu cầu mới phát sự kiện `callback.created` cho nhân viên có `conversations:read`
- [ ] Hội thoại có phiên chờ chưa ai nhận: yêu cầu mới đóng phiên đó với `closed_by = auto`, phát `session.closed` tới khách và nhân viên
- [ ] Đóng phiên khi gửi số không gửi tin có `action = csat_form`. Phiên đã có người nhận vẫn mở
- [ ] `GET` cần `conversations:read`. `PATCH` cần `conversations:reply` và chỉ nhận `done` hoặc `unreachable`
- [ ] `PATCH` ghi `handled_by` và `handled_at`. Yêu cầu không có trả 404 `CALLBACK_REQUEST_NOT_FOUND`
- [ ] Test hợp đồng: response khớp các ví dụ gọi lại của `customer-api.json` và `admin-callbacks.json`
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
- [ ] Phiên không có trả 404 `SUPPORT_SESSION_NOT_FOUND`
- [ ] Hàng chờ có thêm `claimedAt` và `transferNote`. `transferNote` bằng `null` tới khi SD-08 xong
- [ ] Test hợp đồng: response khớp các ví dụ hàng chờ và nhận phiên của `admin-sessions.json`
- [ ] Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp với 2 request song song
**Phụ thuộc:** SD-04 · **Quy mô:** M

### SD-07: Tin nhân viên và tin mới của khách
**Mô tả:** `POST /api/admin/support/sessions/{id}/messages` lưu tin vai `staff` vào `support_messages` và đẩy tin tới khách qua SD-16. Tin mới của khách ở SD-15 được đẩy tới nhân viên giữ phiên qua `SupportEventHub`. Ngày 2026-10-18 task bỏ `IConversationService` và `CustomerMessageEvent`.
**Tiêu chí nghiệm thu:**
- [ ] Người không giữ phiên gửi tin nhận 403 `SUPPORT_SESSION_NOT_ASSIGNED`
- [ ] Tin rỗng hoặc dài hơn 2.000 ký tự trả 400 `VALIDATION_ERROR`
- [ ] Tin đầu tiên của phiên ghi `first_response_at`. Tin sau không đổi giá trị này
- [ ] Tin hiện tên trong hồ sơ của nhân viên, đọc qua `IStaffDirectory`
- [ ] Nhân viên chưa có tên: `staffDisplayName` bằng `null`. Response và sự kiện không chứa email của nhân viên
- [ ] Khách nhận tin qua SSE phía khách trong 2 giây
- [ ] Tin mới của khách tới nhân viên giữ phiên qua SSE. Nhân viên khác không nhận
- [ ] Sự kiện tin phía nhân viên tên `message.created`, dữ liệu `{ sessionId, conversationId }`
- [ ] Tin phía khách (lịch sử và sự kiện `message.created`) có thêm `staffDisplayName`. Tin không phải `staff` có giá trị `null`
- [ ] Thêm `GET /api/admin/support/conversations/{id}/messages` cho nhân viên, cần `conversations:read`. Mỗi tin có `sessionId`
- [ ] Test hợp đồng: response khớp ví dụ gửi tin và lịch sử tin của `admin-sessions.json`, và ví dụ lịch sử của `customer-api.json`
- [ ] Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp đọc cả SSE nhân viên và SSE phía khách
**Phụ thuộc:** SD-06 · **Quy mô:** M

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
- [ ] Phiên chờ có `handoff_in_hours = false` mà chưa ai nhận tự đóng sau `autoCloseMinutes`, với `closed_by = auto`
- [ ] Phiên chờ mở trong giờ không tự đóng khi chưa ai nhận. Phiên này chỉ nhận nhắc lại của SD-04
- [ ] Phiên đã kết thúc trả 409 `SUPPORT_SESSION_CLOSED` cho cả chuyển và kết thúc
- [ ] Hàng chờ `view=mine` của người nhận có `transferNote` của lần chuyển gần nhất
- [ ] Kết thúc và tự đóng phát `session.closed` tới khách và tới nhân viên. Chuyển phát `session.assigned` tới nhân viên
- [ ] Test hợp đồng: response khớp các ví dụ chuyển và kết thúc phiên của `admin-sessions.json`
**Kiểm tra:** test tích hợp; test gọi processor với `FakeTimeProvider`
**Phụ thuộc:** SD-07 · **Quy mô:** M

### SD-09: Chấm điểm CSAT
**Mô tả:** `PUT /api/support/conversations/{id}/csat` lưu điểm 1 đến 5 cho phiên vừa kết thúc của hội thoại.
**Tiêu chí nghiệm thu:**
- [ ] Người không phải chủ hội thoại nhận 404 `SUPPORT_CONVERSATION_NOT_FOUND`
- [ ] Điểm ngoài khoảng 1 đến 5 trả 400 `VALIDATION_ERROR`
- [ ] Hội thoại chưa có phiên đã kết thúc trả 409 `SUPPORT_SESSION_OPEN`
- [ ] Gửi lại điểm cho cùng phiên ghi đè điểm cũ. Phiên chỉ có 1 điểm
- [ ] Test hợp đồng: response khớp các ví dụ chấm điểm của `customer-api.json`
**Kiểm tra:** test tích hợp
**Phụ thuộc:** SD-08 · **Quy mô:** S

### SD-11: Bảng thông tin khách
**Mô tả:** `GET /api/admin/support/sessions/{id}/customer` trả thông tin cho cột phải. Thông tin gồm tên và email của khách và đơn gần đây. Task thêm `ICustomerDirectory.GetContactAsync` vào Contracts của Identity để đọc tên và email. Ngày 2026-10-18 task bỏ "sản phẩm bot đã gợi ý" và "trang khách đang xem", vì hai trường này đến từ `shopping-assistant`.
**Tiêu chí nghiệm thu:**
- [ ] Khách đã đăng nhập: có tên, email và tối đa 5 đơn gần đây qua `ICommerceReader.GetOrdersForCustomerAsync`
- [ ] Khách đã đăng nhập: tên và email đọc qua `ICustomerDirectory.GetContactAsync`
- [ ] Khách vãng lai: có đơn đặt từ cùng `anonymousId` qua `ICommerceReader.GetOrdersForAnonymousAsync`, không có tên và email
- [ ] Bộ integration test chạy đủ, vì task đổi Contracts của Identity
- [ ] Response không có dữ liệu sở thích hay lịch sử xem hàng của khách
- [ ] Mỗi đơn có `code`, để frontend dựng link tới trang đơn trong back-office
- [ ] `sales` nhận 403. Endpoint mới có trong ma trận quyền
- [ ] Test hợp đồng: response khớp các ví dụ của `admin-customer-panel.json`
**Kiểm tra:** test tích hợp với cổng đọc giả của identity và commerce
**Phụ thuộc:** SD-06 · **Quy mô:** M

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

Hết task backend của Phase 2. Báo người dùng để chạy Checkpoint SD2 trong `tasks/support-desk/todo.md`.

---

## Phase 3: Cấu hình và số liệu

### SD-10: Cấu hình và mẫu trả lời nhanh
**Mô tả:** `GET` và `PUT /api/admin/support/settings` cho giờ làm việc, ngày nghỉ, mốc nhắc lại, mốc tự đóng. `/api/admin/support/canned-responses[/{id}]` cho mẫu cá nhân và mẫu dùng chung.
**Tiêu chí nghiệm thu:**
- [ ] `support` gọi `PUT /api/admin/support/settings` nhận 403. Admin nhận 200
- [ ] Giờ mở cửa không trước giờ đóng cửa. Sai thì trả 400 `VALIDATION_ERROR`
- [ ] `waitNudgeMinutes` không nhỏ hơn `autoCloseMinutes` trả 400 `VALIDATION_ERROR` với khóa `waitNudgeMinutes`
- [ ] Mỗi lần sửa cấu hình ghi đúng 1 dòng audit `support.settings.update` trong cùng transaction
- [ ] Mẫu cá nhân: người có `conversations:reply` tạo, sửa, xóa mẫu của chính mình. Sửa mẫu của người khác trả 404
- [ ] Mẫu dùng chung: tạo, sửa, xóa cần `support-settings:manage`, nếu không trả 403
- [ ] Thay đổi mẫu dùng chung ghi 1 dòng audit
- [ ] `shortcut` trùng trong phạm vi của một người trả 409 `CANNED_SHORTCUT_TAKEN`
- [ ] `GET` trả mẫu cá nhân của người gọi và mọi mẫu dùng chung. Mỗi mẫu có `editable` theo quyền của người gọi
- [ ] Mẫu không có, hoặc mẫu cá nhân của người khác, trả 404 `CANNED_RESPONSE_NOT_FOUND`
- [ ] Test hợp đồng: response khớp mọi ví dụ của `admin-canned-responses.json` và `admin-settings.json`
- [ ] Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp
**Phụ thuộc:** SD-02, SD-C1 · **Quy mô:** M

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

### SD-12: AI soạn nháp
Đã bỏ ngày 2026-10-18. Pickory gỡ `ai-gateway` (task `AIR`), nên module không còn gọi AI.

**Mô tả:** `POST /api/admin/support/sessions/{id}/drafts` dựng đầu vào theo quyết định 15, gọi `IAiGateway` với `AiFeature.SupportDeskDraftReply`, rồi trả bản nháp kèm nguồn. Module ghi một dòng `draft_logs` và không gửi tin.
**Tiêu chí nghiệm thu:**
- Người không giữ phiên nhận 403 `SUPPORT_SESSION_NOT_ASSIGNED`
- Đầu vào có tối đa 10 tin gần nhất và đoạn tài liệu từ `IKnowledgeRetriever` với `audience` là `staff`
- Fake provider ghi lại request. Request không chứa số điện thoại, email hay địa chỉ của khách, kể cả số trong `callback_requests`
- Request có thể chứa đoạn tài liệu `internal`
- Nguồn trả về chỉ gồm `chunkId` có trong kết quả truy xuất của lượt đó
- Sau khi tạo bản nháp, số tin nhân viên trong hội thoại không đổi
- Hết ngân sách hoặc AI lỗi trả 503 `SUPPORT_DRAFT_UNAVAILABLE`. Nút trên giao diện hiện "Tạm không khả dụng"
- Câu hỏi về chính sách đổi trả với tài liệu seed trả bản nháp có ít nhất 1 nguồn
- Endpoint mới có trong ma trận quyền
**Kiểm tra:** test tích hợp với fake provider ghi request
**Phụ thuộc:** SD-07 · **Quy mô:** M

Hết task backend của Phase 3. Báo người dùng để chạy Checkpoint SD3 trong `tasks/support-desk/todo.md`. Phase 4 cũng nằm trong file đó.
