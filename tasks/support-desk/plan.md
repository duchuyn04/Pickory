# Implementation Plan: support-desk

## Overview

Module `support-desk` là bàn làm việc của nhân viên CSKH. Nhân viên nhận hội thoại chatbot chuyển sang, trả lời khách realtime, xem đơn của khách và nhờ AI soạn nháp từ kho tri thức. Module cũng quản lý giờ làm việc, yêu cầu gọi lại ngoài giờ, mẫu trả lời nhanh và số liệu CSKH.

`support-desk` phụ thuộc `identity`, `commerce`, `shopping-assistant`, `knowledge-base`, `ai-gateway`. Module đứng sau `shopping-assistant` và trước `admin-console` trong thứ tự build.

Spec gốc: `spec/SPEC.md`, `spec/SPEC-support-desk.md`. Spec còn viết interface theo bản Node. Plan này chuyển interface sang .NET theo `tasks/dotnet-migration/plan.md`.
Danh sách task chi tiết: `tasks/support-desk/todo.md`.

## Phần đã phủ bởi plan cũ

Các mục dưới đây đã có trong code hoặc trong plan cũ. Plan này không lặp lại task của các mục đó.

1. **Quyền.** `conversations:read`, `conversations:reply`, `support-settings:manage` đã có trong `Permissions.cs` và `PermissionCatalog.cs` (ID-01 của `tasks/identity-ai-gateway/todo.md`). Spec ghi `supportPermissions` là export riêng của module. Quyết định 1 của `tasks/identity-ai-gateway/plan.md` đã thay cách này bằng danh mục chung của `identity`.
2. **Role mặc định.** `DefaultRoles.cs` cho `support` hai quyền `conversations:read` và `conversations:reply`. `sales` không có `conversations:read`. Bảng này khớp spec, nên test "sales gọi hàng chờ nhận 403" dùng seed có sẵn.
3. **Mục menu và trang giữ chỗ.** Mục "CSKH" trỏ tới `/admin/conversations` đã có trong `apps/frontend/src/features/identity/admin/admin-navigation.ts`. `apps/frontend/src/app.tsx` đang gắn `AdminPlaceholderPage` cho đường dẫn này. Module này thay trang giữ chỗ bằng màn CSKH thật và giữ nguyên đường dẫn `/admin/conversations`.
4. **Tính năng AI.** `AiFeature.SupportDeskDraftReply` với mã `support-desk.draft-reply` đã có trong `AiGateway.Contracts`. Quyết định 9 của `tasks/identity-ai-gateway/plan.md` đã xếp tính năng này vào bậc ưu tiên thấp, dừng ở 100% ngân sách.
5. **Hạ tầng dùng chung.** `PiiRedactor` (`Pickory.Shared/Privacy/`), luật số điện thoại `ContactFieldRules` (`Pickory.Shared/Validation/`), `IAuditLogWriter`, khung ma trận quyền và hạ tầng E2E đã có. `RateLimiter` chuyển sang `Pickory.Shared` ở SHIP-08 của `tasks/shipping/todo.md`.

## Điều kiện trước

1. `shopping-assistant` đã có `IConversationService`, `HandoffRequestedEvent`, `CustomerMessageEvent` và SSE phía khách. Xem `tasks/shopping-assistant/plan.md`.
2. `knowledge-base` đã có `IKnowledgeRetriever` với `audience` là `staff`. Xem `tasks/knowledge-base/plan.md`.
3. `commerce` đã có `ICommerceReader.GetOrdersForCustomerAsync`. Xem `tasks/commerce/plan.md`.
4. Plan đổi route cũ sang REST đã xong. Plan đó chạy trước `commerce`.
5. Người dùng đã trả lời Câu hỏi mở 1 đến 4. Các câu này đổi Contracts của module khác hoặc đổi route của spec.

## Đồ thị phụ thuộc

```
SD-01 logic thuần: giờ làm việc, ước tính thời gian chờ, biến mẫu, độ giống bản nháp, phân vị
 └── SD-02 khung module, Contracts, schema, migration
      └── SD-03 hub SSE nhân viên + trạng thái sẵn sàng ─────────── SD-UI1 hàng chờ, công tắc, thông báo
           └── SD-04 nhận chuyển giao + job nhắc lại
                ├── SD-05 yêu cầu gọi lại ───────────────────────── SD-UI4 tab Gọi lại; SD-UI6 form SĐT, form CSAT
                └── SD-06 hàng chờ + nhận hội thoại
                     └── SD-07 tin nhân viên + tin mới của khách ── SD-UI2 khung chat, mẫu `/`
                          └── SD-08 chuyển, kết thúc, tự đóng ───── SD-UI3 bảng thông tin khách, hộp thoại chuyển
                               ├── SD-09 CSAT
                               └── SD-11 bảng thông tin khách (chờ Câu hỏi mở 2, 3)
SD-02 └── SD-10 cấu hình + mẫu trả lời nhanh ────────────────────── SD-UI5 màn cấu hình
SD-07 └── SD-12 AI soạn nháp
SD-08, SD-09, SD-12 └── SD-13 `ISupportMetrics`
mọi task └── SD-E2E1, SD-E2E2 └── SD-14 nghiệm thu
```

`SD-04` cần `SD-03`, vì tin chuyển giao phải tới nhân viên qua SSE. `SD-12` cần `SD-07`, vì bản nháp chỉ được dùng khi nhân viên gửi tin. `SD-13` cần dữ liệu phiên, CSAT và bản nháp.

## Architecture Decisions

1. **Cấu trúc theo `tasks/dotnet-migration/plan.md`.** Hai project mới: `Pickory.Modules.SupportDesk` và `Pickory.Modules.SupportDesk.Contracts`. `SupportDeskModule.cs` có `AddSupportDeskModule` và `MapSupportDeskEndpoints`. `SupportDeskDbContext` ghi lịch sử migration vào schema `ef_support_desk`. `Pickory.ArchitectureTests` thêm luật cho module mới.
2. **Chuyển interface từ Node sang .NET.**
   - `supportMetrics.get(range)` thành `ISupportMetrics.GetAsync(DateRange range, CancellationToken cancellationToken)`, trả `SupportMetricsSnapshot`.
   - `supportMetrics.pendingCount()` thành `ISupportMetrics.GetPendingCountsAsync(CancellationToken cancellationToken)`, trả `SupportPendingCounts(int Waiting, int Callbacks)`.
   - `DateRange` dùng kiểu của `Pickory.Shared` nếu `commerce` đã thêm. Nếu chưa có, task SD-02 thêm `DateRange` vào `Pickory.Modules.SupportDesk.Contracts` và ghi chú để `admin-console` gộp sau.
   - `onHandoffRequested` và `onCustomerMessage` thành `IDomainEventHandler<HandoffRequestedEvent>` và `IDomainEventHandler<CustomerMessageEvent>`.
   - `conversations.*` của `shopping-assistant` thành `IConversationService` với hàm `...Async` và `CancellationToken`.
   - Spec ghi validate bằng Zod. Backend dùng FluentValidation.
3. **Phiên hỗ trợ là nguồn sự thật của việc nhận hội thoại.** Mỗi lần chuyển giao tạo một dòng `support_sessions`. Chỉ mục unique một phần trên `conversation_id` khi `closed_at` rỗng chặn hai phiên mở cho một hội thoại. Sự kiện chuyển giao gửi trùng không tạo phiên thứ hai.
4. **Nhận hội thoại bằng UPDATE có điều kiện.** Lệnh là `UPDATE support_sessions SET staff_id = @me, claimed_at = now() WHERE id = @id AND staff_id IS NULL AND closed_at IS NULL`. Số dòng bằng 0 nghĩa là người khác đã nhận, API trả 409 `SUPPORT_SESSION_ALREADY_CLAIMED`.
5. **Bù trừ khi gọi `shopping-assistant` lỗi.** Bảng phiên và bảng hội thoại thuộc hai `DbContext`, nên không chung transaction. Module nhận phiên trước, rồi gọi `IConversationService.AssignAsync`. Lời gọi lỗi thì module trả phiên về trạng thái chưa nhận và trả 503 `SUPPORT_UNAVAILABLE`.
6. **Hub SSE trong tiến trình.** `SupportEventHub` giữ một `Channel` cho mỗi kết nối của nhân viên. MVP chạy một instance, giống quyết định 6 của `tasks/identity-ai-gateway/plan.md`. Route SSE dùng `TypedResults.ServerSentEvents` của ASP.NET Core 10, không thêm package.
7. **Trạng thái sẵn sàng theo nhịp tim.** Kết nối SSE cập nhật `staff_presence.last_seen_at` mỗi 30 giây. Job chạy mỗi 30 giây chuyển nhân viên sang `away` khi `last_seen_at` cũ hơn 2 phút. Identity không phát sự kiện đăng xuất. Đăng xuất đóng trang, nên SSE ngắt và job chuyển `away` sau 2 phút.
8. **Giờ làm việc là hàm thuần có `TimeProvider`.** `WorkingHoursCalendar` đổi giờ UTC sang `Asia/Ho_Chi_Minh`, rồi so với khung giờ và ngày nghỉ. Khung giờ là nửa mở: 8:00 thuộc trong giờ, 21:00 thuộc ngoài giờ. "Trong giờ" còn cần ít nhất một nhân viên `available`.
9. **Ước tính thời gian chờ.** N bằng thời gian chờ trung bình 7 ngày nhân số phiên đang chờ cộng 1, chia số nhân viên sẵn sàng, làm tròn lên. Chưa có dữ liệu 7 ngày thì dùng 5 phút cho mỗi phiên. Hằng số này nằm trong `SupportSettings` để admin chỉnh sau.
10. **Phiên lưu thêm cột cho báo cáo.** Spec chưa có các cột này. `support_sessions` thêm `handoff_in_hours`, `customer_user_id`, `anonymous_id`. Báo cáo đếm phiên trong giờ mà không cần tính lại lịch cũ. Hai cột định danh cho phép xóa dữ liệu khi nhận `UserDeletedEvent`.
11. **Route theo REST mức tài nguyên.** Spec còn dùng route đặt theo hành động. Plan đề xuất bảng dưới, chờ người dùng chốt ở Câu hỏi mở 1. Response giữ dạng `{ data }`, không kèm link.

    | Spec | Đề xuất REST |
    | --- | --- |
    | `GET /api/admin/support/queue` | `GET /api/admin/support/sessions?view=waiting\|mine` |
    | `POST .../conversations/:id/claim` | `POST /api/admin/support/sessions/{id}/assignment` (201, hoặc 409 khi đã có người nhận) |
    | `POST .../conversations/:id/transfer` | `PUT /api/admin/support/sessions/{id}/assignment` (người đang giữ phiên đổi người nhận) |
    | `POST .../conversations/:id/close` | `PATCH /api/admin/support/sessions/{id}` với `status: "closed"` |
    | `POST .../conversations/:id/draft` | `POST /api/admin/support/sessions/{id}/drafts` |
    | `POST .../conversations/:id/messages` | `POST /api/admin/support/sessions/{id}/messages` |
    | `GET · PATCH /api/admin/support/callbacks[/:id]` | `GET · PATCH /api/admin/support/callback-requests[/{id}]` |
    | `POST /api/support/conversations/:id/csat` | `PUT /api/support/conversations/{id}/csat` |
    | không có | `GET /api/admin/support/presence` (danh sách nhân viên sẵn sàng cho hộp thoại chuyển) |

    Route giữ như spec: `POST /api/support/callback-requests`, `GET /api/admin/support/events`, `PUT /api/admin/support/presence`, `/api/admin/support/canned-responses[/{id}]`, `GET · PUT /api/admin/support/settings`.
12. **Mã lỗi.** Thông báo viết tiếng Việt.
    - `SUPPORT_SESSION_ALREADY_CLAIMED` (409): phiên đã có người nhận.
    - `SUPPORT_SESSION_NOT_ASSIGNED` (403): người gửi không giữ phiên.
    - `SUPPORT_SESSION_CLOSED` (409): phiên đã kết thúc.
    - `SUPPORT_SESSION_OPEN` (409): khách chấm điểm khi hội thoại chưa có phiên đã kết thúc.
    - `CANNED_SHORTCUT_TAKEN` (409): một người có hai mẫu cùng `shortcut`.
    - `SUPPORT_STAFF_UNAVAILABLE` (409): người nhận chuyển giao không ở trạng thái sẵn sàng.
    - `SUPPORT_DRAFT_UNAVAILABLE` (503): AI lỗi hoặc hết ngân sách.
    - `CALLBACK_LIMIT_REACHED` (429): khách gửi quá 3 yêu cầu gọi lại trong một ngày.
    - `SUPPORT_UNAVAILABLE` (503): lời gọi sang `shopping-assistant` lỗi.
13. **Chủ hội thoại.** Route phía khách đọc `ConversationDetail` qua `IConversationService.GetAsync`, rồi so `UserId` hoặc `AnonymousId` với `Actor`. Không khớp thì trả 404, để không lộ hội thoại có tồn tại.
14. **Giới hạn 3 yêu cầu gọi lại mỗi ngày.** Module đếm dòng `callback_requests` theo `anonymous_id` trong ngày giờ Việt Nam. Bộ đếm nằm trong DB, nên khởi động lại API không xóa giới hạn. Số điện thoại kiểm bằng `ContactFieldRules` và lưu dạng `0xxxxxxxxx`.
15. **Dữ liệu gửi AI.** Bản nháp nhận ba nguồn:
    - 10 tin gần nhất;
    - đoạn tài liệu từ `IKnowledgeRetriever` với `audience` là `staff`;
    - tóm tắt đơn gồm mã, trạng thái, sản phẩm, ngày.

    Module chạy `PiiRedactor` trên cả ba nguồn trước khi gọi `IAiGateway`. Số điện thoại trong `callback_requests` không bao giờ vào prompt.
16. **Tin nhân viên tối đa 2.000 ký tự.** Spec không ghi giới hạn này. Plan chọn 2.000 ký tự, bằng giới hạn nhận xét của review trong `commerce`.
17. **AI không tự gửi.** `POST .../drafts` chỉ trả bản nháp và ghi `draft_logs`. Không có đường code nào gọi `AppendStaffMessageAsync` từ bản nháp. Tin gửi kèm `draftId` thì module tính độ giống và ghi `sent_message_id`.
18. **Độ giống bản nháp.** `DraftSimilarity` tính 1 trừ khoảng cách Levenshtein chia độ dài lớn hơn, trên chuỗi đã chuẩn hóa khoảng trắng. Kết quả nằm trong khoảng 0 đến 1.
19. **Audit log.** Module ghi `IAuditLogWriter` trong cùng transaction cho các hành động sau:
    - `support.session.claim`, `support.session.transfer`, `support.session.close` theo spec.
    - `support.settings.update` và thay đổi mẫu dùng chung, theo luật "Always" của `spec/SPEC.md`.
    - Phiên tự đóng không có nhân viên, nên không ghi audit. Module ghi log `Information` với `closedBy = auto`.
20. **Job nền theo mẫu `ImageDeletionWorker`.** Module có bốn job:
    - nhắc lại phiên chờ quá 10 phút;
    - tự đóng phiên `human` im lặng 30 phút;
    - chuyển `away` khi mất nhịp tim;
    - xóa số điện thoại sau 30 ngày.

    Test gọi thẳng processor với `FakeTimeProvider`, không chờ timer.
21. **Form của khách do module này làm, widget của `shopping-assistant` gắn vào.** `features/support-desk/customer/` có `CallbackRequestForm` và `CsatForm`. Widget chat render form theo `action` của tin hệ thống (`callback_form`, `csat_form`).
22. **Công tắc sẵn sàng trên thanh trên cùng.** Module làm component `PresenceToggle`. Trước khi có `admin-console`, component gắn vào `AdminLayout` hiện tại. `admin-console` chuyển component vào layout mới sau.
23. **Báo cáo CSKH hiện ở `admin-console`.** Module này cung cấp `ISupportMetrics` và test số liệu. Trang báo cáo thuộc `tasks/admin-console/plan.md`.

## Task List

Chi tiết từng task ở `tasks/support-desk/todo.md`.

### Phase 1: Nền tảng
- [ ] SD-01 · [ ] SD-02 · [ ] SD-03 · [ ] SD-UI1

### Checkpoint SD1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `SupportDesk` tham chiếu module khác ngoài Contracts
- [ ] Nhân viên bật "Sẵn sàng" trên màn CSKH, mất kết nối 2 phút thì thành "Tạm vắng"
- [ ] Review với người dùng

### Phase 2: Tiếp nhận và trả lời
- [ ] SD-04 · [ ] SD-05 · [ ] SD-06 · [ ] SD-07 · [ ] SD-UI2 · [ ] SD-08 · [ ] SD-09 · [ ] SD-11 · [ ] SD-UI3 · [ ] SD-UI4 · [ ] SD-UI6

### Checkpoint SD2
- [ ] Khách yêu cầu gặp nhân viên, nhân viên nhận, trả lời, kết thúc, khách chấm điểm trên `pnpm dev`
- [ ] Ngoài giờ, khách để lại số điện thoại và tab Gọi lại hiện yêu cầu
- [ ] Review với người dùng

### Phase 3: Cấu hình, AI soạn nháp và số liệu
- [ ] SD-10 · [ ] SD-UI5 · [ ] SD-12 · [ ] SD-13

### Checkpoint SD3
- [ ] Bản nháp có nguồn từ kho tri thức, nhân viên sửa rồi gửi
- [ ] `ISupportMetrics` trả đúng số đã tính tay trên bộ dữ liệu test
- [ ] Review với người dùng

### Phase 4: E2E và nghiệm thu
- [ ] SD-E2E1 · [ ] SD-E2E2 · [ ] SD-14

### Checkpoint cuối
- [ ] Mọi dòng Traceability đạt, trừ mục ghi "khi có API key"
- [ ] Coverage dòng của `Pickory.Modules.SupportDesk` ít nhất 80%, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `admin-console`

## Traceability: Success Criteria → Task

| Success Criteria (spec) | Task | Cách kiểm |
| --- | --- | --- |
| Khách yêu cầu gặp nhân viên trong giờ: nhân viên sẵn sàng nhận thông báo trong 3 giây | SD-03, SD-04, SD-UI1, SD-E2E1 | Test tích hợp đo thời điểm sự kiện SSE so với lúc phát `HandoffRequestedEvent`; E2E chờ thông báo tối đa 3 giây |
| Ngoài giờ: khách thấy thông báo ngoài giờ và form SĐT; yêu cầu gọi lại xuất hiện trong tab Gọi lại | SD-04, SD-05, SD-UI4, SD-UI6, SD-E2E2 | Test tích hợp với `FakeTimeProvider` lúc 21:00; E2E ngoài giờ |
| 2 nhân viên bấm nhận cùng lúc: đúng 1 người nhận được | SD-06 | Test tích hợp gửi 2 request song song, đếm 1 lần 201 và 1 lần 409 |
| Tin nhân viên đến khách trong 2 giây; sau khi kết thúc phiên, bot trả lời lại | SD-07, SD-08, SD-E2E1 | Test tích hợp đo SSE phía khách qua `IConversationService`; E2E gửi tin cho bot sau khi kết thúc |
| "Gợi ý trả lời" cho câu hỏi đổi trả trả bản nháp có nguồn từ kho tri thức, p95 dưới 8 giây | SD-12, SD-14 | Test tích hợp với fake provider kiểm nguồn; đo p95 khi có API key (chưa có khóa thì để mở) |
| Không có bản nháp nào được gửi mà nhân viên không bấm gửi | SD-12, SD-UI2, SD-E2E1 | Test tích hợp kiểm số tin nhân viên không đổi sau khi tạo bản nháp; Vitest ô soạn thảo; E2E |
| Báo cáo hiện đúng thời gian phản hồi đầu tiên p50/p90, CSAT trung bình, tỉ lệ dùng bản nháp với dữ liệu test | SD-01, SD-13 | Unit test phân vị; test tích hợp `ISupportMetrics` trên dữ liệu dựng sẵn. Trang báo cáo kiểm ở `tasks/admin-console/plan.md` |
| Coverage ít nhất 80% | SD-14 | Coverage gộp unit và tích hợp, cách đo như shipping |

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
| --- | --- | --- |
| Nhận phiên thành công nhưng `AssignAsync` lỗi | Phiên và hội thoại lệch trạng thái | Quyết định 5: trả phiên về chưa nhận, trả 503, test bằng `IConversationService` giả ném lỗi |
| SSE bị proxy giữ đệm | Thông báo trễ quá 3 giây | Gửi header `Cache-Control: no-cache` và `X-Accel-Buffering: no`; E2E đo qua Vite proxy |
| Hub SSE chỉ sống trong một instance | Chạy nhiều instance làm mất thông báo | Ghi giới hạn MVP một instance, giống ai-gateway |
| Biên giờ làm việc sai múi giờ | Báo ngoài giờ lúc đang trong giờ | Unit test 7:59, 8:00, 20:59, 21:00, ngày nghỉ với `FakeTimeProvider` |
| Bản nháp chứa số điện thoại hoặc địa chỉ của khách | Vi phạm luật "Never" | Chạy `PiiRedactor` trước khi gọi AI; test fake provider ghi lại request |
| Thiếu cổng đọc người dùng ở identity | Bảng thông tin khách thiếu tên và email | SD-11 chờ Câu hỏi mở 2; các task khác không chờ |
| Chưa có API key AI | Không đo được p95 bản nháp | Làm bằng fake provider; tiêu chí p95 để mở như mục sandbox của shipping |

## Câu hỏi mở

1. **Route REST thay route hành động.** Spec dùng `/claim`, `/transfer`, `/close`, `/draft`, `/csat`, `/queue`. Plan đề xuất bảng ở quyết định 11, gồm thêm `GET /api/admin/support/presence`. Người dùng duyệt bảng này không? Nếu duyệt, cần cập nhật `spec/SPEC-support-desk.md` trước SD-06.
2. **Cổng đọc người dùng ở identity.** Bảng thông tin khách cần tên và email. Tin nhân viên cần tên ngắn, ví dụ "Lan - CSKH". Đề xuất thêm `IUserDirectory` vào `Pickory.Modules.Identity.Contracts`:
   - `GetCustomerContactAsync(string userId, CancellationToken)` trả tên và email;
   - `GetStaffDisplayNamesAsync(IReadOnlyList<string> staffIds, CancellationToken)` trả tên hiển thị của nhân viên.
   Đây là thay đổi Contracts, thuộc "Ask first". Tên ngắn lấy từ đâu cũng cần chốt: tên đầu của hồ sơ, hay một trường mới do nhân viên tự đặt.
3. **Đơn của khách vãng lai.** Spec của `commerce` chỉ có `getOrdersForCustomer(userId, limit)`. Bảng thông tin khách cần đơn theo `anonymousId`. Dùng `ICommerceReader.GetOrdersForAnonymousAsync(string anonymousId, int limit, CancellationToken)`. `tasks/commerce/todo.md` đã thêm hàm này vào Contracts của `commerce`.
4. **`IConversationService` cần trả chủ hội thoại.** Route phía khách cần `UserId` và `AnonymousId` trong `ConversationDetail`. Màn CSKH cần `productRefs` và trang khách đang xem. Spec của `shopping-assistant` chưa ghi rõ các trường này. Cần ghi vào plan của `shopping-assistant`.
5. **Hội thoại ngoài giờ ở `waiting_human` tới khi nào?** Spec nói bot ngừng trả lời khi chuyển giao. Ngoài giờ không có ai nhận, nên khách bị kẹt tới sáng hôm sau. Đề xuất: khách gửi số điện thoại thì đóng phiên ngay, bot hoạt động lại. Khách không gửi thì phiên tự đóng sau 30 phút.
6. **Thời hạn lưu dữ liệu hỗ trợ.** `shopping-assistant` xóa hội thoại sau 90 ngày, nhưng chưa phát sự kiện xóa. Luật "Never" cấm xóa lịch sử hỗ trợ trước hạn lưu của hội thoại. Đề xuất: module tự xóa `support_sessions` và `draft_logs` cũ hơn 90 ngày, cùng hạn với hội thoại. Phương án khác là `shopping-assistant` phát `ConversationDeletedEvent`.
7. **Ước tính thời gian chờ khi chưa có dữ liệu.** Plan dùng 5 phút mỗi phiên (quyết định 9). Người dùng muốn mức khác không?
8. **Mốc nhắc lại 10 phút và tự đóng 30 phút.** Đây là Open Question 1 của spec. Plan giữ mặc định của spec và đặt hai mốc trong `support_settings`.
9. **Phân loại hội thoại.** Đây là Open Question 2 của spec. Plan giữ mặc định: chưa làm.
