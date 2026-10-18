# Implementation Plan: personalized-content

> **Trạng thái: đã bỏ ngày 2026-10-14.** Người dùng thu hẹp phần AI cá nhân hóa về một tính năng: chatbot tư vấn `shopping-assistant`.
>
> Chatbot đọc review thật của `commerce` khi khách hỏi, rồi tự tóm tắt trong câu trả lời. Hệ thống không còn nội dung viết sẵn theo từng người.
>
> File này chỉ giữ để tra cứu lịch sử. Không làm task nào trong file này. Không tick mục nào trong file này.

## Overview

Module `personalized-content` dùng LLM viết ba loại nội dung: tóm tắt đánh giá, điểm nổi bật theo nhóm nhu cầu, banner trang chủ. Nội dung chỉ dựa trên thông số và đánh giá có thật. Bộ kiểm tra chống bịa chạy trước khi nội dung được hiển thị.

Module tạo nội dung bằng job nền và lưu vào bảng `generated_contents`. Trang của khách chỉ đọc nội dung đã có. Module tạo nội dung theo nhóm nhu cầu, không theo từng người, nên chi phí AI không tăng theo số khách.

`personalized-content` phụ thuộc `catalog`, `commerce`, `activity-tracking` và `ai-gateway`. Module chạy song song với `recommendations`, sau `smart-search`, theo `spec/CAPABILITY-MAP.md`.

Spec gốc: `spec/SPEC.md`, `spec/SPEC-personalized-content.md`. Spec còn viết interface theo bản Node. Plan này chuyển interface sang .NET theo `tasks/dotnet-migration/plan.md`.
Danh sách task chi tiết: `tasks/personalized-content/todo.md`.

## Phần đã phủ bởi plan cũ

1. **Mã tính năng AI đã có.** `AiFeature.PersonalizedContentReviewSummary`, `PersonalizedContentDescription` và `PersonalizedContentBanner` đã có trong `Pickory.Modules.AiGateway.Contracts/Calls/AiFeature.cs`. `AiFeatureCodes.cs` đã trả mã `personalized-content.banner`. Mục "Mở rộng thêm vào `AiFeature`" của spec không cần task mới.
2. **Bậc ngân sách thấp đã có.** `BudgetGuard.IsLowPriority` trong `Pickory.Modules.AiGateway/Budget/BudgetGuard.cs` xếp cả ba tính năng vào bậc thấp. Gateway dừng các tính năng này ở 100% ngân sách (quyết định 9 của `tasks/identity-ai-gateway/plan.md`).
3. **Chặn PII trước khi gửi AI đã có.** Gateway gọi `PiiRedactor` của `Pickory.Shared` cho mọi lời gọi. Module vẫn bỏ tên người viết khỏi đầu vào, vì spec cấm đưa tên vào prompt.
4. **Hạ tầng dùng lại.** Module dùng `RequirePermission`, `IAuditLogWriter`, `DomainEventCollector`, mẫu `ImageDeletionWorker` và kho ảnh giả của catalog. Plan này không làm lại các phần đó.
5. **Quyền đã có.** `products:read` và `products:write` có sẵn trong `Permissions.cs`. Module không thêm quyền mới.

## Điều kiện trước

1. `commerce` xong `ICommerceReader` có `GetVisibleReviewsAsync` và `GetRatingSummaryAsync`. Tóm tắt đánh giá cần hai hàm này.
2. `activity-tracking` xong `IProfileReader`. Ánh xạ nhóm nhu cầu cần `InterestProfile`.
3. Người dùng chốt Câu hỏi mở 1 (tín hiệu khi đánh giá đổi). `PCON-06` chờ quyết định này.
4. Người dùng chốt Câu hỏi mở 5 (trang chủ cho khách). `PCON-UI2` chờ quyết định này.
5. Plan đổi route cũ sang REST đã xong trước `commerce`. Route mới của module này theo quyết định 13.

## Đồ thị phụ thuộc

```
PCON-01 nhóm nhu cầu: danh mục, ánh xạ hồ sơ, khóa khía cạnh (logic thuần)
PCON-02 bộ kiểm tra chống bịa (logic thuần)
PCON-03 hash đầu vào, điều kiện tạo lại, sắp ý theo sở thích (logic thuần)
 └── PCON-04 khung module, Contracts, schema, migration
      └── PCON-05 hàng đợi job, processor, worker
           ├── PCON-06 tạo tóm tắt đánh giá (cần ICommerceReader, Câu hỏi mở 1)
           ├── PCON-07 tạo điểm nổi bật (cần ICatalogReader, ProductChangedEvent)
           └── PCON-08 tạo banner, hết hạn sau 7 ngày
                └── PCON-09 API đọc công khai, IContentReader ──── PCON-UI1 khối trang sản phẩm
                     │                                       └── PCON-UI2 banner trang chủ
                     ├── PCON-10 API quản trị nội dung AI ────── PCON-UI3 màn nội dung AI
                     └── PCON-11 banner tĩnh ─────────────────── PCON-UI4 màn banner tĩnh
                          └── PCON-12 lệnh kiểm lại và lệnh đánh giá với AI thật
                               └── PCON-E2E1 ── PCON-13 nghiệm thu
```

Ba task logic thuần không cần DB, nên làm song song được. `PCON-09` cần `PCON-06` đến `PCON-08`, vì API đọc trả cả ba loại nội dung.

## Architecture Decisions

1. **Cấu trúc theo `tasks/dotnet-migration/plan.md`.** Hai project mới: `Pickory.Modules.PersonalizedContent` và `Pickory.Modules.PersonalizedContent.Contracts`. `PersonalizedContentModule.cs` có `AddPersonalizedContentModule` và `MapPersonalizedContentEndpoints`. `Pickory.ArchitectureTests` thêm luật cho module mới.
2. **Chuyển interface sang .NET.** `contentReader` của spec thành `IContentReader` trong Contracts. Mọi hàm nhận `CancellationToken` và trả `Task`.
   - `GetReviewSummaryAsync(productId, actor)` trả `ReviewSummaryView?`.
   - `GetHighlightsAsync(productId, actor)` trả `HighlightsView?`.
   - `GetHomeBannersAsync(actor)` trả `IReadOnlyList<BannerView>`.
   - `ResolvePersonaAsync(categoryId, actor)` trả mã nhóm nhu cầu dạng `string`.
   - `PendingApprovalCountAsync()` trả `int`.
   - `Actor` lấy từ `Pickory.Modules.Identity.Contracts`.
3. **Chuyển file Node sang .NET.** `personas.ts` thành lớp `PersonaCatalog` trong thư mục `Personas/`. Schema Zod của tóm tắt đánh giá thành `record` C# và `AbstractValidator`, truyền vào `IAiGateway.ChatJsonAsync`. Bảng Prisma thành entity EF Core trong `Entities/` và cấu hình trong `Persistence/Configurations/`.
4. **Hash đầu vào do module tự tính.** `ProductChangedEvent` chỉ có `ProductId` và `Change`, không có `contentHash`. Module đọc `ProductDetail`, dựng đầu vào không có giá, rồi băm SHA-256. Hash không đổi thì module không tạo job, nên đổi giá không tốn lời gọi AI.
5. **Nội dung luôn tạo ở job nền.** API đọc không bao giờ gọi `IAiGateway`. Thiếu nội dung thì API trả `{ data: null }` và thêm 1 dòng `content_jobs`. Chỉ mục unique trên khóa nội dung chặn job trùng.
6. **Job theo mẫu `ImageDeletionWorker`.** Worker chạy mỗi 30 giây, lấy tối đa 10 job đến hạn. Test gọi `ContentJobProcessor` trực tiếp, không chờ timer. Lỗi `AiUnavailableException` hoãn job với backoff tăng dần, tối đa 5 lần.
7. **Hết ngân sách thì dừng tạo mới.** `AiBudgetExceededException` làm processor dừng vòng chạy hiện tại và giữ nguyên job. Nội dung `active` cũ vẫn được trả. Vòng sau thử lại, nên việc tạo tự chạy tiếp khi ngân sách mới bắt đầu.
8. **Kiểm tra chống bịa là hàm thuần.** Bộ kiểm tra không gọi DB và không gọi AI. Nội dung không đạt thì job tạo lại đúng 1 lần. Lần hai vẫn không đạt thì nội dung có `status = failed` và `failReason`.
9. **Khóa duy nhất của nội dung.** Khóa là `(kind, productId, categoryId, persona)`, dùng `NULLS NOT DISTINCT` của PostgreSQL 15. Spec dùng `(kind, productId, persona)`, nhưng banner có `productId` rỗng. Khi tạo lại, module ghi đè dòng cũ sau khi nội dung mới đạt kiểm tra.
10. **Khía cạnh đánh giá khớp sở thích qua bảng ánh xạ.** `InterestProfile` lưu thuộc tính dạng khóa và giá trị, ví dụ `battery_mah`. Tóm tắt đánh giá lưu khía cạnh dạng chữ, ví dụ `pin`. `PersonaCatalog` có bảng ánh xạ khóa thuộc tính sang khía cạnh.
11. **Giá trong banner do hệ thống chèn lúc đọc.** LLM chỉ viết tiêu đề và câu phụ không có số tiền. Bộ kiểm tra từ chối câu có số tiền. API đọc thêm phần "từ X triệu" theo giá thấp nhất hiện tại của bộ sưu tập.
12. **Sản phẩm bị ẩn hoặc xóa.** `ProductChangedEvent` loại `Status` hoặc `Deleted` làm module kiểm lại sản phẩm bằng `ICatalogReader.GetProductAsync`. Sản phẩm không còn `active` thì module xóa điểm nổi bật và tóm tắt đánh giá của sản phẩm đó.
13. **Route theo REST mức tài nguyên.** Spec có ba route đặt theo hành động. Plan đề xuất route thay thế dưới đây, chờ người dùng chốt (Câu hỏi mở 2):
    - `POST /api/admin/generated-contents/:id/hide` thành `PATCH /api/admin/generated-contents/{id}` với `{ "status": "hidden" }`.
    - `POST /api/admin/generated-contents/:id/approve` thành `PATCH /api/admin/generated-contents/{id}` với `{ "status": "active" }`.
    - `POST /api/admin/generated-contents/:id/regenerate` thành `POST /api/admin/content-jobs` với `{ "generatedContentId": "..." }`, trả 202.
    - Các route còn lại của spec đã là danh từ, giữ nguyên.
14. **Mã lỗi.** `PRODUCT_NOT_FOUND` (404) khi sản phẩm không có hoặc khách không thấy. `GENERATED_CONTENT_NOT_FOUND` (404). `INVALID_CONTENT_TRANSITION` (409) khi đổi trạng thái không hợp lệ, ví dụ duyệt nội dung không phải banner chờ duyệt. Thông báo viết tiếng Việt.
15. **Audit log.** Ẩn, duyệt, yêu cầu tạo lại và sửa banner tĩnh ghi đúng 1 dòng audit trong cùng transaction. Mã audit: `personalized-content.content.hide`, `personalized-content.content.approve`, `personalized-content.content.regenerate`, `personalized-content.static-banners.update`.
16. **Prompt có phiên bản.** Mỗi loại nội dung có hằng `promptVersion`, ví dụ `review-summary@1`. Cột `promptVersion` lưu giá trị này. `CallContext` gửi cùng giá trị, nên `ai_usage_logs` truy được nội dung.
17. **Banner tĩnh dùng kho ảnh của catalog.** Admin tải ảnh qua `POST /api/admin/uploads/signature` đã có của catalog. Module lưu `imagePublicId`. Module cần hàm dựng URL ảnh, hiện nằm trong `Pickory.Modules.Catalog/Images/` (Câu hỏi mở 4).

## Task List

Chi tiết từng task ở `tasks/personalized-content/todo.md`.

### Phase 1: Logic thuần và nền tảng
- [ ] PCON-01 · [ ] PCON-02 · [ ] PCON-03 · [ ] PCON-04

### Checkpoint P1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `PersonalizedContent` tham chiếu module khác ngoài Contracts
- [ ] Review với người dùng: danh sách nhóm nhu cầu và luật ánh xạ (Câu hỏi mở 3)

### Phase 2: Tạo nội dung
- [ ] PCON-05 · [ ] PCON-06 · [ ] PCON-07 · [ ] PCON-08

### Checkpoint P2
- [ ] Job với fake provider tạo đủ ba loại nội dung `active` hoặc `pending_approval`
- [ ] Nội dung bịa thành `failed` sau đúng 2 lần tạo
- [ ] Review với người dùng

### Phase 3: Đọc, quản trị và giao diện
- [ ] PCON-09 · [ ] PCON-10 · [ ] PCON-11 · [ ] PCON-UI1 · [ ] PCON-UI2 · [ ] PCON-UI3 · [ ] PCON-UI4

### Checkpoint P3
- [ ] Trang sản phẩm hiện hai khối nội dung AI có nhãn, và ẩn khối khi chưa có nội dung
- [ ] Admin duyệt banner trên giao diện, banner hiện ở trang chủ cho đúng nhóm nhu cầu
- [ ] Review với người dùng

### Phase 4: E2E và nghiệm thu
- [ ] PCON-12 · [ ] PCON-E2E1 · [ ] PCON-13

### Checkpoint cuối
- [ ] Mọi dòng Traceability đạt, trừ mục ghi "khi có API key"
- [ ] Coverage dòng của `Pickory.Modules.PersonalizedContent` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `shopping-assistant`

## Traceability: Success Criteria → Task

| Success Criteria (spec) | Task | Cách kiểm |
| --- | --- | --- |
| Sản phẩm seed có ít nhất 3 đánh giá đều có tóm tắt; dưới 3 đánh giá thì không hiện khối | PCON-03, PCON-06, PCON-09, PCON-UI1, PCON-13 | Unit điều kiện tạo; test tích hợp với đánh giá dựng sẵn; Vitest ẩn khối. Phần "dữ liệu seed" chờ Câu hỏi mở 6 |
| Mọi điểm nổi bật `active` qua bộ kiểm tra chống bịa, 0 vi phạm | PCON-02, PCON-07, PCON-12 | Unit bộ kiểm tra; lệnh `personalized-content:verify` in 0 vi phạm |
| Với AI thật, ít nhất 90% nội dung đạt ngay lần đầu; 18 trên 20 mẫu người chấm đạt | PCON-12, PCON-13 | Lệnh `personalized-content:eval` khi `AI_LIVE_TESTS=1` (chưa có API key, để mở) |
| Hai khách khác nhóm nhu cầu thấy điểm nổi bật khác nhau cho cùng sản phẩm | PCON-01, PCON-07, PCON-09, PCON-E2E1 | Test tích hợp với hai hồ sơ dựng sẵn; E2E |
| Trang sản phẩm không chờ LLM; p95 của 2 endpoint đọc dưới 150 ms | PCON-09, PCON-13 | Test tích hợp xác nhận API đọc không gọi gateway; lệnh đo p95 |
| Banner chưa duyệt không xuất hiện; duyệt xong thì xuất hiện cho đúng nhóm | PCON-08, PCON-09, PCON-10, PCON-UI3, PCON-E2E1 | Test tích hợp; E2E duyệt banner |
| Tắt API AI hoặc hết ngân sách: trang vẫn hiện nội dung đã có, không lỗi | PCON-05, PCON-09, PCON-E2E1 | Test tích hợp với fake provider ném lỗi ngân sách; E2E với AI tắt |
| Coverage ít nhất 80% | PCON-13 | Coverage gộp unit và tích hợp, cách đo như shipping |

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
| --- | --- | --- |
| Luật nhóm nhu cầu không khớp thuộc tính thật của seed | Mọi khách rơi vào nhóm `general`, tiêu chí "hai nhóm khác nhau" không đạt | `PCON-01` viết luật theo thuộc tính có trong seed; người dùng duyệt ở Checkpoint P1 |
| Bộ kiểm tra số quá chặt với cách viết đơn vị | Nhiều nội dung đúng bị `failed` | Chuẩn hóa số và đơn vị trước khi so; đo tỉ lệ đạt bằng `personalized-content:eval` |
| Chưa có tín hiệu khi đánh giá đổi | Tóm tắt đánh giá cũ không được tạo lại | Câu hỏi mở 1; phương án dự phòng là job quét mỗi giờ |
| E2E cần hồ sơ sở thích ngay, nhưng job hồ sơ chạy mỗi 5 phút | E2E chậm hoặc chập chờn | Dùng cách kích hoạt job hồ sơ mà `activity-tracking` cung cấp cho E2E |
| Chưa có API key | Không đo được tỉ lệ đạt với AI thật | Toàn bộ test dùng fake provider; tiêu chí AI thật để mở như mục sandbox của shipping |
| Chưa có trang chủ cho khách | Không có chỗ đặt banner | Câu hỏi mở 5; phối hợp với plan `tasks/recommendations/plan.md` |

## Câu hỏi mở

1. **Tín hiệu khi đánh giá đổi.** Spec `commerce` chưa có sự kiện đánh giá. Đề xuất thêm `ReviewChangedEvent(ProductId, Change)` vào `Pickory.Modules.Commerce.Contracts/Events/`, với `Change` là `Created`, `Updated`, `Hidden` hoặc `Shown`. Đổi Contracts thuộc nhóm "Ask first". Phương án khác: job quét `GetRatingSummaryAsync` mỗi giờ.
2. **Route REST thay route hành động.** Spec ghi `/hide`, `/regenerate`, `/approve`. Plan đề xuất route ở quyết định 13. Người dùng chốt thì cập nhật spec trước khi làm `PCON-10`.
3. **Nhóm nhu cầu và luật ánh xạ.** Spec lấy ví dụ "GPU rời" và danh mục con Laptop gaming. Seed hiện không có thuộc tính GPU, và danh mục seed không có danh mục con. Màn hình và Phụ kiện chưa có nhóm nhu cầu. Đề xuất: Màn hình và Phụ kiện chỉ dùng `general`, luật Laptop dựa trên `ram_gb`, `screen_inch` và tầm giá.
4. **Dựng URL ảnh banner tĩnh.** Hàm dựng URL ảnh nằm trong `Pickory.Modules.Catalog/Images/`, module khác không gọi được. Đề xuất chuyển phần dựng URL Cloudinary sang `Pickory.Shared`. Phương án khác: thêm `BuildImageUrl(publicId)` vào `Pickory.Modules.Catalog.Contracts`.
5. **Trang chủ cho khách.** Route `/` hiện là `HealthPage`. Banner và khối "Dành cho bạn" của `recommendations` đều cần trang chủ. Đề xuất: `REC-UI3` của `tasks/recommendations/plan.md` dựng trang chủ tối thiểu. `PCON-UI2` chỉ thêm banner vào trang đó.
6. **Đánh giá trong dữ liệu seed.** Tiêu chí 1 cần sản phẩm seed có ít nhất 3 đánh giá. Đánh giá thuộc bảng của `commerce`, module này không ghi được. Đề xuất: `commerce` thêm lệnh seed đánh giá cho dev.
7. **Link giải thích nhãn AI.** Spec yêu cầu link giải thích ngắn nhưng không nói đích. Đề xuất: một tài liệu `faq` của `knowledge-base`, ví dụ `/help/noi-dung-ai`.
8. Câu hỏi mở của spec giữ mặc định: chưa làm email cá nhân hóa; admin chưa sửa tay nội dung AI.
