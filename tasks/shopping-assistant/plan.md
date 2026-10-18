# Implementation Plan: shopping-assistant

> **Trạng thái: đã bỏ ngày 2026-10-18.** Người dùng chuyển chatbot sang dự án riêng `chatbot-rag`, gắn vào Pickory bằng widget.
>
> Phần thay thế nằm ở `tasks/chatbot-widget/`. Chat với CSKH chuyển sang `tasks/support-desk/`. File này chỉ giữ để tra cứu lịch sử.

## Overview

Module `shopping-assistant` là chatbot tư vấn mua sắm theo kiểu RAG có tool calling. Mỗi câu trả lời chỉ dựa trên kết quả tool của lượt đó. Lượt là một tin của khách và câu trả lời của bot. Bot nêu giá, thông số, chính sách và trạng thái đơn kèm tham chiếu kiểm được.

Module đứng gần cuối thứ tự build. Module dùng cổng của `catalog`, `commerce`, `activity-tracking`, `knowledge-base` và `ai-gateway`. Ngày 2026-10-14 người dùng bỏ `smart-search`, `recommendations` và `personalized-content`, nên plan không còn dùng cổng của ba module này. `support-desk` đi sau và dùng `IConversationService` của module này.

Spec gốc: `spec/SPEC.md`, `spec/SPEC-shopping-assistant.md`. Spec còn viết theo bản Node, Vercel AI SDK và Zod. Plan này chuyển interface sang .NET theo `tasks/dotnet-migration/plan.md`.
Danh sách task chi tiết: `tasks/shopping-assistant/todo.md`.

## Phần đã phủ bởi plan cũ

Các phần sau đã có trong `ai-gateway`. Plan này dùng lại, không tạo task lặp:

1. **Vòng lặp tool nhiều bước.** AI-07 trong `tasks/identity-ai-gateway/todo.md` và DN-06 trong `tasks/dotnet-migration/todo.md` đã làm `ToolLoopRunner`. Mỗi bước qua kiểm tra ngân sách, ẩn PII và ghi đúng 1 dòng `ai_usage_logs`.
2. **Ẩn PII trong kết quả tool.** `RedactedToolDefinition` của gateway ẩn SĐT và email trong `ToolCompleted.Value` trước khi gửi lại cho model.
3. **Hạn mức chat theo người.** `ChatRateLimiter` áp 50 tin mỗi ngày cho `user:<id>` và 15 tin cho `anon:<id>`. Mỗi lần gọi `ChatStreamAsync` tính 1 tin.
4. **Stream.** `IAiGateway.ChatStreamAsync` đã chạy vòng lặp tool khi stream. Gateway có timeout token đầu 10 giây và timeout chờ chunk 15 giây (`docs/adr/0002-ai-gateway-provider-findings.md`).
5. **Mã tính năng.** `AiFeature.ShoppingAssistantChat` và `AiFeature.ShoppingAssistantSummarize` đã có, thuộc bậc ưu tiên cao của ngân sách.
6. **Quyền.** `conversations:read` và `reports:read` đã có trong `Permissions` của `Pickory.Modules.Identity.Contracts`.
7. **Fake provider có kịch bản.** `FakeChatProvider` nhận kịch bản qua `EnqueueStream` và `EnqueueHandler`. Test tích hợp dùng hai hàm này để trả tool call rồi câu trả lời.

Phần còn mở của plan cũ liên quan tới module này: AI-00 và live test tool calling của AI-07 chưa chạy với key thật. ADR 0002 ghi rõ chưa xác minh tool calling của DeepSeek bằng tài khoản dự án. `SA-00` gom phần xác minh này cho chatbot.

## Điều kiện trước

1. `commerce`, `activity-tracking`, `knowledge-base` đã qua Checkpoint cuối của plan riêng (`tasks/<module-id>/plan.md`). `CAT-14` của `tasks/catalog/todo.md` cũng đã xong, vì `search_products` dùng `ProductFilters.Query`.
2. Plan đổi route cũ sang REST đã xong. Route mới của module này theo REST mức tài nguyên ngay từ đầu.
3. Người dùng đã chốt các khoảng trống hợp đồng ở mục "Câu hỏi mở", vì đổi Contracts thuộc nhóm "Ask first".
4. Tiêu chí cần AI thật chờ `LLM_API_KEY`, `LLM_MODEL` và `EMBEDDING_API_KEY`. Các task khác không chờ key.

## Đồ thị phụ thuộc

```
SA-00 xác minh DeepSeek: tool calling và stream (chờ key, không chặn task khác)
SA-01 logic thuần: parser tham chiếu, kiểm tra grounding, dựng ngữ cảnh, luật chuyển CSKH
 └── SA-02 khung module, Contracts, schema, migration
      └── SA-03 hội thoại: lấy hoặc tạo, lịch sử, quyền chủ hội thoại, tự đóng 24 giờ
           ├── SA-04 tool sản phẩm (search, details, compare)
           ├── SA-05 tool review, tài liệu
           ├── SA-06 tool đơn hàng (kiểm quyền sở hữu)
           │    └── SA-07 lượt hỏi đáp: stream SSE, vòng lặp tool, system prompt có phiên bản
           │         ├── SA-08 kiểm tra grounding sau trả lời và lưu tin
           │         │    └── SA-09 tóm tắt tin cũ
           │         ├── SA-UI1 widget chat ── SA-UI2 thẻ sản phẩm, so sánh, trích dẫn
           │         └── SA-10 chuyển CSKH
           │              ├── SA-11 kênh SSE `/events`
           │              │    └── SA-12 `IConversationService` cho `support-desk`
           │              └── SA-UI3 chuyển CSKH, tin hệ thống, phản hồi trên widget
           ├── SA-13 phản hồi thích, không thích
           ├── SA-14 xóa hội thoại sau 90 ngày và khi xóa tài khoản
           └── SA-15 API back-office: xem hội thoại, số liệu chatbot
SA-16 kiểm tra bảo mật (prompt injection, đơn người khác) ← SA-08, SA-06
SA-17 bộ đánh giá `chat:eval` ← SA-08
SA-E2E1, SA-E2E2 ← SA-UI3
SA-18 nghiệm thu ← mọi task
```

`SA-07` cần đủ 3 nhóm tool (`SA-04`, `SA-05`, `SA-06`), vì system prompt mô tả toàn bộ tool. `SA-12` cần `SA-11`, vì tin của nhân viên phải tới khách qua kênh `/events`.

## Architecture Decisions

1. **Cấu trúc theo `tasks/dotnet-migration/plan.md`.** Hai project mới: `Pickory.Modules.ShoppingAssistant` và `Pickory.Modules.ShoppingAssistant.Contracts`. `ShoppingAssistantModule.cs` có `AddShoppingAssistantModule` và `MapShoppingAssistantEndpoints`. `Pickory.ArchitectureTests` thêm luật cho module mới.
2. **Chuyển interface từ bản Node sang .NET.**
   - `conversations` thành `IConversationService` trong Contracts: `GetAsync`, `ListAsync`, `AppendStaffMessageAsync`, `AssignAsync`, `CloseHumanSessionAsync`, `AppendSystemMessageAsync`. Mỗi hàm nhận `CancellationToken`.
   - `Actor` của bản Node thành `Actor` của `Pickory.Modules.Identity.Contracts`. `AppendStaffMessageAsync` nhận `Actor` của nhân viên.
   - `onHandoffRequested` thành `HandoffRequestedEvent(ConversationId, Reason, OccurredAt)`. `onCustomerMessage` thành `CustomerMessageEvent(ConversationId, MessageId, OccurredAt)`. Hai sự kiện là `record` cài `IDomainEvent`, phát qua `DomainEventCollector` sau commit.
   - Tool khai báo bằng `ToolDefinition<TParameters>` của AiGateway.Contracts. Tham số kiểm bằng FluentValidation, thay Zod.
   - Vòng lặp tool đi qua `IAiGateway.ChatStreamAsync` với `ChatRequest.MaxSteps = 4`, thay `maxSteps` của Vercel AI SDK.
   - Lệnh `pnpm chat:eval` thành `dotnet run --project apps/backend/src/Pickory.Api -- chat:eval`.
3. **Actor gắn ở server qua closure.** Mỗi lượt dựng bộ tool mới, gắn `Actor` của request vào closure của tool. Tham số tool không có trường định danh khách. Cách này theo chú thích của `ToolDefinition`.
4. **Giới hạn 6 lần gọi tool mỗi lượt.** Gateway chỉ giới hạn số bước (`MaxSteps`). Module đếm lần gọi trong bộ tool của lượt. Lần gọi thứ 7 trở đi trả `ToolCompleted` với thông báo "đã hết lượt tra cứu", không chạy truy xuất.
5. **Thu kết quả tool để kiểm grounding.** Bộ tool ghi mọi kết quả vào bộ thu của lượt. Bộ thu giữ id sản phẩm, giá từng SKU, đoạn tài liệu và đoạn review theo số `[n]`. Đoạn tài liệu và đoạn review của một lượt dùng chung một dãy số. Bước kiểm grounding đọc bộ thu này, không đọc lại text của model.
6. **Kết quả tool bọc trong khối phân cách.** Mỗi kết quả tool là JSON nằm trong thẻ `<tool_data>`. System prompt nói rõ nội dung trong thẻ là dữ liệu, không phải chỉ dẫn.
7. **Stream bằng SSE tự viết, không thêm package.** Endpoint gửi tin đặt `Content-Type: text/event-stream` rồi ghi từng sự kiện vào `HttpResponse.Body`.
   - `event: delta` mang một đoạn text.
   - `event: done` mang `messageId`, `productRefs` đã xác nhận, `citations`, `groundingFlags`.
   - `event: error` mang mã lỗi khi stream đứt giữa chừng.
   - Frontend đọc stream bằng `fetch` và `ReadableStream`, vì `EventSource` không gửi được POST.
8. **Lỗi trước token đầu trả HTTP, lỗi sau token đầu trả `event: error`.** Module đọc chunk đầu tiên trước khi ghi header SSE.
   - Hết hạn mức (`AiBudgetExceededException`): 429, mã `CHAT_LIMIT_REACHED`, thông báo tiếng Việt thân thiện. Gateway chặn trước khi gọi provider.
   - AI lỗi hoặc circuit mở (`AiUnavailableException`): 503, mã `ASSISTANT_UNAVAILABLE`. Widget hiện "Trợ lý tạm thời không khả dụng" và nút gặp nhân viên.
   - Tin của khách luôn được lưu trước khi gọi gateway, nên nhân viên vẫn đọc được tin đó.
9. **Thẻ sản phẩm chỉ hiện sau `event: done`.** Khi stream, frontend thay `{{product:id}}` bằng khung chờ. Sau `done`, frontend chỉ render thẻ cho id có trong `productRefs` đã xác nhận. Thẻ lấy giá từ API của `catalog`, không lấy từ text của model.
10. **Kiểm grounding là hàm thuần.** Hàm nhận text đầy đủ, bộ thu kết quả tool và tin của khách. Hàm trả text đã gỡ id lạ cùng danh sách cờ `unknown_product`, `price_mismatch`, `missing_citation`.
    - Số tiền nhận dạng: `24.990.000đ`, `24.990.000 đ`, `25 triệu`, `25tr`, `25 củ`.
    - Số tiền hợp lệ khi lệch dưới 1% so với một giá trong bộ thu, hoặc trùng ngưỡng khách tự nêu trong tin của lượt.
11. **Đếm "không có thông tin" bằng dấu hiệu `{{no_info}}`.** System prompt yêu cầu model chèn `{{no_info}}` khi thiếu dữ liệu. Parser gỡ dấu hiệu trước khi lưu và đếm số lượt liên tiếp. Hai lượt liên tiếp có dấu hiệu thì module chuyển CSKH.
12. **Trạng thái hội thoại là máy trạng thái nhỏ.** `bot` sang `waiting_human` khi chuyển CSKH. `waiting_human` sang `human` khi nhân viên nhận. `human` sang `bot` khi nhân viên kết thúc phiên. Mọi trạng thái sang `closed` khi quá 24 giờ không hoạt động. Chuyển không hợp lệ trả 409, mã `INVALID_CONVERSATION_STATE`.
13. **Không gọi LLM khi đang `waiting_human` hoặc `human`.** Tin của khách lưu với vai `user`, rồi module phát `CustomerMessageEvent`. Response trả 202, không mở stream.
14. **Chủ hội thoại.** Khách là chủ khi `userId` trùng, hoặc hội thoại chưa gắn `userId` và `anonymousId` trùng. Người không phải chủ nhận 404, mã `CONVERSATION_NOT_FOUND`. Mã 404 không tiết lộ hội thoại có tồn tại.
15. **Gắn hội thoại vãng lai khi đăng nhập.** Module nghe `UserLinkedEvent`. Hội thoại đang mở của `anonymousId` được gắn `userId`. Spec chưa nói rõ trường hợp này, xem Câu hỏi mở 6.
16. **Tự đóng hội thoại khi đọc.** `POST /api/chat/conversations` thấy hội thoại đang mở có `lastMessageAt` cũ hơn 24 giờ thì đóng hội thoại đó và tạo hội thoại mới. Job hằng ngày đóng nốt hội thoại còn sót.
17. **Kênh `/events` dùng hub trong bộ nhớ.** `ConversationEventHub` giữ một `Channel` cho mỗi kết nối. `AppendStaffMessageAsync` và đổi trạng thái đẩy sự kiện vào hub sau commit. Kết nối gửi dòng chú thích giữ kết nối mỗi 15 giây. MVP chạy 1 instance, giống `ChatRateLimiter`.
18. **Tóm tắt tin cũ chạy sau `event: done`.** Khi số tin chưa được tóm tắt vượt 20, module gọi `ChatAsync` với `AiFeature.ShoppingAssistantSummarize`. Lỗi tóm tắt chỉ ghi log và giữ bản tóm tắt cũ.
19. **Ngữ cảnh gửi model.** Ngữ cảnh gồm system prompt, tóm tắt hồ sơ, trang khách đang xem, bản tóm tắt tin cũ và 10 tin gần nhất. Tóm tắt hồ sơ lấy từ `IProfileReader.GetProfileSummaryAsync` và chỉ gửi khi `Actor.Consent.Personalization` là `true`. Trang đang xem do client gửi và server kiểm id bằng `ICatalogReader`.
20. **System prompt có phiên bản.** Prompt nằm trong file của module, mã phiên bản dạng `chat@1`. Mỗi tin của bot lưu `promptVersion`. `CallContext.PromptVersion` mang cùng mã này.
21. **Tra đơn chỉ trả dữ liệu đã lược bớt.** Tool đơn hàng trả mã đơn, trạng thái, ngày, mã vận đơn, danh sách sản phẩm. Tool không trả SĐT, email, địa chỉ đầy đủ.
22. **Route theo REST thuần.** Route theo hành động của spec được đổi như sau. Đây là mâu thuẫn với spec, chờ người dùng chốt (Câu hỏi mở 1).
    - `POST /api/chat/conversations/{id}/handoff` thành `PATCH /api/chat/conversations/{id}` với `{ "status": "waiting_human", "reason": "..." }`. Khách chỉ được đặt `waiting_human`.
    - `POST /api/chat/messages/{id}/feedback` thành `PUT /api/chat/messages/{id}/feedback`. Phản hồi là tài nguyên con duy nhất của tin.
    - Các route còn lại của spec đã là danh từ và giữ nguyên.
23. **Mã lỗi.** `CONVERSATION_NOT_FOUND` (404), `INVALID_CONVERSATION_STATE` (409), `CHAT_LIMIT_REACHED` (429), `ASSISTANT_UNAVAILABLE` (503). Tin dài hơn 1.000 ký tự trả 400 `VALIDATION_ERROR`. Thông báo viết tiếng Việt.
24. **Lưu trữ.** Tin lưu bản gốc để CSKH đọc. Bản gửi model luôn qua `PiiRedactor` của gateway. Job hằng ngày xóa hội thoại cũ hơn 90 ngày. `UserDeletedEvent` xóa hội thoại của người dùng.
25. **Bộ đánh giá nằm trong module.** 60 kịch bản ở `Pickory.Modules.ShoppingAssistant/Evaluation/scenarios.json`. Lệnh `chat:eval` chỉ chạy khi `AI_LIVE_TESTS=1`. Chấm bằng LLM giám khảo chỉ để tham khảo.
26. **`search_products` dùng bộ lọc của `catalog` (đổi ngày 2026-10-14).** Model trích từ khóa và bộ lọc có cấu trúc từ câu hỏi, theo mục "Tìm sản phẩm bằng bộ lọc" của spec.
    - Tool đổi tham số thành `ProductFilters`, với `keyword` vào trường `Query`.
    - Tool gọi `ICatalogReader.ListProductsAsync` với `limit` bằng 8.
    - Tool gọi `GetProductAsync` cho từng sản phẩm để lấy 4 thông số chính, vì `ProductSummary` không có thông số.
    - Bộ lọc sai trả kết quả từ chối cho model, không ném lỗi.
    - Không có tìm sản phẩm theo ý nghĩa (vector).
27. **Không có tool `recommend` (đổi ngày 2026-10-14).** Khi cần sản phẩm thay thế, model gọi lại `search_products` với bộ lọc khác. System prompt `chat@1` mô tả cách này.
28. **`get_review_insights` chỉ đọc `commerce` (đổi ngày 2026-10-14).** Tool gọi `ICommerceReader.GetRatingSummaryAsync` và `GetVisibleReviewsAsync` (COM-22 của `tasks/commerce/todo.md`).
    - Tool trả điểm trung bình, phân bố số sao và tối đa 5 đoạn review thật.
    - Đoạn có từ của `aspect` đứng trước, so sánh không dấu và không phân biệt hoa thường. Không có đoạn khớp thì tool trả 5 review mới nhất kèm `aspectMatched: false`.
    - Tool không gọi LLM để tóm tắt. Model của lượt chat tự tóm tắt và trích dẫn đoạn review bằng `[n]`.

## Task List

Chi tiết từng task ở `tasks/shopping-assistant/todo.md`.

### Phase 0: Xác minh DeepSeek
- [ ] SA-00 (chờ key, không chặn các task khác)

### Phase 1: Nền tảng
- [ ] SA-01 · [ ] SA-02 · [ ] SA-03

### Checkpoint SA1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `ShoppingAssistant` tham chiếu module khác ngoài Contracts
- [ ] Review với người dùng

### Phase 2: Trả lời có truy xuất
- [ ] SA-04 · [ ] SA-05 · [ ] SA-06 · [ ] SA-07 · [ ] SA-08 · [ ] SA-09 · [ ] SA-UI1 · [ ] SA-UI2

### Checkpoint SA2
- [ ] Khách hỏi trong widget, thấy câu trả lời stream, thẻ sản phẩm và trích dẫn với fake provider có kịch bản
- [ ] Id sản phẩm lạ không thành thẻ, số tiền sai bị gắn cờ
- [ ] Review với người dùng

### Phase 3: Chuyển CSKH và vận hành
- [ ] SA-10 · [ ] SA-11 · [ ] SA-12 · [ ] SA-13 · [ ] SA-14 · [ ] SA-15 · [ ] SA-UI3

### Checkpoint SA3
- [ ] Chuyển CSKH đổi trạng thái, bot ngừng trả lời, tin nhân viên tới widget qua `/events`
- [ ] `support-desk` có đủ hàm của `IConversationService` để bắt đầu plan riêng
- [ ] Review với người dùng

### Phase 4: Bảo mật, đánh giá và nghiệm thu
- [ ] SA-16 · [ ] SA-17 · [ ] SA-E2E1 · [ ] SA-E2E2 · [ ] SA-18

### Checkpoint cuối
- [ ] Mọi dòng Traceability đạt, trừ dòng ghi "chờ key"
- [ ] Coverage dòng của `Pickory.Modules.ShoppingAssistant` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `support-desk`

## Traceability: Success Criteria → Task

| Success Criteria (spec) | Task | Cách kiểm |
| --- | --- | --- |
| Bộ 60 kịch bản: ít nhất 85% đạt mọi tiêu chí tự động. Kịch bản injection và đơn người khác đạt 100% | SA-17, SA-16 | `chat:eval` với AI thật (chờ key). Phần prompt injection và đơn người khác có thêm test tích hợp với fake provider ở SA-16 |
| Tỉ lệ tin bị gắn cờ `price_mismatch` hoặc `unknown_product` dưới 2% trên bộ đánh giá | SA-08, SA-17 | Unit test bộ kiểm grounding; `chat:eval` in tỉ lệ cờ (chờ key) |
| Mọi câu trả lời chính sách trong bộ đánh giá có trích dẫn hợp lệ, link mở đúng đoạn | SA-05, SA-08, SA-UI2, SA-17, SA-E2E1 | Unit test cờ `missing_citation`; E2E mở link trích dẫn; `chat:eval` (chờ key) |
| Token đầu tiên dưới 3 giây (p95, lượt có 1 vòng tool) | SA-07, SA-00, SA-17 | Test tích hợp đo thời gian tới `event: delta` đầu với fake provider; đo p95 với DeepSeek thật trong `chat:eval` (chờ key) |
| Khách vãng lai không tra được đơn từ trình duyệt khác; khách đăng nhập không tra được đơn người khác | SA-06, SA-16 | Test tích hợp gọi tool với `anonymousId` khác và `userId` khác, kết quả "không tìm thấy" |
| Tin nhân viên hiện trong widget trong 2 giây; bot không trả lời khi ở chế độ người thật | SA-10, SA-11, SA-12, SA-E2E1 | Test tích hợp đo thời gian từ `AppendStaffMessageAsync` tới sự kiện SSE; test không gọi provider khi `waiting_human`. E2E với nhân viên thật thuộc `support-desk` |
| Tắt API AI: widget báo "Trợ lý tạm thời không khả dụng", có nút gặp nhân viên, phần còn lại của trang không ảnh hưởng | SA-07, SA-UI3, SA-E2E2 | Test tích hợp trả 503 `ASSISTANT_UNAVAILABLE`; E2E với provider lỗi |
| Dashboard hiện số hội thoại, tỉ lệ thích, tỉ lệ chuyển CSKH, chi phí trung bình mỗi hội thoại, tỉ lệ chuyển đổi từ chat | SA-15 | Test tích hợp API số liệu trên dữ liệu dựng sẵn. Màn dashboard thuộc `tasks/admin-console/plan.md` |
| Coverage ít nhất 80% | SA-18 | Coverage gộp unit và tích hợp, cách đo như shipping |

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
| --- | --- | --- |
| DeepSeek gọi tool không ổn định | Bot gọi sai tool hoặc bỏ qua tool, câu trả lời thiếu căn cứ | `SA-00` xác minh trước. Phương án dự phòng là bước định tuyến bằng `ChatJsonAsync` (Open Question 1 của spec) |
| Chưa có key AI | Không đo được p95 token đầu và bộ 60 kịch bản | Mọi task chạy bằng fake provider có kịch bản. Tiêu chí cần AI thật để mở, ghi lý do ở `SA-18` |
| Prompt injection qua review hoặc tài liệu | Bot nói sai giá hoặc làm theo lệnh lạ | Khối `<tool_data>`, thẻ giá lấy từ `catalog`, cờ grounding, test injection ở `SA-16` |
| Hub sự kiện trong bộ nhớ | Chạy nhiều instance thì khách mất tin nhân viên | Ghi ràng buộc 1 instance như `ChatRateLimiter`. Đổi sang kênh dùng chung khi tăng instance |
| Cổng của module trước thiếu hàm | Tool tra đơn của khách vãng lai hoặc số liệu chi phí không làm được | Đề xuất hàm cụ thể ở Câu hỏi mở 2 đến 4, chốt trước Phase 2 |
| Stream đứt giữa chừng | Khách thấy câu trả lời dở | `event: error` có mã lỗi. Tin dở không lưu thành tin của bot |
| Model trích bộ lọc sai từ câu hỏi | `search_products` trả sản phẩm lệch nhu cầu hoặc trả rỗng | Tool trả lý do từ chối để model sửa bộ lọc. System prompt có danh sách danh mục và thuộc tính (Câu hỏi mở 10). `chat:eval` chấm sản phẩm theo danh mục và giá |

## Câu hỏi mở

1. **Route REST thay route hành động của spec (mâu thuẫn với spec).** Đề xuất ở quyết định 22: `PATCH /api/chat/conversations/{id}` thay `/handoff`, `PUT /api/chat/messages/{id}/feedback` thay `POST .../feedback`. Người dùng chốt trước `SA-10` và `SA-13`.
2. **`ICommerceReader` cần tra đơn theo khách vãng lai.** Spec của `commerce` chỉ có `GetOrdersForCustomer(userId)` và `GetOrderByCode(code)`. Tool `get_my_orders` của khách vãng lai cần đơn theo `anonymousId`. Dùng `GetOrdersForAnonymousAsync` mà `tasks/commerce/todo.md` đã thêm cho `support-desk`. `OrderDetail` cần có `UserId` và `AnonymousId` để `get_order_status` kiểm quyền sở hữu.
3. **AiGateway.Contracts chưa có cổng đọc chi phí.** Dashboard cần chi phí trung bình mỗi hội thoại. Dùng `IAiUsageReader.GetDailyUsageAsync(DateRange, CancellationToken)` mà `tasks/admin-console/plan.md` đề xuất, rồi cộng `costUsd` theo feature. Module chia tổng chi phí `shopping-assistant.*` cho số hội thoại trong kỳ.
4. **Cổng số liệu chatbot cho `admin-console`.** Đề xuất `IChatInsights.GetDailyAsync(DateRange, CancellationToken)` trong Contracts của module này, cùng tên với `tasks/admin-console/plan.md`, cùng số liệu với `GET /api/admin/chat/insights`. Tên này chưa có trong danh sách tên thống nhất giữa các module.
5. **Kịch bản fake cho E2E.** E2E chạy API trong process riêng, nên test không gọi được `EnqueueStream`. Đề xuất provider fake theo từ khóa, chỉ đăng ký trong `Pickory.Api/E2e/`. Cách này cần thêm điểm mở rộng trong `AiGatewayModule`, thuộc nhóm đổi module khác.
6. **Hội thoại vãng lai khi khách đăng nhập.** Spec không nói hội thoại đang mở có theo khách sang tài khoản không. Mặc định đề xuất: gắn `userId` khi nhận `UserLinkedEvent` (quyết định 15).
7. **Màn back-office rà soát hội thoại chatbot.** Spec chỉ có API `GET /api/admin/chat/conversations`. Mặc định đề xuất: chưa làm màn riêng, nhân viên đọc lịch sử qua màn của `support-desk`.
8. **Câu hỏi mở của spec giữ mặc định.** Bot không chủ động mở lời. Lưu hội thoại 90 ngày chờ xác nhận chính sách của shop. Giờ làm việc của CSKH thuộc `support-desk`.
9. **Phiên bản tài liệu đang được trích dẫn.** `knowledge-base` không được xóa phiên bản đang được trích dẫn, nhưng `knowledge-base` không được gọi module này vì sẽ tạo vòng phụ thuộc. Đề xuất: `knowledge-base` giữ mọi dòng phiên bản, chỉ xóa chunk.
10. **Danh mục và thuộc tính cho bộ lọc của `search_products`.** Model cần slug danh mục và khóa thuộc tính `filterable`. `ICatalogReader` chưa có hàm đọc danh sách danh mục. Đề xuất: thêm hàm này vào Contracts của `catalog`, theo Open Question 5 của `spec/SPEC-shopping-assistant.md`. Đây là đổi Contracts, thuộc nhóm "Ask first".
11. **Cách chọn đoạn review theo `aspect`.** Quyết định 28 so khớp từ của `aspect` trong nội dung review. `GetVisibleReviewsAsync` chỉ phân trang, không lọc theo từ. Đề xuất: tool đọc tối đa 50 review mới nhất. Tool lọc 50 review đó trong bộ nhớ. Người dùng chốt trước `SA-05`.
