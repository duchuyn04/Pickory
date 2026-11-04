# Hợp đồng API của support-desk

## Tóm tắt

Hợp đồng API là bản thỏa thuận về dạng dữ liệu giữa frontend và backend. Bản này ghi mỗi route nhận gì, trả gì và báo lỗi bằng mã nào.

Ví dụ đời thường: thợ làm ổ cắm và thợ làm phích cắm cùng theo một bản vẽ. Hai người làm cùng lúc ở hai xưởng, và phích vẫn cắm vừa ổ.

Với support-desk, cách làm như sau:

- Frontend dựng giao diện theo hợp đồng. Route backend chưa có thì frontend dùng dữ liệu mẫu.
- Backend làm route theo hợp đồng. Test của backend kiểm response thật có cùng dạng với dữ liệu mẫu.
- Hai bên gặp nhau ở hai task E2E (`SD-E2E1`, `SD-E2E2`). Lúc đó frontend gỡ hết dữ liệu mẫu và chạy với backend thật.

Dữ liệu mẫu (fixture) là file JSON chứa ví dụ request và response. Các file nằm ở `spec/fixtures/support-desk/`. Cả frontend và backend cùng đọc các file này, giống cách `spec/fixtures/catalog/` đang làm.

Plan: `tasks/support-desk/plan.md`, quyết định 30 và 31. Task: `tasks/support-desk/todo-backend.md` và `tasks/support-desk/todo-frontend.md`.

## Cách hai bên dùng hợp đồng

### Frontend

1. Viết schema `zod` theo bảng trường của route.
2. Thêm Vitest đọc file mẫu của route.
3. Kiểm schema `zod` đọc được mọi ví dụ thành công trong file mẫu.
4. Dùng `page.route` trả `response` của ví dụ trong E2E khi route chưa có.
5. Gỡ `page.route` của route khi helper của SD-C2 báo backend đã có route.

Kết quả mong đợi: giao diện chạy được với dữ liệu mẫu trước khi backend xong. Khi backend xong, giao diện chạy với route thật mà không đổi code đọc dữ liệu.

### Làm sao biết backend đã xong một route

Frontend có hai tín hiệu. Tín hiệu tự động chạy trong E2E. Tín hiệu cho người đọc nằm trong tài liệu.

Tín hiệu tự động:
- Backend chạy E2E ở môi trường Development. Môi trường này luôn bật tài liệu OpenAPI ở `/api/openapi/v1.json`.
- Tài liệu OpenAPI liệt kê mọi route backend đang có, với `method` và đường dẫn.
- Helper route giả của SD-C2 đọc tài liệu này trước khi gắn dữ liệu giả.
- Route đã có trong tài liệu thì helper làm test lỗi, kèm câu "Backend đã có ... Gỡ dữ liệu giả của route này."

Tín hiệu cho người đọc:
- Ô "Có từ" trong bảng tổng hợp route ghi tên task backend.
- Dòng của task backend trong mục "Task List" của `plan.md` đã tick.

Hai tín hiệu tới nhánh frontend theo thứ tự này:

1. Nhóm backend gộp task vào `main`.
2. Nhóm frontend gộp `main` vào nhánh làm việc.
3. Nhóm frontend chạy `pnpm test:e2e`.
4. Helper báo lỗi ở route mà backend vừa làm xong.
5. Nhóm frontend xóa lời gọi helper route giả của route đó.
6. Nhóm frontend chạy lại E2E với route thật.

Kết quả mong đợi: bước 6 xanh. Bước 6 đỏ thì frontend và backend đang hiểu hợp đồng khác nhau. Hai nhóm đối chiếu với file này trước khi sửa code.

Sự kiện SSE giả không có tín hiệu tự động. Route `events` đã có từ SD-03 và SD-16. Hai task `SD-E2E1` và `SD-E2E2` kiểm sự kiện thật.

### Backend

1. Làm route theo bảng trường và bảng mã lỗi.
2. Thêm test tích hợp gọi route thật.
3. So response thật với ví dụ cùng `name` trong file mẫu bằng helper của `SD-C1`.

Helper so tập khóa và kiểu giá trị (chuỗi, số, boolean, `null`, mảng, object). Helper không so giá trị cụ thể, vì mã định danh và thời điểm khác nhau mỗi lần chạy.

### Khi cần đổi hợp đồng

Không đổi dạng dữ liệu trong code trước khi sửa hợp đồng. Bên kia sẽ đọc sai dữ liệu mà không có test nào báo.

1. Sửa file này và file mẫu trong cùng một commit.
2. Ghi một dòng vào mục "Lịch sử thay đổi" ở cuối file.
3. Báo bên kia trong handoff của task.
4. Chạy test hợp đồng của cả hai bên.

Kết quả mong đợi: test hợp đồng của bên chưa sửa báo đỏ. Bên đó sửa code theo hợp đồng mới cho tới khi test xanh.

## Quy ước chung

- Response thành công có dạng `{ data }`. Response lỗi có dạng `{ error: { code, message, details? } }`.
- `details` chỉ có ở lỗi 400 `VALIDATION_ERROR`. Mỗi phần tử có `key` và `message`.
- Thời điểm là chuỗi ISO 8601 giờ UTC, ví dụ `2026-11-03T02:15:00Z`.
- Mã định danh là chuỗi. Client không đoán cấu trúc bên trong mã.
- Trường có thể rỗng luôn có mặt với giá trị `null`. Backend không bỏ khóa.
- Tên khóa viết camelCase. Giá trị liệt kê viết thường, ví dụ `open`, `pending`, `callback_form`.
- Trạng thái đơn hàng dùng cùng giá trị với API đơn của `commerce`, ví dụ `pending_payment`, `shipping`.
- Lỗi dùng chung: 400 `VALIDATION_ERROR`, 401 khi chưa đăng nhập, 403 `FORBIDDEN` khi thiếu quyền.
- Route phía khách gửi header `Cache-Control: no-store`.

## Bảng tổng hợp route

Cột "Có từ" ghi task đã làm route. Ô ghi "chưa có" nghĩa là frontend dùng dữ liệu mẫu cho tới khi task backend được tick.

| Route | File mẫu | Task backend | Task frontend | Có từ |
|---|---|---|---|---|
| `POST /api/support/conversations` | `customer-api.json` | SD-15, SD-04 thêm `session` | SD-UI7 | SD-15, SD-04 |
| `GET /api/support/conversations/{id}/messages` | `customer-api.json` | SD-15, SD-07 thêm `staffDisplayName` | SD-UI7 | SD-15 |
| `POST /api/support/conversations/{id}/messages` | `customer-api.json` | SD-15 | SD-UI7 | SD-15 |
| `GET /api/support/conversations/{id}/events` | `customer-events.json` | SD-16, SD-07 thêm `staffDisplayName` | SD-UI7 | SD-16 |
| `POST /api/support/callback-requests` | `customer-api.json` | SD-05 | SD-UI6 | SD-05 |
| `PUT /api/support/conversations/{id}/csat` | `customer-api.json` | SD-09 | SD-UI6 | chưa có |
| `GET /api/admin/support/sessions?view=waiting\|mine` | `admin-sessions.json` | SD-06 | SD-UI1 | SD-06 |
| `POST /api/admin/support/sessions/{id}/assignment` | `admin-sessions.json` | SD-06 | SD-UI2 | SD-06 |
| `PUT /api/admin/support/sessions/{id}/assignment` | `admin-sessions.json` | SD-08 | SD-UI3 | chưa có |
| `PATCH /api/admin/support/sessions/{id}` | `admin-sessions.json` | SD-08 | SD-UI3 | chưa có |
| `POST /api/admin/support/sessions/{id}/messages` | `admin-sessions.json` | SD-07 | SD-UI2 | chưa có |
| `GET /api/admin/support/conversations/{id}/messages` | `admin-sessions.json` | SD-07 | SD-UI2 | chưa có |
| `GET /api/admin/support/sessions/{id}/customer` | `admin-customer-panel.json` | SD-11 | SD-UI3 | chưa có |
| `GET /api/admin/support/callback-requests` | `admin-callbacks.json` | SD-05 | SD-UI4 | SD-05 |
| `PATCH /api/admin/support/callback-requests/{id}` | `admin-callbacks.json` | SD-05 | SD-UI4 | SD-05 |
| `GET · POST · PATCH · DELETE /api/admin/support/canned-responses[/{id}]` | `admin-canned-responses.json` | SD-10 | SD-UI2 (đọc), SD-UI5 (sửa) | chưa có |
| `GET · PUT /api/admin/support/settings` | `admin-settings.json` | SD-10 | SD-UI5 | chưa có |
| `GET /api/admin/support/events` | `admin-events.json` | SD-03, SD-04, SD-05, SD-06, SD-07, SD-08 | SD-UI1, SD-UI2, SD-UI4 | SD-03 |
| `GET · PUT /api/admin/support/presence` | không có | SD-03 | SD-UI1, SD-UI3 | SD-03 |

`GET · PUT /api/admin/support/presence` đã chạy thật từ SD-03, nên route này không có file mẫu.

## Route phía khách

Mọi route ở mục này kiểm chủ hội thoại theo quyết định 13 của plan. Người không phải chủ nhận 404 `SUPPORT_CONVERSATION_NOT_FOUND`. Nhân viên gọi route phía khách nhận 403 `FORBIDDEN`.

### Lấy hoặc tạo hội thoại

`POST /api/support/conversations`. Request không có body.

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | chuỗi | Mã hội thoại |
| `status` | `open` | Route này chỉ trả hội thoại đang mở |
| `createdAt` | thời điểm | |
| `showRetentionNotice` | boolean | `true` khi hội thoại vừa được tạo |
| `session` | object hoặc `null` | Phiên gần nhất của hội thoại. `null` khi hội thoại chưa có phiên |
| `session.id` | chuỗi | Mã phiên |
| `session.status` | `waiting`, `assigned`, `closed` | Trang `/support` dùng giá trị này để hiện trạng thái khi tải lại |
| `session.csatScore` | số 1 đến 5 hoặc `null` | Điểm khách đã chấm cho phiên `closed` |

Response 201 khi tạo mới, 200 khi trả hội thoại đang mở.

SD-04 thêm trường `session`. Trước khi SD-04 xong, schema frontend đọc khóa thiếu như `null`.

### Lịch sử tin của khách

`GET /api/support/conversations/{id}/messages?cursor=&limit=`

- `limit` mặc định 50. Backend kéo giá trị về khoảng 1 đến 100.
- `cursor` lấy từ `nextCursor` của trang trước. Trang sau chứa tin cũ hơn.
- Trong một trang, tin mới nhất ở cuối.
- `nextCursor` bằng `null` khi không còn tin cũ hơn.

Response: `{ data: { items: Message[], nextCursor } }`.

Kiểu `Message` phía khách:

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | chuỗi | |
| `role` | `customer`, `staff`, `system` | |
| `body` | chuỗi | Tối đa 2.000 ký tự |
| `action` | `callback_form`, `csat_form` hoặc `null` | Chỉ tin `system` có giá trị khác `null` |
| `staffDisplayName` | chuỗi hoặc `null` | Tên trong hồ sơ của nhân viên, ví dụ "Trần Thị Lan". Chỉ tin `staff` có giá trị |
| `createdAt` | thời điểm | |

SD-07 thêm `staffDisplayName`. Tới lúc đó backend trả `null`.

- `staffDisplayName` không bao giờ chứa email của nhân viên. Nhân viên chưa có tên trong hồ sơ thì giá trị bằng `null`.
- Frontend hiện "Nhân viên CSKH" khi giá trị bằng `null`.

### Khách gửi tin

`POST /api/support/conversations/{id}/messages` với body `{ body }`.

- `body` sau khi bỏ khoảng trắng hai đầu dài 1 đến 2.000 ký tự.
- Response 201 trả một `Message`.

| Lỗi | Khi nào |
|---|---|
| 400 `VALIDATION_ERROR` | `body` rỗng hoặc dài hơn 2.000 ký tự |
| 409 `SUPPORT_CONVERSATION_CLOSED` | Hội thoại đã đóng |
| 429 `SUPPORT_MESSAGE_RATE_LIMITED` | Khách gửi quá 10 tin mỗi phút hoặc 200 tin mỗi ngày |

### Kênh SSE của khách

`GET /api/support/conversations/{id}/events`. Danh sách sự kiện ở mục "Sự kiện SSE phía khách".

### Khách để lại số điện thoại

`POST /api/support/callback-requests` với body `{ conversationId, phone, note }`.

- `phone` theo luật của `ContactFieldRules`. Backend lưu dạng `0xxxxxxxxx`.
- `note` là chuỗi hoặc `null`, tối đa 500 ký tự.
- Response 201: `{ data: { id, status, createdAt } }`, với `status` bằng `pending`.
- Hội thoại có phiên chờ chưa ai nhận thì backend đóng phiên đó với `closedBy` bằng `auto`. Backend phát `session.closed` tới khách và nhân viên.
- Đóng phiên lúc này không gửi form CSAT, vì chưa có nhân viên nào nhận phiên.

| Lỗi | Khi nào |
|---|---|
| 400 `VALIDATION_ERROR` | Số điện thoại sai luật, hoặc `note` quá dài |
| 404 `SUPPORT_CONVERSATION_NOT_FOUND` | Người gửi không phải chủ hội thoại |
| 429 `CALLBACK_LIMIT_REACHED` | Yêu cầu thứ 4 trong cùng ngày giờ Việt Nam của một `anonymousId` |

### Khách chấm điểm

`PUT /api/support/conversations/{id}/csat` với body `{ score }`.

- `score` là số nguyên 1 đến 5.
- Backend lưu điểm cho phiên `closed` gần nhất của hội thoại. Gửi lại thì ghi đè điểm cũ.
- Response 200: `{ data: { sessionId, score } }`.

| Lỗi | Khi nào |
|---|---|
| 400 `VALIDATION_ERROR` | `score` ngoài khoảng 1 đến 5 |
| 409 `SUPPORT_SESSION_OPEN` | Hội thoại chưa có phiên `closed` |

## Route phía nhân viên

Cột "Quyền" theo mục API của `spec/SPEC-support-desk.md`. Người thiếu quyền nhận 403 `FORBIDDEN`.

### Hàng chờ

`GET /api/admin/support/sessions?view=waiting|mine`, quyền `conversations:read`.

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | chuỗi | Mã phiên |
| `conversationId` | chuỗi | |
| `preview` | chuỗi | 200 ký tự đầu của tin đầu tiên trong phiên |
| `waitingSeconds` | số | Số giây từ `handoffAt` tới lúc nhận, hoặc tới hiện tại khi chưa ai nhận |
| `handoffAt` | thời điểm | Lúc mở phiên chờ |
| `claimedAt` | thời điểm hoặc `null` | `null` ở `view=waiting` |
| `transferNote` | chuỗi hoặc `null` | Ghi chú nội bộ của lần chuyển gần nhất |

`view=waiting` trả phiên chưa ai nhận, cũ nhất trước. `view=mine` trả phiên người gọi đang giữ.

SD-06 thêm `claimedAt` và `transferNote` vào hợp đồng cũ của quyết định 30. `transferNote` bằng `null` cho tới khi SD-08 xong.

### Nhận phiên

`POST /api/admin/support/sessions/{id}/assignment`, quyền `conversations:reply`. Request không có body.

Response 201: `{ data: { sessionId, staffId, claimedAt } }`.

| Lỗi | Khi nào |
|---|---|
| 404 `SUPPORT_SESSION_NOT_FOUND` | Không có phiên |
| 409 `SUPPORT_SESSION_ALREADY_CLAIMED` | Người khác đã nhận phiên |
| 409 `SUPPORT_SESSION_CLOSED` | Phiên đã kết thúc |

### Chuyển phiên

`PUT /api/admin/support/sessions/{id}/assignment` với body `{ staffId, note }`, quyền `conversations:reply`.

- `note` là chuỗi hoặc `null`, tối đa 500 ký tự. Khách không đọc được ghi chú này.
- Response 200: `{ data: { sessionId, staffId, claimedAt } }`, với `staffId` là người nhận mới.

| Lỗi | Khi nào |
|---|---|
| 400 `VALIDATION_ERROR` | `staffId` rỗng, `staffId` là người gọi, hoặc `note` quá dài |
| 403 `SUPPORT_SESSION_NOT_ASSIGNED` | Người gọi không giữ phiên |
| 404 `SUPPORT_SESSION_NOT_FOUND` | Không có phiên |
| 409 `SUPPORT_STAFF_UNAVAILABLE` | Người nhận không ở trạng thái `available` |
| 409 `SUPPORT_SESSION_CLOSED` | Phiên đã kết thúc |

### Kết thúc phiên

`PATCH /api/admin/support/sessions/{id}` với body `{ status: "closed" }`, quyền `conversations:reply`.

Response 200: `{ data: { sessionId, closedAt, closedBy } }`. `closedBy` là mã nhân viên, hoặc `auto` khi job tự đóng.

| Lỗi | Khi nào |
|---|---|
| 400 `VALIDATION_ERROR` | `status` khác `closed` |
| 403 `SUPPORT_SESSION_NOT_ASSIGNED` | Người gọi không giữ phiên |
| 404 `SUPPORT_SESSION_NOT_FOUND` | Không có phiên |
| 409 `SUPPORT_SESSION_CLOSED` | Phiên đã kết thúc |

### Nhân viên gửi tin

`POST /api/admin/support/sessions/{id}/messages` với body `{ body }`, quyền `conversations:reply`.

Response 201 trả một `StaffViewMessage`.

| Lỗi | Khi nào |
|---|---|
| 400 `VALIDATION_ERROR` | `body` rỗng hoặc dài hơn 2.000 ký tự |
| 403 `SUPPORT_SESSION_NOT_ASSIGNED` | Người gọi không giữ phiên |
| 404 `SUPPORT_SESSION_NOT_FOUND` | Không có phiên |
| 409 `SUPPORT_SESSION_CLOSED` | Phiên đã kết thúc |

### Lịch sử tin cho nhân viên

`GET /api/admin/support/conversations/{id}/messages?cursor=&limit=`, quyền `conversations:read`.

Route này mới, spec chưa có. Khung chat của SD-UI2 cần lịch sử của mọi phiên trong hội thoại. Phân trang giống route lịch sử tin của khách.

Kiểu `StaffViewMessage` có mọi trường của `Message` phía khách, thêm `sessionId` (chuỗi hoặc `null`). Frontend vẽ dòng phân cách khi `sessionId` đổi giữa hai tin liền nhau.

| Lỗi | Khi nào |
|---|---|
| 404 `SUPPORT_CONVERSATION_NOT_FOUND` | Không có hội thoại |

### Bảng thông tin khách

`GET /api/admin/support/sessions/{id}/customer`, quyền `conversations:read`.

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `customer.isGuest` | boolean | `true` với khách vãng lai |
| `customer.name` | chuỗi hoặc `null` | `null` với khách vãng lai, hoặc khi hồ sơ không có tên |
| `customer.email` | chuỗi hoặc `null` | `null` với khách vãng lai |
| `orders` | mảng | Tối đa 5 đơn, mới nhất trước |
| `orders[].code` | chuỗi | Frontend dựng link `/admin/orders/{code}` |
| `orders[].status` | chuỗi | Giá trị theo API đơn của `commerce` |
| `orders[].total` | số | Tổng tiền tính bằng đồng |
| `orders[].itemCount` | số | |
| `orders[].createdAt` | thời điểm | |

| Lỗi | Khi nào |
|---|---|
| 404 `SUPPORT_SESSION_NOT_FOUND` | Không có phiên |

### Yêu cầu gọi lại

`GET /api/admin/support/callback-requests`, quyền `conversations:read`. Danh sách có yêu cầu `pending` trước, rồi theo `createdAt` mới nhất trước.

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | chuỗi | |
| `conversationId` | chuỗi | |
| `phone` | chuỗi hoặc `null` | `null` khi job đã xóa số sau 30 ngày |
| `note` | chuỗi hoặc `null` | |
| `status` | `pending`, `done`, `unreachable` | |
| `createdAt` | thời điểm | |
| `handledAt` | thời điểm hoặc `null` | |

`PATCH /api/admin/support/callback-requests/{id}` với body `{ status }`, quyền `conversations:reply`. `status` nhận `done` hoặc `unreachable`. Response 200 trả một yêu cầu đầy đủ.

| Lỗi | Khi nào |
|---|---|
| 400 `VALIDATION_ERROR` | `status` khác `done` và `unreachable` |
| 404 `CALLBACK_REQUEST_NOT_FOUND` | Không có yêu cầu |

### Mẫu trả lời nhanh

`/api/admin/support/canned-responses[/{id}]`.

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | chuỗi | |
| `shortcut` | chuỗi | Không có dấu `/` ở đầu |
| `body` | chuỗi | Tối đa 2.000 ký tự. Có thể chứa `{ten_khach}` và `{ma_don}` |
| `shared` | boolean | `true` với mẫu dùng chung |
| `editable` | boolean | Người gọi sửa được mẫu này hay không |

- `GET` trả mẫu cá nhân của người gọi và mọi mẫu dùng chung. Quyền `conversations:reply`.
- `POST` với body `{ shortcut, body, shared }` trả 201.
- `PATCH` với body có `shortcut`, `body` hoặc cả hai trả 200.
- `DELETE` trả 204, không có body.
- Mẫu cá nhân cần `conversations:reply`. Mẫu dùng chung cần `support-settings:manage`.

| Lỗi | Khi nào |
|---|---|
| 400 `VALIDATION_ERROR` | `shortcut` sai luật, hoặc `body` rỗng hay quá dài |
| 403 `FORBIDDEN` | Sửa mẫu dùng chung khi thiếu `support-settings:manage` |
| 404 `CANNED_RESPONSE_NOT_FOUND` | Không có mẫu, hoặc mẫu cá nhân của người khác |
| 409 `CANNED_SHORTCUT_TAKEN` | Người gọi đã có mẫu dùng `shortcut` này |

### Cấu hình CSKH

`GET · PUT /api/admin/support/settings`, quyền `support-settings:manage`.

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `opensAt` | chuỗi `HH:mm` | Giờ Việt Nam |
| `closesAt` | chuỗi `HH:mm` | Phải sau `opensAt` |
| `holidays` | mảng chuỗi `yyyy-MM-dd` | Ngày theo lịch Việt Nam, không trùng nhau |
| `waitNudgeMinutes` | số nguyên | Mốc nhắc lại phiên chờ. Phải nhỏ hơn `autoCloseMinutes` |
| `autoCloseMinutes` | số nguyên | Mốc tự đóng phiên im lặng |
| `updatedAt` | thời điểm | Chỉ có trong response |

`PUT` nhận mọi trường trừ `updatedAt`, và trả cấu hình đã lưu.

| Lỗi | Khi nào |
|---|---|
| 400 `VALIDATION_ERROR` | `opensAt` không trước `closesAt`, ngày sai dạng, hoặc mốc phút ngoài khoảng cho phép |
| 400 `VALIDATION_ERROR` | `waitNudgeMinutes` không nhỏ hơn `autoCloseMinutes`. Lỗi gắn với khóa `waitNudgeMinutes` |

## Sự kiện SSE

Trường `data` của mọi sự kiện là JSON. Client bỏ qua sự kiện có tên lạ, để backend thêm sự kiện mới mà không làm vỡ client cũ.

### Sự kiện SSE phía khách

File mẫu: `customer-events.json`.

| Tên | Dữ liệu | Task backend | Client làm gì |
|---|---|---|---|
| `message.created` | Một `Message` phía khách | SD-16, SD-07 thêm `staffDisplayName` | Thêm tin vào khung chat. Bỏ qua tin có `id` đã hiện |
| `session.waiting` | `{ sessionId }` | SD-04 | Hiện "Đang chờ nhân viên" |
| `session.assigned` | `{ sessionId }` | SD-06 | Hiện "Nhân viên đang hỗ trợ" |
| `session.closed` | `{ sessionId }` | SD-08 | Hiện phiên đã kết thúc |

Mỗi lần luồng SSE mở lại, client đọc lại lịch sử tin và gọi lại `POST /api/support/conversations`. Sự kiện phát lúc mất kết nối không được gửi lại.

### Sự kiện SSE phía nhân viên

File mẫu: `admin-events.json`. Theo quyết định 30, client nhận sự kiện thì đọc lại danh sách liên quan.

| Tên | Dữ liệu | Task backend | Client đọc lại |
|---|---|---|---|
| `presence.changed` | `{ staffId, status }` | SD-03 | Danh sách nhân viên sẵn sàng |
| `heartbeat` | `{ at }` | SD-03 | Không đọc lại |
| `session.waiting` | `{ sessionId }` | SD-04 | Tab Đang chờ, kèm thông báo và âm báo |
| `session.reminded` | `{ sessionId }` | SD-04 | Tab Đang chờ, kèm thông báo nhắc lại |
| `session.assigned` | `{ sessionId }` | SD-06, SD-08 | Tab Đang chờ và tab Của tôi |
| `session.closed` | `{ sessionId }` | SD-05, SD-08 | Tab Đang chờ và tab Của tôi |
| `message.created` | `{ sessionId, conversationId }` | SD-07 | Lịch sử tin của hội thoại đang mở |
| `callback.created` | `{ callbackRequestId }` | SD-05 | Tab Gọi lại |

`message.created` phía nhân viên chỉ gửi tới người đang giữ phiên.

## Mã lỗi riêng của module

Thông báo của lỗi viết tiếng Việt. Frontend hiện `message` của API, trừ khi task UI ghi câu riêng.

| Mã | HTTP | Ý nghĩa | Có từ |
|---|---|---|---|
| `SUPPORT_CONVERSATION_NOT_FOUND` | 404 | Không có hội thoại, hoặc người gọi không phải chủ | SD-15 |
| `SUPPORT_CONVERSATION_CLOSED` | 409 | Khách gửi tin vào hội thoại đã đóng | SD-15 |
| `SUPPORT_MESSAGE_RATE_LIMITED` | 429 | Khách gửi tin vượt giới hạn | SD-15 |
| `SUPPORT_SESSION_NOT_FOUND` | 404 | Không có phiên | SD-06, mã mới |
| `SUPPORT_SESSION_ALREADY_CLAIMED` | 409 | Phiên đã có người nhận | SD-06 |
| `SUPPORT_SESSION_CLOSED` | 409 | Phiên đã kết thúc | SD-06 |
| `SUPPORT_SESSION_NOT_ASSIGNED` | 403 | Người gọi không giữ phiên | SD-07 |
| `SUPPORT_STAFF_UNAVAILABLE` | 409 | Người nhận chuyển giao không sẵn sàng | SD-08 |
| `SUPPORT_SESSION_OPEN` | 409 | Khách chấm điểm khi chưa có phiên kết thúc | SD-09 |
| `CALLBACK_LIMIT_REACHED` | 429 | Khách gửi quá 3 yêu cầu gọi lại trong ngày | SD-05 |
| `CALLBACK_REQUEST_NOT_FOUND` | 404 | Không có yêu cầu gọi lại | SD-05, mã mới |
| `CANNED_RESPONSE_NOT_FOUND` | 404 | Không có mẫu, hoặc mẫu cá nhân của người khác | SD-10, mã mới |
| `CANNED_SHORTCUT_TAKEN` | 409 | Người gọi đã có mẫu cùng phím tắt | SD-10 |

## Giới hạn

Người dùng duyệt các giới hạn dưới đây ngày 2026-11-04. Hai bên dùng cùng các giá trị này.

- Ghi chú chuyển phiên: tối đa 500 ký tự.
- Ghi chú yêu cầu gọi lại: tối đa 500 ký tự.
- Phím tắt mẫu: 2 đến 30 ký tự. Chỉ có chữ thường không dấu, số, `-` và `_`.
- Mốc nhắc lại và mốc tự đóng: 1 đến 240 phút.
- Mốc nhắc lại nhỏ hơn mốc tự đóng. Nếu không, phiên đóng trước lúc nhắc lại.
- `limit` của lịch sử tin: mặc định 50, tối đa 100.

## Quyết định ngày 2026-11-04

Người dùng chốt các mục dưới đây. Dạng dữ liệu của hợp đồng không đổi.

| Mục | Câu hỏi mở của plan | Quyết định |
|---|---|---|
| Bảng route REST | 1 | Duyệt bảng ở quyết định 11 của plan |
| `staffDisplayName` | 2 | Tên trong hồ sơ nhân viên, đọc qua `IStaffDirectory`. Không có tên thì `null` |
| `customer.name`, `customer.email` | 2 | SD-11 đọc qua `ICustomerDirectory` mới. Frontend hiện "Chưa có thông tin" khi giá trị bằng `null` |
| Đơn của khách vãng lai | 3 | SD-11 đọc qua `ICommerceReader.GetOrdersForAnonymousAsync` |
| Phiên chờ ngoài giờ | 5 | Khách gửi số điện thoại thì backend đóng phiên chờ. Mục "Khách để lại số điện thoại" ghi chi tiết |

## Lịch sử thay đổi

- 2026-11-04: Người dùng chốt Câu hỏi mở 1, 2, 3, 5 của plan và các giới hạn. Các phần đổi:
  - `staffDisplayName` là tên trong hồ sơ, không chứa email;
  - luật `waitNudgeMinutes` nhỏ hơn `autoCloseMinutes`, kèm ví dụ lỗi trong `admin-settings.json`;
  - gửi số điện thoại đóng phiên chờ chưa ai nhận và phát `session.closed`.
- 2026-11-03: Tạo hợp đồng cho mọi route còn lại, mở rộng quyết định 30. Các phần mới so với spec và plan cũ:
  - trường `session` trong response tạo hội thoại;
  - trường `staffDisplayName` trong tin;
  - trường `claimedAt` và `transferNote` trong hàng chờ;
  - route `GET /api/admin/support/conversations/{id}/messages`;
  - sự kiện nhân viên `session.reminded`, `message.created`, `callback.created`;
  - mã lỗi `SUPPORT_SESSION_NOT_FOUND`, `CALLBACK_REQUEST_NOT_FOUND`, `CANNED_RESPONSE_NOT_FOUND`.
