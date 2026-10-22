# Implementation Plan: support-desk

## Thay đổi phạm vi ngày 2026-10-18

Người dùng chuyển chatbot sang dự án riêng `chatbot-rag`. Pickory gắn chatbot đó bằng widget ở `tasks/chatbot-widget/` (mã `CBW`). Người dùng cũng bỏ `shopping-assistant` và lập task gỡ `ai-gateway` ở `tasks/ai-gateway-removal/` (mã `AIR`).

Người dùng chọn giữ `support-desk` và làm độc lập. Các thay đổi chính:

- Khách chat trực tiếp với nhân viên CSKH ở trang `/support`, không qua bot.
- `support-desk` tự sở hữu hội thoại phía khách: tạo hội thoại, lưu tin, kênh SSE, quyền chủ hội thoại, xóa dữ liệu.
- Fallback của `chatbot-rag` trỏ `form_url` tới `/support`. Task cấu hình fallback thuộc `CBW`.
- Bỏ `SD-12` (AI soạn nháp), cột `draft_logs`, chỉ số `draftUsageRate` và mọi phụ thuộc `ai-gateway`.
- Bỏ phụ thuộc `shopping-assistant` (`IConversationService`, `HandoffRequestedEvent`, `CustomerMessageEvent`).
- Bỏ phụ thuộc truy xuất của `knowledge-base` (`IKnowledgeRetriever`). `knowledge-base` chỉ còn trang `/help`.
- Thêm `SD-15`, `SD-16`, `SD-17` và `SD-UI7` cho phần hội thoại phía khách.

Task và quyết định bị bỏ vẫn nằm trong file để tra cứu, kèm dòng "Đã bỏ ngày 2026-10-18".

## Overview

Module `support-desk` là kênh chat giữa khách và nhân viên CSKH. Khách mở `/support`, gửi tin và chờ nhân viên nhận. Nhân viên nhận hội thoại, trả lời realtime và xem đơn của khách. Module cũng quản lý giờ làm việc, yêu cầu gọi lại ngoài giờ, mẫu trả lời nhanh, CSAT và số liệu CSKH.

`support-desk` phụ thuộc `identity` và `commerce`. Module đứng sau `commerce` và trước `admin-console` trong thứ tự build.

Spec gốc: `spec/SPEC.md`, `spec/SPEC-support-desk.md`. Spec còn viết theo phạm vi cũ (chatbot chuyển giao, AI soạn nháp) và theo interface bản Node. Plan này chuyển interface sang .NET theo `tasks/dotnet-migration/plan.md`.
Danh sách task chi tiết: `tasks/support-desk/todo.md`.

## Phần đã phủ bởi plan cũ

Các mục dưới đây đã có trong code hoặc trong plan cũ. Plan này không lặp lại task của các mục đó.

1. Quyền. `conversations:read`, `conversations:reply`, `support-settings:manage` đã có trong `Permissions.cs` và `PermissionCatalog.cs` (ID-01 của `tasks/identity-ai-gateway/todo.md`). Spec ghi `supportPermissions` là export riêng của module. Quyết định 1 của `tasks/identity-ai-gateway/plan.md` đã thay cách này bằng danh mục chung của `identity`.
2. Role mặc định. `DefaultRoles.cs` cho `support` hai quyền `conversations:read` và `conversations:reply`. `sales` không có `conversations:read`. Bảng này khớp spec, nên test "sales gọi hàng chờ nhận 403" dùng seed có sẵn.
3. Mục menu và trang giữ chỗ. Mục "CSKH" trỏ tới `/admin/conversations` đã có trong `apps/frontend/src/features/identity/admin/admin-navigation.ts`. `apps/frontend/src/app.tsx` đang gắn `AdminPlaceholderPage` cho đường dẫn này. Module này thay trang giữ chỗ bằng màn CSKH thật và giữ nguyên đường dẫn `/admin/conversations`.
4. Tính năng AI. Đã bỏ ngày 2026-10-18. `AiFeature.SupportDeskDraftReply` không còn dùng, và task `AIR` gỡ mã này cùng `ai-gateway`.
5. Hạ tầng dùng chung. `PiiRedactor` (`Pickory.Shared/Privacy/`), luật số điện thoại `ContactFieldRules` (`Pickory.Shared/Validation/`), `IAuditLogWriter`, khung ma trận quyền và hạ tầng E2E đã có. `RateLimiter` chuyển sang `Pickory.Shared` ở SHIP-08 của `tasks/shipping/todo.md`.
6. Định danh khách vãng lai. `Actor.AnonymousId` lấy từ cookie `pk_anon` và luôn có giá trị. `identity` phát `UserLinkedEvent` khi khách vãng lai đăng nhập. Giỏ hàng của `commerce` đã dùng cơ chế này (`CartLinkHandler`).

## Điều kiện trước

1. `commerce` đã có `ICommerceReader.GetOrdersForCustomerAsync`. Xem `tasks/commerce/plan.md`.
2. Plan đổi route cũ sang REST đã xong. Plan đó chạy trước `commerce`.
3. Người dùng đã trả lời Câu hỏi mở 1, 2, 3 và 10. Các câu này đổi route của spec, đổi Contracts của module khác, hoặc chốt giới hạn chống spam.
4. `spec/SPEC-support-desk.md` đã được cập nhật theo phạm vi mới trước `SD-15`. Spec hiện còn ghi phụ thuộc `shopping-assistant`, `knowledge-base`, `ai-gateway`.

## Đồ thị phụ thuộc

```
SD-01 logic thuần: giờ làm việc, ước tính thời gian chờ, biến mẫu, phân vị
 └── SD-02 khung module, Contracts, schema, migration
      ├── SD-03 hub SSE nhân viên + trạng thái sẵn sàng ─────────── SD-UI1 hàng chờ, công tắc, thông báo
      ├── SD-15 hội thoại và tin của khách
      │    └── SD-16 kênh SSE phía khách ─────────────────────────── SD-UI7 trang /support của khách
      │         └── SD-04 mở phiên chờ + job nhắc lại (cần SD-03)
      │              ├── SD-05 yêu cầu gọi lại ──────────────────── SD-UI4 tab Gọi lại; SD-UI6 form SĐT, form CSAT
      │              └── SD-06 hàng chờ + nhận hội thoại
      │                   └── SD-07 tin nhân viên + tin mới của khách ── SD-UI2 khung chat, mẫu `/`
      │                        └── SD-08 chuyển, kết thúc, tự đóng ──── SD-UI3 bảng thông tin khách, hộp thoại chuyển
      │                             ├── SD-09 CSAT
      │                             └── SD-11 bảng thông tin khách (chờ Câu hỏi mở 2, 3)
      ├── SD-17 xóa dữ liệu hỗ trợ
      └── SD-10 cấu hình + mẫu trả lời nhanh ─────────────────────── SD-UI5 màn cấu hình
SD-08, SD-09 └── SD-13 `ISupportMetrics`
mọi task └── SD-E2E1, SD-E2E2 └── SD-14 nghiệm thu
```

`SD-04` cần `SD-03` và `SD-16`. Tin hệ thống của phiên chờ phải tới nhân viên qua hub nhân viên và tới khách qua kênh của khách. `SD-12` đã bỏ, nên `SD-13` chỉ cần dữ liệu phiên và CSAT.

## Architecture Decisions

1. Cấu trúc theo `tasks/dotnet-migration/plan.md`. Hai project mới: `Pickory.Modules.SupportDesk` và `Pickory.Modules.SupportDesk.Contracts`. `SupportDeskModule.cs` có `AddSupportDeskModule` và `MapSupportDeskEndpoints`. `SupportDeskDbContext` ghi lịch sử migration vào schema `ef_support_desk`. `Pickory.ArchitectureTests` thêm luật cho module mới.
2. Chuyển interface từ Node sang .NET.
   - `supportMetrics.get(range)` thành `ISupportMetrics.GetAsync(DateRange range, CancellationToken cancellationToken)`, trả `SupportMetricsSnapshot`.
   - `supportMetrics.pendingCount()` thành `ISupportMetrics.GetPendingCountsAsync(CancellationToken cancellationToken)`, trả `SupportPendingCounts(int Waiting, int Callbacks)`.
   - `DateRange` dùng kiểu của `Pickory.Shared` nếu `commerce` đã thêm. Nếu chưa có, task SD-02 thêm `DateRange` vào `Pickory.Modules.SupportDesk.Contracts` và ghi chú để `admin-console` gộp sau.
   - Đã bỏ ngày 2026-10-18: `onHandoffRequested`, `onCustomerMessage` và `IConversationService`. Module tự sở hữu hội thoại (quyết định 24).
   - Spec ghi validate bằng Zod. Backend dùng FluentValidation.
3. Phiên hỗ trợ là nguồn sự thật của việc nhận hội thoại. Mỗi lần khách cần nhân viên, module tạo một dòng `support_sessions`. Chỉ mục unique một phần trên `conversation_id` khi `closed_at` rỗng chặn hai phiên mở cho một hội thoại. Hai tin của khách đến cùng lúc không tạo phiên thứ hai.
4. Nhận hội thoại bằng UPDATE có điều kiện. Lệnh là `UPDATE support_sessions SET staff_id = @me, claimed_at = now() WHERE id = @id AND staff_id IS NULL AND closed_at IS NULL`. Số dòng bằng 0 nghĩa là người khác đã nhận, API trả 409 `SUPPORT_SESSION_ALREADY_CLAIMED`.
5. Đã bỏ ngày 2026-10-18. Bù trừ khi gọi `shopping-assistant` lỗi không còn cần. Phiên, hội thoại và tin nằm chung `SupportDeskDbContext`, nên nhận phiên và ghi tin hệ thống chạy trong một transaction.
6. Hub SSE trong tiến trình. `SupportEventHub` giữ một `Channel` cho mỗi kết nối của nhân viên. `CustomerEventHub` giữ một `Channel` cho mỗi kết nối của khách. MVP chạy một instance, giống quyết định 6 của `tasks/identity-ai-gateway/plan.md`. Route nhân viên dùng `SupportEventStreamResult`, viết bằng `SseFormatter` của ASP.NET Core 10 và không thêm package. Lý do: `TypedResults.ServerSentEvents` không gửi header trước sự kiện đầu tiên, nên trình duyệt phải chờ nhịp tim đầu mới biết đã kết nối.
7. Trạng thái sẵn sàng theo nhịp tim. Kết nối SSE cập nhật `staff_presence.last_seen_at` mỗi 30 giây. Job chạy mỗi 30 giây chuyển nhân viên sang `away` khi `last_seen_at` cũ hơn 2 phút. Identity không phát sự kiện đăng xuất. Đăng xuất đóng trang, nên SSE ngắt và job chuyển `away` sau 2 phút. Luồng SSE cũng tự đóng sau 5 phút (`MaxConnectionLifetime`). `EventSource` tự kết nối lại, và lần kết nối lại kiểm phiên cùng quyền. Nhờ vậy tài khoản bị khóa, mất quyền hoặc đăng xuất ở máy khác không giữ luồng quá 5 phút. Nhân viên đăng xuất ở máy khác có thể còn `available` tối đa khoảng 7 phút: 5 phút tới khi luồng đóng, cộng 2 phút tới khi job chuyển `away`. Sự kiện phát lúc `EventSource` đang kết nối lại bị mất, vì hub chỉ gửi cho kết nối đã đăng ký. Vì vậy client đọc lại `GET /api/admin/support/presence` mỗi lần `EventSource` mở lại. Các task sau cũng đọc lại danh sách phiên ở thời điểm đó.
8. Giờ làm việc là hàm thuần có `TimeProvider`. `WorkingHoursCalendar` đổi giờ UTC sang `Asia/Ho_Chi_Minh`, rồi so với khung giờ và ngày nghỉ. Khung giờ là nửa mở: 8:00 thuộc trong giờ, 21:00 thuộc ngoài giờ. "Trong giờ" còn cần ít nhất một nhân viên `available`.
9. Ước tính thời gian chờ. N bằng thời gian chờ trung bình 7 ngày nhân số phiên đang chờ cộng 1, chia số nhân viên sẵn sàng, làm tròn lên. Chưa có dữ liệu 7 ngày thì dùng 5 phút cho mỗi phiên. Hằng số này nằm trong `SupportSettings` để admin chỉnh sau.
10. Phiên lưu thêm cột cho báo cáo. Spec chưa có các cột này. `support_sessions` thêm `handoff_in_hours`, `customer_user_id`, `anonymous_id`. Báo cáo đếm phiên trong giờ mà không cần tính lại lịch cũ. Hai cột định danh cho phép xóa dữ liệu khi nhận `UserDeletedEvent`.
11. Route theo REST mức tài nguyên. Spec còn dùng route đặt theo hành động. Plan đề xuất bảng dưới, chờ người dùng chốt ở Câu hỏi mở 1. Response giữ dạng `{ data }`, không kèm link.

    | Spec | Đề xuất REST |
    | --- | --- |
    | `GET /api/admin/support/queue` | `GET /api/admin/support/sessions?view=waiting\|mine` |
    | `POST .../conversations/:id/claim` | `POST /api/admin/support/sessions/{id}/assignment` (201, hoặc 409 khi đã có người nhận) |
    | `POST .../conversations/:id/transfer` | `PUT /api/admin/support/sessions/{id}/assignment` (người đang giữ phiên đổi người nhận) |
    | `POST .../conversations/:id/close` | `PATCH /api/admin/support/sessions/{id}` với `status: "closed"` |
    | `POST .../conversations/:id/draft` | Đã bỏ ngày 2026-10-18 cùng `SD-12` |
    | `POST .../conversations/:id/messages` | `POST /api/admin/support/sessions/{id}/messages` |
    | `GET · PATCH /api/admin/support/callbacks[/:id]` | `GET · PATCH /api/admin/support/callback-requests[/{id}]` |
    | `POST /api/support/conversations/:id/csat` | `PUT /api/support/conversations/{id}/csat` |
    | không có | `GET /api/admin/support/presence` (danh sách nhân viên sẵn sàng cho hộp thoại chuyển) |
    | không có | `POST /api/support/conversations` (lấy hội thoại đang mở hoặc tạo mới) |
    | không có | `GET /api/support/conversations/{id}/messages` (lịch sử, phân trang theo con trỏ) |
    | không có | `POST /api/support/conversations/{id}/messages` (khách gửi tin) |
    | không có | `GET /api/support/conversations/{id}/events` (SSE phía khách) |

    Route giữ như spec: `POST /api/support/callback-requests`, `GET /api/admin/support/events`, `PUT /api/admin/support/presence`, `/api/admin/support/canned-responses[/{id}]`, `GET · PUT /api/admin/support/settings`.
12. Mã lỗi. Thông báo viết tiếng Việt.
    - `SUPPORT_SESSION_ALREADY_CLAIMED` (409): phiên đã có người nhận.
    - `SUPPORT_SESSION_NOT_ASSIGNED` (403): người gửi không giữ phiên.
    - `SUPPORT_SESSION_CLOSED` (409): phiên đã kết thúc.
    - `SUPPORT_SESSION_OPEN` (409): khách chấm điểm khi hội thoại chưa có phiên đã kết thúc.
    - `CANNED_SHORTCUT_TAKEN` (409): một người có hai mẫu cùng `shortcut`.
    - `SUPPORT_STAFF_UNAVAILABLE` (409): người nhận chuyển giao không ở trạng thái sẵn sàng.
    - `CALLBACK_LIMIT_REACHED` (429): khách gửi quá 3 yêu cầu gọi lại trong một ngày.
    - `SUPPORT_CONVERSATION_NOT_FOUND` (404): hội thoại không có, hoặc người gọi không phải chủ.
    - `SUPPORT_CONVERSATION_CLOSED` (409): khách gửi tin vào hội thoại đã đóng.
    - `SUPPORT_MESSAGE_RATE_LIMITED` (429): khách gửi tin vượt giới hạn ở quyết định 27.
    - Đã bỏ ngày 2026-10-18: `SUPPORT_DRAFT_UNAVAILABLE` và `SUPPORT_UNAVAILABLE`.
13. Chủ hội thoại. Route phía khách đọc `support_conversations` của module. Khách đăng nhập là chủ khi `customer_user_id` bằng `Actor.UserId`. Khách vãng lai là chủ khi `anonymous_id` bằng `Actor.AnonymousId`. Không khớp thì trả 404 `SUPPORT_CONVERSATION_NOT_FOUND`, để không lộ hội thoại có tồn tại.
14. Giới hạn 3 yêu cầu gọi lại mỗi ngày. Module đếm dòng `callback_requests` theo `anonymous_id` trong ngày giờ Việt Nam. Bộ đếm nằm trong DB, nên khởi động lại API không xóa giới hạn. Số điện thoại kiểm bằng `ContactFieldRules` và lưu dạng `0xxxxxxxxx`.
15. Đã bỏ ngày 2026-10-18. Dữ liệu gửi AI không còn, vì `SD-12` đã bỏ.
16. Tin tối đa 2.000 ký tự. Giới hạn áp cho tin của nhân viên và tin của khách. Spec không ghi giới hạn này. Plan chọn 2.000 ký tự, bằng giới hạn nhận xét của review trong `commerce`.
17. Đã bỏ ngày 2026-10-18. Luật "AI không tự gửi" không còn đối tượng.
18. Đã bỏ ngày 2026-10-18. `DraftSimilarity` không còn dùng.
19. Audit log. Module ghi `IAuditLogWriter` trong cùng transaction cho các hành động sau:
    - `support.session.claim`, `support.session.transfer`, `support.session.close` theo spec.
    - `support.settings.update` và thay đổi mẫu dùng chung, theo luật "Always" của `spec/SPEC.md`.
    - Phiên tự đóng không có nhân viên, nên không ghi audit. Module ghi log `Information` với `closedBy = auto`.
20. Job nền theo mẫu `ImageDeletionWorker`. Module có sáu job:
    - nhắc lại phiên chờ quá 10 phút;
    - tự đóng phiên `human` im lặng 30 phút;
    - chuyển `away` khi mất nhịp tim;
    - xóa số điện thoại sau 30 ngày;
    - đóng hội thoại im lặng 24 giờ (quyết định 25);
    - xóa hội thoại cũ hơn 90 ngày (quyết định 26).

    Test gọi thẳng processor với `FakeTimeProvider`, không chờ timer.
21. Form của khách nằm trên trang `/support`. `features/support-desk/customer/` có `CallbackRequestForm` và `CsatForm`. Khung chat của `/support` render form theo `action` của tin hệ thống (`callback_form`, `csat_form`).
22. Công tắc sẵn sàng trên thanh trên cùng. Module làm component `PresenceToggle`. Trước khi có `admin-console`, component gắn vào `AdminLayout` hiện tại. `admin-console` chuyển component vào layout mới sau.
23. Báo cáo CSKH hiện ở `admin-console`. Module này cung cấp `ISupportMetrics` và test số liệu. Trang báo cáo thuộc `tasks/admin-console/plan.md`.
24. Module tự sở hữu hội thoại phía khách. Hai bảng mới:
    - `support_conversations`: `id`, `customer_user_id`, `anonymous_id`, `status` (`open` hoặc `closed`), `created_at`, `last_message_at`, `closed_at`;
    - `support_messages`: `id`, `conversation_id`, `role` (`customer`, `staff`, `system`), `staff_id`, `body`, `action`, `created_at`.

    Ghi chú nội bộ khi chuyển phiên nằm ở `support_sessions`, không nằm ở `support_messages`. Khách không đọc được ghi chú này.
25. Vòng đời hội thoại. Mỗi chủ có tối đa một hội thoại `open`. Hội thoại im lặng 24 giờ thì job chuyển `closed`. Tin tiếp theo của khách tạo hội thoại mới. Trong một hội thoại, mỗi lần khách cần nhân viên mở một phiên mới. Khách gửi tin khi không có phiên mở thì module mở phiên chờ.
26. Thời hạn lưu 90 ngày. Job hằng ngày xóa hội thoại có `last_message_at` cũ hơn 90 ngày, cùng tin và phiên của hội thoại. `UserDeletedEvent` xóa hội thoại của người dùng đó. Hạn này thay Câu hỏi mở 6 cũ và giữ luật "Never" của spec về lịch sử hỗ trợ.
27. Chống spam tin của khách. Route gửi tin và tạo hội thoại mở công khai cho khách vãng lai. Module dùng `RateLimiter` của `Pickory.Shared` theo `anonymous_id`. Đề xuất 10 tin mỗi phút và 200 tin mỗi ngày, chờ chốt ở Câu hỏi mở 10.
28. Gắn hội thoại vãng lai vào tài khoản. Module nghe `UserLinkedEvent` và gán `customer_user_id` cho hội thoại `open` của `anonymous_id` đó. Khách đăng nhập giữa chừng vẫn thấy hội thoại cũ.
29. Thông báo lưu hội thoại. Response tạo hội thoại lần đầu có cờ `showRetentionNotice`. Trang `/support` hiện câu báo hội thoại được lưu 90 ngày. Cách làm giữ theo SA-03 của plan `shopping-assistant` đã bỏ.

## Task List

Chi tiết từng task ở `tasks/support-desk/todo.md`.

### Phase 1: Nền tảng
- [x] SD-01 · [x] SD-02 · [x] SD-03 · [ ] SD-UI1

### Checkpoint SD1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `SupportDesk` tham chiếu module khác ngoài Contracts
- [ ] Nhân viên bật "Sẵn sàng" trên màn CSKH, mất kết nối 2 phút thì thành "Tạm vắng"
- [ ] Review với người dùng

### Phase 2: Hội thoại phía khách, tiếp nhận và trả lời
- [ ] SD-15 · [ ] SD-16 · [ ] SD-UI7 · [ ] SD-04 · [ ] SD-05 · [ ] SD-06 · [ ] SD-07 · [ ] SD-UI2 · [ ] SD-08 · [ ] SD-09 · [ ] SD-11 · [ ] SD-UI3 · [ ] SD-UI4 · [ ] SD-UI6 · [ ] SD-17

### Checkpoint SD2
- [ ] Khách gửi tin ở `/support`, nhân viên nhận, trả lời, kết thúc, khách chấm điểm trên `pnpm dev`
- [ ] Ngoài giờ, khách để lại số điện thoại và tab Gọi lại hiện yêu cầu
- [ ] Review với người dùng

### Phase 3: Cấu hình và số liệu
- [ ] SD-10 · [ ] SD-UI5 · [ ] SD-13

Đã bỏ ngày 2026-10-18: SD-12.

### Checkpoint SD3
- [ ] `ISupportMetrics` trả đúng số đã tính tay trên bộ dữ liệu test
- [ ] Review với người dùng

### Phase 4: E2E và nghiệm thu
- [ ] SD-E2E1 · [ ] SD-E2E2 · [ ] SD-14

### Checkpoint cuối
- [ ] Mọi dòng Traceability đạt
- [ ] Coverage dòng của `Pickory.Modules.SupportDesk` ít nhất 80%, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `admin-console`

## Traceability: Success Criteria → Task

Spec chưa cập nhật theo phạm vi mới. Bảng dưới ghi tiêu chí theo phạm vi ngày 2026-10-18.

| Success Criteria | Task | Cách kiểm |
| --- | --- | --- |
| Khách gửi tin đầu tiên ở `/support` trong giờ: nhân viên sẵn sàng nhận thông báo trong 3 giây | SD-03, SD-04, SD-15, SD-UI1, SD-E2E1 | Test tích hợp đo thời điểm sự kiện SSE so với lúc khách gửi tin; E2E chờ thông báo tối đa 3 giây |
| Ngoài giờ: khách thấy thông báo ngoài giờ và form SĐT; yêu cầu gọi lại xuất hiện trong tab Gọi lại | SD-04, SD-05, SD-UI4, SD-UI6, SD-E2E2 | Test tích hợp với `FakeTimeProvider` lúc 21:00; E2E ngoài giờ |
| 2 nhân viên bấm nhận cùng lúc: đúng 1 người nhận được | SD-06 | Test tích hợp gửi 2 request song song, đếm 1 lần 201 và 1 lần 409 |
| Tin nhân viên đến khách trong 2 giây; sau khi kết thúc phiên, tin mới của khách mở phiên chờ mới | SD-07, SD-08, SD-16, SD-E2E1 | Test tích hợp đọc SSE phía khách; E2E gửi tin sau khi kết thúc |
| Người không phải chủ hội thoại không đọc, không gửi được tin | SD-15, SD-16 | Test tích hợp với hai `pk_anon` khác nhau, kỳ vọng 404 |
| Khách gửi tin vượt giới hạn nhận 429 | SD-15 | Test tích hợp gửi quá giới hạn với `FakeTimeProvider` |
| Hội thoại cũ hơn 90 ngày bị xóa cùng tin và phiên | SD-17 | Test gọi processor với `FakeTimeProvider` |
| "Gợi ý trả lời" trả bản nháp có nguồn từ kho tri thức | Đã bỏ ngày 2026-10-18 | Không kiểm |
| Không có bản nháp nào được gửi mà nhân viên không bấm gửi | Đã bỏ ngày 2026-10-18 | Không kiểm |
| Báo cáo hiện đúng thời gian phản hồi đầu tiên p50/p90 và CSAT trung bình với dữ liệu test | SD-01, SD-13 | Unit test phân vị; test tích hợp `ISupportMetrics` trên dữ liệu dựng sẵn. Trang báo cáo kiểm ở `tasks/admin-console/plan.md` |
| Coverage ít nhất 80% | SD-14 | Coverage gộp unit và tích hợp, cách đo như shipping |

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
| --- | --- | --- |
| Route khách mở công khai bị spam | DB đầy tin rác, nhân viên mất thời gian | Quyết định 27: giới hạn theo `anonymous_id`; tin tối đa 2.000 ký tự |
| Khách xóa cookie `pk_anon` | Khách vãng lai mất hội thoại đang mở | Ghi rõ trên `/support`: đăng nhập để giữ hội thoại. Hội thoại cũ vẫn tự xóa sau 90 ngày |
| SSE bị proxy giữ đệm | Thông báo trễ quá 3 giây | Gửi header `Cache-Control: no-cache` và `X-Accel-Buffering: no`; E2E đo qua Vite proxy |
| Hub SSE chỉ sống trong một instance | Chạy nhiều instance làm mất thông báo | Ghi giới hạn MVP một instance, giống ai-gateway |
| Biên giờ làm việc sai múi giờ | Báo ngoài giờ lúc đang trong giờ | Unit test 7:59, 8:00, 20:59, 21:00, ngày nghỉ với `FakeTimeProvider` |
| Thiếu cổng đọc người dùng ở identity | Bảng thông tin khách thiếu tên và email | SD-11 chờ Câu hỏi mở 2; các task khác không chờ |

## Câu hỏi mở

1. Route REST thay route hành động. Spec dùng `/claim`, `/transfer`, `/close`, `/csat`, `/queue`. Plan đề xuất bảng ở quyết định 11, gồm thêm `GET /api/admin/support/presence` và bốn route phía khách. Người dùng duyệt bảng này không? Nếu duyệt, cần cập nhật `spec/SPEC-support-desk.md` trước SD-06.
2. Cổng đọc người dùng ở identity. Bảng thông tin khách cần tên và email. Tin nhân viên cần tên ngắn, ví dụ "Lan - CSKH". Đề xuất thêm `IUserDirectory` vào `Pickory.Modules.Identity.Contracts`:
   - `GetCustomerContactAsync(string userId, CancellationToken)` trả tên và email;
   - `GetStaffDisplayNamesAsync(IReadOnlyList<string> staffIds, CancellationToken)` trả tên hiển thị của nhân viên.
   Đây là thay đổi Contracts, thuộc "Ask first". Tên ngắn lấy từ đâu cũng cần chốt: tên đầu của hồ sơ, hay một trường mới do nhân viên tự đặt.
3. Đơn của khách vãng lai. Spec của `commerce` chỉ có `getOrdersForCustomer(userId, limit)`. Bảng thông tin khách cần đơn theo `anonymousId`. Dùng `ICommerceReader.GetOrdersForAnonymousAsync(string anonymousId, int limit, CancellationToken)`. `tasks/commerce/todo.md` đã thêm hàm này vào Contracts của `commerce`.
4. Đã bỏ ngày 2026-10-18. Module không còn dùng `IConversationService`.
5. Hội thoại ngoài giờ chờ tới khi nào? Ngoài giờ không có ai nhận, nên phiên chờ kéo dài tới sáng hôm sau. Đề xuất: khách gửi số điện thoại thì đóng phiên ngay. Khách không gửi thì phiên tự đóng sau 30 phút.
6. Đã chốt bằng quyết định 26: module tự xóa hội thoại, tin và phiên cũ hơn 90 ngày.
7. Ước tính thời gian chờ khi chưa có dữ liệu. Plan dùng 5 phút mỗi phiên (quyết định 9). Người dùng muốn mức khác không?
8. Mốc nhắc lại 10 phút và tự đóng 30 phút. Đây là Open Question 1 của spec. Plan giữ mặc định của spec và đặt hai mốc trong `support_settings`.
9. Phân loại hội thoại. Đây là Open Question 2 của spec. Plan giữ mặc định: chưa làm.
10. Giới hạn tin của khách. Đã chốt ngày 2026-10-18: 10 tin mỗi phút và 200 tin mỗi ngày theo `anonymous_id` (quyết định 27).
11. Thông tin khách vãng lai. Đã chốt ngày 2026-10-18: khách không phải nhập tên hay số điện thoại trước khi chat. Nhân viên hỏi trong lúc chat khi cần.
12. Lối vào `/support`. Đã chốt ngày 2026-10-18: link "Liên hệ CSKH" ở chân trang và trên `/help`. Thanh đầu trang không có link này. Fallback của `chatbot-rag` trỏ tới cùng trang.
    - Ngày 2026-10-20: `knowledge-base` làm sau module này. Module này dựng chân trang và đặt link ở đó.
    - Link trên `/help` do KB-UI1 của `tasks/knowledge-base/todo.md` thêm sau.
13. Ngữ cảnh từ chatbot. Khách mở `/support` từ fallback của `chatbot-rag` thì nhân viên không thấy câu khách đã hỏi bot. Đề xuất: MVP chưa chuyển ngữ cảnh. Việc này mở lại khi `chatbot-rag` có human handoff.
