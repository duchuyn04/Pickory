# Implementation Plan: knowledge-base

## Overview

Module `knowledge-base` là kho tri thức dạng văn bản của Pickory. Kho có bốn loại tài liệu:
- `policy`: chính sách;
- `faq`: câu hỏi thường gặp;
- `guide`: bài hướng dẫn chọn mua;
- `internal`: tài liệu nội bộ.

Module lo các việc sau:
- soạn tài liệu và nhập file;
- chia chunk và tạo embedding;
- truy xuất hybrid.

`shopping-assistant` và `support-desk` dùng kết quả truy xuất làm nguồn RAG. Tài liệu đã đăng cũng hiện thành trang công khai `/help`.

`knowledge-base` chỉ phụ thuộc `identity` và `ai-gateway`. Theo `spec/CAPABILITY-MAP.md`, module này build song song với `catalog` và `shipping`. Module bắt đầu được trước `commerce`, vì không cần dữ liệu đơn hàng hay sản phẩm.

Spec gốc: `spec/SPEC.md`, `spec/SPEC-knowledge-base.md`. Spec còn viết interface và công cụ RAG theo bản Node. Plan này chuyển sang .NET theo `tasks/dotnet-migration/plan.md`.
Danh sách task chi tiết: `tasks/knowledge-base/todo.md`.

## Phần đã phủ bởi plan cũ

Các mục dưới đây đã có trong code hoặc plan cũ. Plan này không tạo task cho chúng.

1. **Quyền.** `knowledge:read` và `knowledge:write` đã có trong `Permissions.cs` và `PermissionCatalog.cs` (ID-01 của `tasks/identity-ai-gateway/todo.md`).
2. **Quyền mặc định của role.** `DefaultRoles.cs` cấp `knowledge:read` cho `sales` và `support`. Admin có mọi quyền ngầm định. Chỉ admin có `knowledge:write`. Bảng này khớp mục "Phân quyền" của spec.
3. **Mã tính năng AI.** `AiFeature.KnowledgeBaseIndex` và `AiFeature.KnowledgeBaseQuery` đã có. `BudgetGuard` xếp hai tính năng này vào bậc ưu tiên cao (dừng ở 120%), theo quyết định 9 của `tasks/identity-ai-gateway/plan.md`.
4. **Cache embedding và chặn PII.** `IAiGateway.EmbedAsync` đã có cache theo text và đã chạy `PiiRedactor` trước khi gửi (AI-01, AI-08, DN-06). Câu hỏi lặp lại không tốn thêm lời gọi embedding.
5. **Package vector.** `Pgvector.EntityFrameworkCore` đã được duyệt trong `tasks/dotnet-migration/plan.md`. Migration của `ai-gateway` đã tạo extension `vector`.
6. **Hạ tầng dùng chung.** Các phần sau đã có:
   - `IAuditLogWriter` và `RequirePermission`;
   - `SerializableTransactionRunner` và `RequestValidation`;
   - khung back-office (UI-05, DS-02).

## Điều kiện trước

1. Người dùng duyệt danh sách package ở `KB-00` (nhóm "Ask first" của `spec/SPEC.md`). `KB-03`, `KB-04` và `KB-09` chờ quyết định này.
2. Người dùng chốt route REST thay cho route hành động của spec (Câu hỏi mở 1). `KB-05`, `KB-06`, `KB-08` và `KB-09` chờ quyết định này.
3. Plan đổi route cũ sang REST không chặn module này. Module không gọi route cũ nào.

## Đồ thị phụ thuộc

```
KB-00 chọn thư viện .NET cho nhập file, đếm token, parse Markdown (cần duyệt)
KB-01 logic thuần: anchor, trạng thái tài liệu, RRF, luật đối tượng đọc
 └── KB-02 khung module, Contracts, schema, migration
      ├── KB-03 parse Markdown thành khối hiển thị (cần KB-00)
      │    └── KB-04 chia chunk (cần KB-00)
      │         └── KB-06 đăng, gỡ, lưu trữ ──────────────── KB-UI3 lịch sử phiên bản, nút đăng
      │              ├── KB-07 truy xuất hybrid
      │              │    └── KB-08 route thử truy xuất ───── KB-UI4 màn thử truy xuất
      │              ├── KB-10 API công khai ─────────────── KB-UI1 Trung tâm hỗ trợ
      │              └── KB-11 dữ liệu mẫu `knowledge:seed`
      └── KB-05 API bản nháp (cần KB-03) ─────────────────── KB-UI2 danh sách, trình soạn
           └── KB-09 nhập DOCX và PDF (cần KB-00) ────────── KB-UI3
KB-12 bộ đánh giá và đo p95 (cần KB-07, KB-11)
KB-E2E1 luồng E2E (cần mọi task UI)
KB-13 nghiệm thu
```

`KB-06` cần `KB-04`, vì đăng tài liệu tạo chunk ngay trong lần gọi. `KB-07` cần `KB-06`, vì chỉ tài liệu đã đăng mới có chunk. `KB-10` cần `KB-03`, vì trang công khai dùng cùng bộ parse với bộ chia chunk.

## Architecture Decisions

1. **Cấu trúc theo `tasks/dotnet-migration/plan.md`.** Hai project mới: `Pickory.Modules.KnowledgeBase` và `Pickory.Modules.KnowledgeBase.Contracts`. `KnowledgeBaseModule.cs` có `AddKnowledgeBaseModule` và `MapKnowledgeBaseEndpoints`. `Program.cs` chỉ gọi hai hàm này. `Pickory.ArchitectureTests` thêm luật cho module mới.

2. **Chuyển interface từ TypeScript sang .NET.** Spec viết `knowledge.retrieve(input): Promise<KbHit[]>`. Bản .NET đặt trong Contracts:
   - `IKnowledgeRetriever.RetrieveAsync(KnowledgeQuery query, CancellationToken cancellationToken)` trả `Task<IReadOnlyList<KbHit>>`.
   - `KnowledgeQuery` là `record` gồm `Query`, `Audience`, `Types`, `CategoryId`, `K`.
   - `KbAudience` là enum `Customer`, `Staff`. `KbDocType` là enum `Policy`, `Faq`, `Guide`, `Internal`.
   - `KbHit` giữ đúng 9 trường của spec, đổi tên sang PascalCase, ví dụ `DocVersionId`, `HeadingPath`, `Url`.
   - `knowledgePermissions` của spec không cần re-export. Hai quyền đã nằm trong `Permissions.cs` của `identity`.

3. **Thư viện .NET thay LangChain.js và `mammoth`.** Bản Node dùng LangChain.js cho PDF và chia chunk, dùng `mammoth` cho DOCX. Hai thư viện này không chạy trên .NET. Đề xuất chờ duyệt ở `KB-00`:
   - `Markdig` (BSD-2): parse Markdown thành cây cú pháp, tắt HTML thô.
   - `DocumentFormat.OpenXml` (MIT): đọc DOCX, ánh xạ style `Heading1` đến `Heading3`, danh sách và bảng sang Markdown bằng code tự viết.
   - `UglyToad.PdfPig` (Apache-2.0): trích văn bản theo trang, tạo PDF mẫu trong test.
   - `Microsoft.ML.Tokenizers` cùng gói dữ liệu `cl100k_base`: đếm token đúng bộ mã của `text-embedding-3-small`.
   - Bộ chia đệ quy có chồng lấp: tự viết, không thêm package. Thứ tự tách là đoạn văn, câu, rồi từ.
   - Phương án không thêm package: tự đếm token bằng ước lượng 4 ký tự mỗi token, như provider giả. Cách này làm sai giới hạn 300 đến 500 token, nên không đề xuất.
   - Sau khi duyệt, `KB-00` cập nhật hàng "RAG tooling" của `spec/SPEC.md` và ghi `docs/adr/0005-knowledge-ingestion-dotnet.md`.

4. **Markdown hiển thị qua cây khối JSON, không qua HTML.** Backend parse Markdown bằng `Markdig` thành danh sách khối có whitelist. Frontend đổi từng khối thành thẻ React, giống quyết định 17 của `tasks/catalog/plan.md`. Frontend không dùng `dangerouslySetInnerHTML` và không thêm thư viện Markdown.
   - Khối được phép:
     - tiêu đề cấp 2 đến 4, đoạn văn;
     - chữ đậm, chữ nghiêng, mã inline;
     - danh sách có thứ tự và không thứ tự;
     - bảng, link.
   - Link chỉ nhận `https:`, `http:` và đường dẫn bắt đầu bằng `/`. Link khác hiện thành chữ thường.
   - HTML thô trong Markdown hiện thành chữ, không thành thẻ.
   - Tiêu đề cấp 1 không dùng trong nội dung, vì tiêu đề tài liệu đã là H1 của trang.
   - Lý do chọn cách này: anchor của trang công khai và anchor của chunk do cùng một hàm tạo. Link trích dẫn vì vậy luôn trỏ đúng heading.

5. **Xem trước trong trình soạn dùng response của bản nháp.** Trình soạn tự lưu bản nháp sau 1 giây ngừng gõ bằng `PATCH`. Response trả kèm `draftBlocks` đã parse. Màn soạn hiện khối này làm bản xem trước. Bản nháp không công khai, nên tự lưu không làm lộ nội dung.

6. **Anchor do một hàm thuần tạo.** `HeadingAnchor` bỏ dấu tiếng Việt và đổi `đ` thành `d`. Hàm nối các từ bằng `-`. Heading trùng trong cùng tài liệu nhận hậu tố `-2`, `-3`. Module tự viết hàm này, vì `SlugGenerator` nằm trong `Pickory.Modules.Catalog` và module khác không được tham chiếu.

7. **Quy ước chia chunk.**
   - Chia theo H2 và H3. Mục dài hơn 500 token thì cắt tiếp, mục tiêu 300 đến 500 token, chồng lấp 50 token.
   - Mỗi chunk mang đường dẫn heading, ví dụ `Chính sách bảo hành > Điều kiện bảo hành > Hàng bị rơi vỡ`. Văn bản embed gồm đường dẫn heading và nội dung chunk.
   - Bảng Markdown nằm nguyên trong một chunk, kể cả khi bảng dài hơn 500 token.
   - FAQ: mỗi H2 là một câu hỏi, phần dưới H2 là câu trả lời. Mỗi cặp hỏi đáp là một chunk.

8. **Đăng tài liệu chạy đồng bộ trong một request.** Thứ tự các bước:
   1. Parse bản nháp và chia chunk.
   2. Gọi `EmbedAsync` cho mọi chunk, ngoài transaction DB.
   3. Mở transaction.
   4. Tạo phiên bản mới và ghi chunk mới.
   5. Đổi phiên bản hiệu lực.
   6. Xóa chunk của phiên bản cũ.
   7. Ghi audit.
   8. Commit.
   - Embedding lỗi thì chưa ghi gì vào DB. Phiên bản cũ vẫn hiệu lực. API trả lỗi của gateway (`AI_UNAVAILABLE` 503 hoặc `AI_BUDGET_EXCEEDED` 429). Admin bấm đăng lại để thử lại.
   - Đổi phiên bản và xóa chunk cũ nằm trong cùng transaction. Truy xuất song song vì vậy không bao giờ thấy tài liệu trống.
   - Hai admin cùng đăng một tài liệu thì `SerializableTransactionRunner` cho một người thắng. Người kia nhận lỗi và đăng lại.
   - Chatbot thấy nội dung mới ngay sau khi request đăng trả 201, nhanh hơn mốc 1 phút của spec.

9. **Lọc đối tượng đọc ở tầng truy vấn DB.** Bảng `kb_chunks` lưu thêm `doc_id`, `doc_type`, `category_ids` và `effective_from` của tài liệu. Truy vấn của `Customer` có điều kiện `doc_type <> 'internal'` trong câu SQL. Bộ lọc không chạy sau khi đã lấy kết quả, theo luật "Always" của spec. Bốn cột này cập nhật cùng transaction đăng tài liệu.

10. **Truy xuất hybrid.** Nhánh vector và nhánh toàn văn chạy song song, mỗi nhánh lấy tối đa 50 ứng viên.
    - Gộp bằng RRF: điểm bằng tổng `1/(60 + hạng)` trên mỗi nhánh.
    - Ngưỡng liên quan áp cho mọi ứng viên sau khi gộp. Module tính cosine giữa vector câu hỏi và embedding đã lưu của từng ứng viên.
    - Ngưỡng mặc định 0,35, đọc từ biến `KB_RELEVANCE_THRESHOLD`.
    - Guide có `categoryId` khớp nhận điểm nhân 1,2.
    - `K` mặc định 5, nằm ngoài khoảng 1 đến 10 thì kéo về khoảng đó.
    - Embedding lỗi thì chỉ dùng nhánh toàn văn. Truy vấn toàn văn dùng `websearch_to_tsquery` với cấu hình `simple` và `unaccent`, nên câu không chứa từ khớp trả mảng rỗng.

11. **Ngày hiệu lực gắn với tài liệu.** Spec đặt `effectiveFrom` trên `KbDocument`. Tài liệu có ngày hiệu lực trong tương lai không được truy xuất. Trang công khai cũng ẩn tài liệu đó cho tới ngày hiệu lực, để khách và chatbot đọc cùng nội dung. Ngày tính theo `Asia/Ho_Chi_Minh`, lưu UTC.

12. **Trạng thái tài liệu.**

    | Từ | Sang | Cách gọi | Chunk |
    | --- | --- | --- | --- |
    | `draft`, `published` | `published` | Tạo phiên bản mới | Thay bằng chunk của phiên bản mới |
    | `published` | `draft` (gỡ) | Đổi `status` | Xóa ngay |
    | `draft`, `published` | `archived` | Đổi `status` | Xóa ngay |
    | `archived` | `draft` | Đổi `status` | Không có chunk |

    Chuyển trạng thái khác trả 409 `KB_INVALID_STATUS_TRANSITION`. Mỗi lần đăng, gỡ, lưu trữ ghi đúng 1 dòng audit trong cùng transaction: `knowledge.document.publish`, `knowledge.document.unpublish`, `knowledge.document.archive`.

13. **Không xóa phiên bản.** Module không có route xóa tài liệu hay phiên bản. Lưu trữ giữ mọi dòng `kb_doc_versions`. `shopping-assistant` lưu `docVersionId` trong trích dẫn, nên nhân viên luôn mở lại được nội dung bot đã dựa vào. Chunk cũ bị xóa khi đổi phiên bản, nên `chunkId` trong trích dẫn cũ có thể không còn. Trích dẫn vẫn có `docVersionId` và `url`.

14. **Nhập file tạo bản nháp mới.** Route nhập file nhận DOCX hoặc PDF, tạo một tài liệu `draft` và trả tài liệu đó.
    - Tài liệu lưu `importedFileName` và `importedAt`. Module không lưu file gốc.
    - File lớn hơn 10 MB trả 413 `PAYLOAD_TOO_LARGE`. Loại file khác trả 415 `KB_FILE_UNSUPPORTED`.
    - PDF không có lớp chữ trả 422 `KB_PDF_NO_TEXT`. Module coi một trang không có lớp chữ khi trang đó có ít hơn 20 ký tự chữ.
    - API hiện giới hạn mọi body ở 1 MB trong `ExceptionHandlingMiddleware` và Kestrel. `KB-09` thêm ngoại lệ 10 MB chỉ cho route nhập file. Mọi route khác giữ 1 MB.

15. **Danh mục sản phẩm lưu dạng id, không kiểm với `catalog`.** `knowledge-base` không phụ thuộc `catalog` theo bản đồ module. `categoryIds` của guide lưu dạng chuỗi. Màn soạn chọn danh mục từ route công khai `GET /api/categories`. Id của danh mục đã xóa chỉ làm mất điểm cộng, không gây lỗi. Xem Câu hỏi mở 4.

16. **Route theo REST mức tài nguyên.** Spec có route đặt theo hành động. Plan đề xuất bảng dưới đây và chờ người dùng chốt (Câu hỏi mở 1).

    | Spec | Đề xuất REST | Quyền |
    | --- | --- | --- |
    | `GET /api/help` | `GET /api/help?type=` | công khai |
    | `GET /api/help/:slug` | `GET /api/help/{slug}` | công khai |
    | `GET /api/admin/knowledge/docs[/:id]` | `GET /api/admin/knowledge/docs`, `GET /api/admin/knowledge/docs/{id}` | `knowledge:read` |
    | (lịch sử phiên bản) | `GET /api/admin/knowledge/docs/{id}/versions`, `GET /api/admin/knowledge/docs/{id}/versions/{versionId}` | `knowledge:read` |
    | `POST · PATCH /api/admin/knowledge/docs[/:id]` | `POST /api/admin/knowledge/docs`, `PATCH /api/admin/knowledge/docs/{id}` | `knowledge:write` |
    | `POST .../docs/:id/publish` | `POST /api/admin/knowledge/docs/{id}/versions` (201) | `knowledge:write` |
    | `POST .../docs/:id/unpublish` · `/archive` | `PATCH /api/admin/knowledge/docs/{id}` với `status` là `draft` hoặc `archived` | `knowledge:write` |
    | `POST /api/admin/knowledge/import` | `POST /api/admin/knowledge/imports` (201, multipart) | `knowledge:write` |
    | `POST /api/admin/knowledge/retrieve-test` | `GET /api/admin/knowledge/retrievals?query=&audience=&types=` | `knowledge:read` |

    `PATCH` với `status` là `published` trả 400. Đăng tài liệu chỉ đi qua route tạo phiên bản.

17. **Mã lỗi.** Thông báo viết tiếng Việt. Lỗi validate dùng `VALIDATION_ERROR` (400). Mã riêng của module:
    - `KB_DOC_NOT_FOUND` (404);
    - `KB_SLUG_TAKEN` (409);
    - `KB_INVALID_STATUS_TRANSITION` (409);
    - `KB_FILE_UNSUPPORTED` (415);
    - `KB_PDF_NO_TEXT` (422);
    - `KB_EMPTY_DOCUMENT` (422): bản nháp không có chữ khi đăng.

18. **Giới hạn dữ liệu.** Tiêu đề tối đa 200 ký tự. Slug theo mẫu `^[a-z0-9]+(-[a-z0-9]+)*$`, tối đa 120 ký tự. Bản nháp tối đa 200.000 ký tự. Tối đa 10 thẻ và 10 danh mục. Câu hỏi truy xuất tối đa 500 ký tự.

19. **Test truy xuất không dựa vào vector giả mặc định.** `FakeEmbeddingProvider` tạo vector chỉ theo độ dài text. Mọi vector giả vì vậy gần cùng hướng, nên cosine không phân biệt được nội dung. Test tích hợp đăng ký handler giả qua `EnqueueHandler`, sinh vector theo từ khóa. Câu cùng từ khóa cho cosine cao, câu khác chủ đề cho cosine thấp.

20. **Lệnh dòng lệnh.** Lệnh pnpm của spec chuyển sang lệnh của `Pickory.Api`:
    - `knowledge:seed`: ghi bộ tài liệu mẫu rồi đăng. Chạy lại không tạo trùng. Từ chối khi `ASPNETCORE_ENVIRONMENT` là `Production`, giống `catalog:seed`.
    - `knowledge:eval` thay `pnpm kb:eval`: chạy bộ đánh giá với embedding thật. Lệnh chỉ chạy khi tên DB chứa `eval`.
    - `knowledge:benchmark`: đo p95 của `RetrieveAsync`. Lệnh chỉ chạy khi tên DB chứa `bench`, giống `catalog:benchmark`.

## Task List

Chi tiết từng task ở `tasks/knowledge-base/todo.md`.

### Phase 0: Chọn thư viện
- [ ] KB-00

### Checkpoint K0
- [ ] Người dùng duyệt danh sách package và route REST
- [ ] Review với người dùng

### Phase 1: Nền tảng
- [ ] KB-01 · [ ] KB-02 · [ ] KB-03 · [ ] KB-04

### Checkpoint K1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `KnowledgeBase` tham chiếu module khác ngoài Contracts
- [ ] Chia chunk đúng trên bộ Markdown mẫu: heading, bảng, FAQ, giới hạn token
- [ ] Review với người dùng

### Phase 2: Soạn, đăng và truy xuất
- [ ] KB-05 · [ ] KB-UI2 · [ ] KB-06 · [ ] KB-07 · [ ] KB-08 · [ ] KB-UI4 · [ ] KB-09 · [ ] KB-UI3

### Checkpoint K2
- [ ] Admin soạn, nhập file, đăng, gỡ tài liệu trên giao diện
- [ ] Truy xuất `Customer` không trả tài liệu `internal`
- [ ] Đăng phiên bản mới không làm truy xuất song song trả rỗng
- [ ] Review với người dùng

### Phase 3: Trang công khai và dữ liệu mẫu
- [ ] KB-10 · [ ] KB-UI1 · [ ] KB-11

### Checkpoint K3
- [ ] Khách đọc `/help` và mở link có anchor cuộn tới đúng heading
- [ ] `knowledge:seed` tạo đủ bộ tài liệu mẫu trên DB sạch
- [ ] Review với người dùng

### Phase 4: Nghiệm thu
- [ ] KB-12 · [ ] KB-E2E1 · [ ] KB-13

### Checkpoint cuối
- [ ] Mọi dòng Traceability đạt, trừ mục ghi "chờ API key"
- [ ] Coverage dòng của `Pickory.Modules.KnowledgeBase` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng

## Traceability: Success Criteria → Task

| Success Criteria (spec) | Task | Cách kiểm |
| --- | --- | --- |
| Admin soạn, đăng, gỡ tài liệu; chatbot truy xuất được nội dung mới trong vòng 1 phút sau khi đăng | KB-05, KB-06, KB-07, KB-UI2, KB-UI3, KB-E2E1 | Test tích hợp gọi `RetrieveAsync` ngay sau khi route đăng trả 201; E2E soạn, đăng, gỡ. Kiểm qua chatbot thật thuộc `shopping-assistant` |
| Nhập DOCX và PDF mẫu ra bản nháp Markdown giữ đúng heading; PDF ảnh scan báo lỗi rõ ràng | KB-09, KB-UI3, KB-E2E1 | Unit test với file mẫu tạo trong test; test tích hợp lỗi 422 `KB_PDF_NO_TEXT`; E2E nhập DOCX |
| Bộ đánh giá: Recall@5 ít nhất 0,9 trên 40 câu có đáp án; ít nhất 8/10 câu không có đáp án trả mảng rỗng | KB-12 | `knowledge:eval` với embedding thật. Chờ API key, xem `KB-13` |
| Không có trường hợp nào tài liệu `internal` lọt vào kết quả `audience: 'customer'` | KB-07, KB-08 | Test tích hợp với câu hỏi khớp đúng nội dung `internal`; test đọc câu SQL có điều kiện `doc_type` |
| Link trích dẫn mở đúng trang và cuộn tới đúng heading | KB-06, KB-10, KB-UI1, KB-E2E1 | Unit test anchor của chunk bằng anchor của khối hiển thị; E2E mở `Url` của `KbHit` |
| p95 của `retrieve` dưới 300 ms trên máy dev (đã có cache embedding câu hỏi) | KB-12 | `knowledge:benchmark` in p95 |
| Coverage ít nhất 80% | KB-13 | Coverage gộp unit và tích hợp, cách đo như shipping |

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
| --- | --- | --- |
| Người dùng không duyệt package | Không đếm token chính xác, phải tự đọc DOCX | `KB-00` ghi phương án tự viết cho từng package. Phase 1 chờ quyết định |
| Bộ chuyển DOCX mất heading khi file không dùng style chuẩn | Bản nháp thiếu heading, chia chunk kém | Ánh xạ thêm style có tên chứa `Heading`. Màn nhập file nhắc admin kiểm heading |
| Vector giả không phân biệt nội dung | Test ngưỡng liên quan cho kết quả sai | Quyết định 19: handler giả sinh vector theo từ khóa |
| Ngưỡng 0,35 không hợp model thật | Bot trả lời "không có thông tin" quá nhiều hoặc quá ít | Chỉnh ngưỡng bằng `knowledge:eval` khi có API key |
| Chính sách mẫu chứa số hotline của shop | `PiiRedactor` đổi số thành `[đã ẩn]` trước khi embed | Văn bản lưu trong DB vẫn giữ số gốc. Bot đọc số gốc từ `KbHit.Text` |
| Embedding lỗi giữa lúc đăng | Admin tưởng đã đăng | Quyết định 8: không ghi gì khi lỗi. Màn soạn hiện lỗi và nút đăng lại |
| Body 10 MB làm hở giới hạn 1 MB của route khác | Tấn công gửi body lớn | Ngoại lệ chỉ gắn với một route. Test gửi 2 MB vào route khác vẫn nhận 413 |

## Câu hỏi mở

1. **Route REST thay route hành động.** Spec có năm route đặt theo hành động, ví dụ `/publish` và `/retrieve-test`. Quyết định 16 đề xuất bảng route REST. Đây là mâu thuẫn giữa spec và luật REST ngày 2026-10-10. Người dùng chốt bảng này, rồi builder cập nhật spec trong `KB-05`.
2. **Duyệt package (Ask first).** `Markdig`, `DocumentFormat.OpenXml`, `UglyToad.PdfPig`, `Microsoft.ML.Tokenizers` và gói dữ liệu `cl100k_base`. Xem quyết định 3.
3. **Ngày hiệu lực gắn với tài liệu hay phiên bản.** Spec đặt `effectiveFrom` trên tài liệu. Đăng lại với ngày tương lai vì vậy ẩn cả phiên bản đang hiệu lực. Đề xuất cho MVP: giữ theo spec. Nếu cần phiên bản cũ chạy tới ngày hiệu lực của phiên bản mới, cần thêm job đổi phiên bản theo lịch.
4. **Danh mục của guide.** Module không phụ thuộc `catalog`, nên không kiểm id danh mục. Đề xuất: lưu id không kiểm (quyết định 15). Phương án khác là thêm `catalog` vào phụ thuộc của module trong `spec/CAPABILITY-MAP.md`.
5. **Màn nhập file "xem bản chuyển đổi bên cạnh bản gốc".** Trình duyệt hiện được PDF gốc từ file trên máy. Trình duyệt không hiện được DOCX. Đề xuất: PDF hiện bản gốc bên cạnh; DOCX chỉ hiện tên file và bản chuyển đổi.
6. **Văn bản chính sách thật** (Open Question 1 của spec). Có văn bản thật thì `KB-11` dùng làm dữ liệu mẫu. Chưa có thì `KB-11` soạn theo các spec đã duyệt. Ngưỡng miễn phí ship 500.000 đ và hạn mức COD 20.000.000 đ vẫn chờ chốt ở Open Question 2 của `spec/SPEC-commerce.md`.
7. **API key OpenAI cho bộ đánh giá.** `knowledge:eval` và việc chỉnh ngưỡng cần embedding thật. Không có key thì hai tiêu chí của bộ đánh giá để mở.
