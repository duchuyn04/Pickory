# Implementation Plan: knowledge-base

## Thay đổi phạm vi ngày 2026-10-18

Người dùng chuyển chatbot sang dự án riêng `chatbot-rag` (`C:\Users\juven\Desktop\chatbot-rag`). Pickory gắn widget của chatbot đó qua module `chatbot-widget` (tiền tố `CBW`).

Module `knowledge-base` giữ phần soạn tài liệu và trang Trung tâm hỗ trợ `/help`. Module bỏ toàn bộ phần AI:
- chia chunk và tạo embedding;
- truy xuất hybrid, route và màn thử truy xuất;
- bộ đánh giá truy xuất và đo p95;
- thư viện đếm token và mã `AiFeature.KnowledgeBase*`.

Module thêm một nguồn tài liệu công khai cho `chatbot-rag` (`KB-14`). Nhờ vậy, admin chỉ soạn tài liệu ở một nơi. `chatbot-rag` nạp lại tài liệu khi tài liệu đổi.

Các quyết định liên quan:
- `AiGateway` bị gỡ khỏi Pickory ở `tasks/ai-gateway-removal/` (tiền tố `AIR`). Module này không gọi `IAiGateway` và không dùng pgvector.
- `shopping-assistant` đã bỏ.
- `support-desk` chạy độc lập và không đọc kho tri thức.

Task bỏ vẫn giữ trong `todo.md`, có dòng "Trạng thái: đã bỏ ngày 2026-10-18".

## Overview

Module `knowledge-base` là kho tri thức dạng văn bản của Pickory. Kho có bốn loại tài liệu:
- `policy`: chính sách;
- `faq`: câu hỏi thường gặp;
- `guide`: bài hướng dẫn chọn mua;
- `internal`: tài liệu nội bộ.

Module lo các việc sau:
- soạn tài liệu Markdown và nhập file DOCX, PDF thành bản nháp;
- lưu phiên bản, đăng, gỡ và lưu trữ tài liệu;
- hiện tài liệu đã đăng ở trang công khai `/help`;
- cấp nguồn tài liệu công khai cho `chatbot-rag`.

`knowledge-base` chỉ phụ thuộc `identity`. Module bắt đầu được trước `commerce`, vì không cần dữ liệu đơn hàng hay sản phẩm.

Spec gốc: `spec/SPEC.md`, `spec/SPEC-knowledge-base.md`. Spec còn viết phần chia đoạn, index và truy xuất theo bản Node. Plan này bỏ các phần đó và chuyển phần còn lại sang .NET theo `tasks/dotnet-migration/plan.md`. Xem Câu hỏi mở 8.
Danh sách task chi tiết: `tasks/knowledge-base/todo.md`.

## Phần đã phủ bởi plan cũ

Các mục dưới đây đã có trong code hoặc plan cũ. Plan này không tạo task cho chúng.

1. **Quyền.** `knowledge:read` và `knowledge:write` đã có trong `Permissions.cs` và `PermissionCatalog.cs` (ID-01 của `tasks/identity-ai-gateway/todo.md`).
2. **Quyền mặc định của role.** `DefaultRoles.cs` cấp `knowledge:read` cho `sales` và `support`. Admin có mọi quyền ngầm định. Chỉ admin có `knowledge:write`. Bảng này khớp mục "Phân quyền" của spec.
3. **Mã tính năng AI.** Không dùng từ ngày 2026-10-18. `AiFeature.KnowledgeBaseIndex` và `AiFeature.KnowledgeBaseQuery` bị gỡ cùng `AiGateway` ở `tasks/ai-gateway-removal/`.
4. **Cache embedding và chặn PII.** Không dùng từ ngày 2026-10-18, vì module không tạo embedding.
5. **Package vector.** Không dùng từ ngày 2026-10-18. Module không có cột vector.
6. **Hạ tầng dùng chung.** Các phần sau đã có:
   - `IAuditLogWriter` và `RequirePermission`;
   - `SerializableTransactionRunner` và `RequestValidation`;
   - khung back-office (UI-05, DS-02).

## Điều kiện trước

1. Người dùng duyệt danh sách package ở `KB-00` (nhóm "Ask first" của `spec/SPEC.md`). `KB-03` và `KB-09` chờ quyết định này.
2. Người dùng chốt route REST thay cho route hành động của spec (Câu hỏi mở 1). `KB-05`, `KB-06`, `KB-09` và `KB-14` chờ quyết định này.
3. Plan đổi route cũ sang REST không chặn module này. Module không gọi route cũ nào.

## Đồ thị phụ thuộc

```
KB-00 chọn thư viện .NET cho nhập file và parse Markdown (cần duyệt)
KB-01 logic thuần: anchor, trạng thái tài liệu, luật tài liệu công khai
 └── KB-02 khung module, Contracts, schema, migration
      └── KB-03 parse Markdown thành khối hiển thị (cần KB-00)
           └── KB-05 API bản nháp ─────────────────────────── KB-UI2 danh sách, trình soạn
                ├── KB-06 đăng, gỡ, lưu trữ ───────────────── KB-UI3 lịch sử phiên bản, nút đăng
                │    ├── KB-10 API công khai ──────────────── KB-UI1 Trung tâm hỗ trợ
                │    ├── KB-14 nguồn tài liệu cho `chatbot-rag`
                │    └── KB-11 dữ liệu mẫu `knowledge:seed`
                └── KB-09 nhập DOCX và PDF (cần KB-00) ────── KB-UI3
KB-E2E1 luồng E2E (cần mọi task UI và KB-14)
KB-13 nghiệm thu
```

`KB-10` và `KB-14` cần `KB-06`, vì chỉ tài liệu đã đăng mới có phiên bản hiệu lực. `KB-10` cần `KB-03`, vì trang công khai hiện khối do bộ parse tạo.

Task đã bỏ ngày 2026-10-18: `KB-04`, `KB-07`, `KB-08`, `KB-UI4`, `KB-12`.

## Architecture Decisions

1. **Cấu trúc theo `tasks/dotnet-migration/plan.md`.** Hai project mới: `Pickory.Modules.KnowledgeBase` và `Pickory.Modules.KnowledgeBase.Contracts`. `KnowledgeBaseModule.cs` có `AddKnowledgeBaseModule` và `MapKnowledgeBaseEndpoints`. `Program.cs` chỉ gọi hai hàm này. `Pickory.ArchitectureTests` thêm luật cho module mới.

2. **Contracts tối thiểu.** Không module nào của Pickory đọc kho tri thức từ ngày 2026-10-18. Contracts chỉ giữ enum `KbDocType` (`Policy`, `Faq`, `Guide`, `Internal`), theo quy ước mỗi module có một project Contracts. Plan bỏ `IKnowledgeRetriever`, `KnowledgeQuery`, `KbAudience` và `KbHit` của bản trước.

3. **Thư viện .NET thay `mammoth` và LangChain.js.** Bản Node dùng `mammoth` cho DOCX và LangChain.js cho PDF. Hai thư viện này không chạy trên .NET. Đề xuất chờ duyệt ở `KB-00`:
   - `Markdig` (BSD-2): parse Markdown thành cây cú pháp, tắt HTML thô.
   - `DocumentFormat.OpenXml` (MIT): đọc DOCX, ánh xạ style `Heading1` đến `Heading3`, danh sách và bảng sang Markdown bằng code tự viết.
   - `UglyToad.PdfPig` (Apache-2.0): trích văn bản theo trang, tạo PDF mẫu trong test.
   - Plan bỏ `Microsoft.ML.Tokenizers` và bộ chia chunk từ ngày 2026-10-18, vì module không chia chunk.
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
   - Mục lục và khối tiêu đề dùng cùng hàm tạo anchor. Link có anchor vì vậy luôn trỏ đúng heading.

5. **Xem trước trong trình soạn dùng response của bản nháp.** Trình soạn tự lưu bản nháp sau 1 giây ngừng gõ bằng `PATCH`. Response trả kèm `draftBlocks` đã parse. Màn soạn hiện khối này làm bản xem trước. Bản nháp không công khai, nên tự lưu không làm lộ nội dung.

6. **Anchor do một hàm thuần tạo.** `HeadingAnchor` bỏ dấu tiếng Việt và đổi `đ` thành `d`. Hàm nối các từ bằng `-`. Heading trùng trong cùng tài liệu nhận hậu tố `-2`, `-3`. Module tự viết hàm này, vì `SlugGenerator` nằm trong `Pickory.Modules.Catalog` và module khác không được tham chiếu.

7. **Quy ước chia chunk.** Đã bỏ ngày 2026-10-18. `chatbot-rag` tự chia chunk khi nạp tài liệu.

8. **Đăng tài liệu chạy trong một transaction.** Thứ tự các bước:
   1. Parse bản nháp. Bản nháp không có chữ thì trả 422 `KB_EMPTY_DOCUMENT`.
   2. Mở transaction.
   3. Tạo phiên bản mới.
   4. Đổi phiên bản hiệu lực.
   5. Ghi audit.
   6. Commit.
   - Đổi phiên bản nằm trong transaction. Request đọc song song vì vậy luôn thấy một phiên bản đầy đủ.
   - Hai admin cùng đăng một tài liệu thì `SerializableTransactionRunner` cho một người thắng. Người kia nhận lỗi và đăng lại.
   - `chatbot-rag` thấy phiên bản mới ở `KB-14` ngay sau khi request đăng trả 201.

9. **Lọc tài liệu công khai ở tầng truy vấn DB.** Route công khai (`KB-10`, `KB-14`) lọc trong câu SQL:
   - `doc_type <> 'internal'`;
   - `status = 'published'`;
   - `effective_from` rỗng hoặc không lớn hơn giờ hiện tại.
   Bộ lọc không chạy sau khi đã lấy kết quả. Một hàm truy vấn dùng chung cho hai route, để hai route không lệch nhau.

10. **Truy xuất hybrid.** Đã bỏ ngày 2026-10-18. `chatbot-rag` tự truy xuất trên index FAISS của nó.

11. **Ngày hiệu lực gắn với tài liệu.** Spec đặt `effectiveFrom` trên `KbDocument`. Trang công khai và nguồn `KB-14` ẩn tài liệu có ngày hiệu lực trong tương lai. Khách và chatbot vì vậy đọc cùng nội dung. Ngày tính theo `Asia/Ho_Chi_Minh`, lưu UTC.

12. **Trạng thái tài liệu.**

    | Từ | Sang | Cách gọi |
    | --- | --- | --- |
    | `draft`, `published` | `published` | Tạo phiên bản mới |
    | `published` | `draft` (gỡ) | Đổi `status` |
    | `draft`, `published` | `archived` | Đổi `status` |
    | `archived` | `draft` | Đổi `status` |

    Chuyển trạng thái khác trả 409 `KB_INVALID_STATUS_TRANSITION`. Mỗi lần đăng, gỡ, lưu trữ ghi đúng 1 dòng audit trong cùng transaction: `knowledge.document.publish`, `knowledge.document.unpublish`, `knowledge.document.archive`.

13. **Không xóa phiên bản.** Module không có route xóa tài liệu hay phiên bản. Lưu trữ giữ mọi dòng `kb_doc_versions`. Admin luôn mở lại được nội dung của phiên bản cũ.

14. **Nhập file tạo bản nháp mới.** Route nhập file nhận DOCX hoặc PDF, tạo một tài liệu `draft` và trả tài liệu đó.
    - Tài liệu lưu `importedFileName` và `importedAt`. Module không lưu file gốc.
    - File lớn hơn 10 MB trả 413 `PAYLOAD_TOO_LARGE`. Loại file khác trả 415 `KB_FILE_UNSUPPORTED`.
    - PDF không có lớp chữ trả 422 `KB_PDF_NO_TEXT`. Module coi một trang không có lớp chữ khi trang đó có ít hơn 20 ký tự chữ.
    - API hiện giới hạn mọi body ở 1 MB trong `ExceptionHandlingMiddleware` và Kestrel. `KB-09` thêm ngoại lệ 10 MB chỉ cho route nhập file. Mọi route khác giữ 1 MB.

15. **Danh mục sản phẩm lưu dạng id, không kiểm với `catalog`.** `knowledge-base` không phụ thuộc `catalog` theo bản đồ module. `categoryIds` của guide lưu dạng chuỗi. Màn soạn chọn danh mục từ route công khai `GET /api/categories`. Id của danh mục đã xóa không gây lỗi. Xem Câu hỏi mở 4.

16. **Route theo REST mức tài nguyên.** Spec có route đặt theo hành động. Plan đề xuất bảng dưới đây và chờ người dùng chốt (Câu hỏi mở 1).

    | Spec | Đề xuất REST | Quyền |
    | --- | --- | --- |
    | `GET /api/help` | `GET /api/help?type=` | công khai |
    | `GET /api/help/:slug` | `GET /api/help/{slug}` | công khai |
    | (nguồn cho `chatbot-rag`, mới) | `GET /api/help-documents` | công khai |
    | `GET /api/admin/knowledge/docs[/:id]` | `GET /api/admin/knowledge/docs`, `GET /api/admin/knowledge/docs/{id}` | `knowledge:read` |
    | (lịch sử phiên bản) | `GET /api/admin/knowledge/docs/{id}/versions`, `GET /api/admin/knowledge/docs/{id}/versions/{versionId}` | `knowledge:read` |
    | `POST · PATCH /api/admin/knowledge/docs[/:id]` | `POST /api/admin/knowledge/docs`, `PATCH /api/admin/knowledge/docs/{id}` | `knowledge:write` |
    | `POST .../docs/:id/publish` | `POST /api/admin/knowledge/docs/{id}/versions` (201) | `knowledge:write` |
    | `POST .../docs/:id/unpublish` · `/archive` | `PATCH /api/admin/knowledge/docs/{id}` với `status` là `draft` hoặc `archived` | `knowledge:write` |
    | `POST /api/admin/knowledge/import` | `POST /api/admin/knowledge/imports` (201, multipart) | `knowledge:write` |
    | `POST /api/admin/knowledge/retrieve-test` | Đã bỏ ngày 2026-10-18 | |

    `PATCH` với `status` là `published` trả 400. Đăng tài liệu chỉ đi qua route tạo phiên bản. `GET /api/help-documents` là tài nguyên riêng. Nếu đặt dưới `/api/help/`, route sẽ trùng với slug `documents`.

17. **Mã lỗi.** Thông báo viết tiếng Việt. Lỗi validate dùng `VALIDATION_ERROR` (400). Mã riêng của module:
    - `KB_DOC_NOT_FOUND` (404);
    - `KB_SLUG_TAKEN` (409);
    - `KB_INVALID_STATUS_TRANSITION` (409);
    - `KB_FILE_UNSUPPORTED` (415);
    - `KB_PDF_NO_TEXT` (422);
    - `KB_EMPTY_DOCUMENT` (422): bản nháp không có chữ khi đăng.

18. **Giới hạn dữ liệu.** Tiêu đề tối đa 200 ký tự. Slug theo mẫu `^[a-z0-9]+(-[a-z0-9]+)*$`, tối đa 120 ký tự. Bản nháp tối đa 200.000 ký tự. Tối đa 10 thẻ và 10 danh mục.

19. **Test truy xuất với vector giả.** Đã bỏ ngày 2026-10-18, vì module không có truy xuất.

20. **Lệnh dòng lệnh.** Lệnh pnpm của spec chuyển sang lệnh của `Pickory.Api`:
    - `knowledge:seed`: ghi bộ tài liệu mẫu rồi đăng. Chạy lại không tạo trùng. Từ chối khi `ASPNETCORE_ENVIRONMENT` là `Production`, giống `catalog:seed`.
    - `knowledge:eval` và `knowledge:benchmark` đã bỏ ngày 2026-10-18. `chatbot-rag` có bộ đánh giá riêng.

21. **Nguồn tài liệu cho `chatbot-rag`.** `GET /api/help-documents` trả mọi tài liệu công khai đã đăng trong một response.
    - Mỗi tài liệu có `id`, `slug`, `title`, `type`, `content`, `url`, `updatedAt`.
    - `content` là Markdown của phiên bản hiệu lực. `chatbot-rag` đọc Markdown như văn bản thuần, không render HTML.
    - `url` là `/help/{slug}`. `updatedAt` là thời điểm đăng phiên bản hiệu lực.
    - Route dùng bộ lọc của quyết định 9. Tài liệu `internal` không bao giờ có trong response.
    - Route không cần phiên. Nội dung này đã công khai ở `/help`.
    - Route trả tối đa 500 tài liệu. Kho vượt mức này thì trả 500 tài liệu mới nhất và log cảnh báo. Xem Câu hỏi mở 9.
    - `chatbot-rag` so `id` và `updatedAt` để biết tài liệu nào mới, đổi hay đã gỡ.

## Task List

Chi tiết từng task ở `tasks/knowledge-base/todo.md`.

### Phase 0: Chọn thư viện
- [ ] KB-00

### Checkpoint K0
- [ ] Người dùng duyệt danh sách package và route REST
- [ ] Review với người dùng

### Phase 1: Nền tảng
- [ ] KB-01 · [ ] KB-02 · [ ] KB-03 · KB-04 đã bỏ

### Checkpoint K1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `KnowledgeBase` tham chiếu module khác ngoài Contracts
- [ ] Parse đúng bộ Markdown mẫu: heading, bảng, link, HTML thô
- [ ] Review với người dùng

### Phase 2: Soạn, đăng và nhập file
- [ ] KB-05 · [ ] KB-UI2 · [ ] KB-06 · [ ] KB-09 · [ ] KB-UI3 · KB-07, KB-08, KB-UI4 đã bỏ

### Checkpoint K2
- [ ] Admin soạn, nhập file, đăng, gỡ tài liệu trên giao diện
- [ ] Đăng phiên bản mới không làm request đọc song song thấy tài liệu trống
- [ ] Review với người dùng

### Phase 3: Trang công khai, nguồn cho chatbot và dữ liệu mẫu
- [ ] KB-10 · [ ] KB-UI1 · [ ] KB-14 · [ ] KB-11

### Checkpoint K3
- [ ] Khách đọc `/help` và mở link có anchor cuộn tới đúng heading
- [ ] `GET /api/help-documents` không trả tài liệu `internal`, `draft`, `archived` hay chưa tới ngày hiệu lực
- [ ] `knowledge:seed` tạo đủ bộ tài liệu mẫu trên DB sạch
- [ ] Review với người dùng

### Phase 4: Nghiệm thu
- [ ] KB-E2E1 · [ ] KB-13 · KB-12 đã bỏ

### Checkpoint cuối
- [ ] Mọi dòng Traceability đạt, trừ dòng ghi "đã bỏ"
- [ ] Coverage dòng của `Pickory.Modules.KnowledgeBase` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng

## Traceability: Success Criteria → Task

| Success Criteria (spec) | Task | Cách kiểm |
| --- | --- | --- |
| Admin soạn, đăng, gỡ tài liệu; chatbot truy xuất được nội dung mới trong vòng 1 phút sau khi đăng | KB-05, KB-06, KB-14, KB-UI2, KB-UI3, KB-E2E1 | Phía Pickory: test tích hợp đọc `GET /api/help-documents` ngay sau khi route đăng trả 201. E2E soạn, đăng, gỡ. Mốc 1 phút phía chatbot thuộc lịch nạp của `chatbot-rag` |
| Nhập DOCX và PDF mẫu ra bản nháp Markdown giữ đúng heading; PDF ảnh scan báo lỗi rõ ràng | KB-09, KB-UI3, KB-E2E1 | Unit test với file mẫu tạo trong test; test tích hợp lỗi 422 `KB_PDF_NO_TEXT`; E2E nhập DOCX |
| Bộ đánh giá: Recall@5 ít nhất 0,9 trên 40 câu có đáp án; ít nhất 8/10 câu không có đáp án trả mảng rỗng | Đã bỏ ngày 2026-10-18 | Chuyển sang bộ đánh giá của `chatbot-rag` |
| Không có trường hợp nào tài liệu `internal` lọt vào kết quả `audience: 'customer'` | KB-01, KB-10, KB-14 | Test tích hợp: tài liệu `internal` đã đăng không có trong `/api/help`, `/api/help/{slug}` và `/api/help-documents`; test đọc câu SQL có điều kiện `doc_type` |
| Link trích dẫn mở đúng trang và cuộn tới đúng heading | KB-03, KB-10, KB-UI1, KB-E2E1 | Unit test anchor của mục lục bằng anchor của khối tiêu đề; E2E mở `/help/{slug}#{anchor}` |
| p95 của `retrieve` dưới 300 ms trên máy dev (đã có cache embedding câu hỏi) | Đã bỏ ngày 2026-10-18 | Module không còn `retrieve` |
| Coverage ít nhất 80% | KB-13 | Coverage gộp unit và tích hợp, cách đo như shipping |

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
| --- | --- | --- |
| Người dùng không duyệt package | Phải tự đọc DOCX và tự parse Markdown | `KB-00` ghi phương án tự viết cho từng package. Phase 1 chờ quyết định |
| Bộ chuyển DOCX mất heading khi file không dùng style chuẩn | Bản nháp thiếu heading | Ánh xạ thêm style có tên chứa `Heading`. Màn nhập file nhắc admin kiểm heading |
| Tài liệu `internal` lọt ra nguồn của `chatbot-rag` | Khách đọc được tài liệu nội bộ qua chatbot | Quyết định 9: một hàm truy vấn dùng chung, lọc trong SQL. Test với tài liệu `internal` đã đăng |
| `chatbot-rag` chưa có loader đọc API hoặc Markdown | Admin phải chép tài liệu sang `chatbot-rag` bằng tay | Ghi ở Câu hỏi mở 7. Việc này thuộc dự án `chatbot-rag` |
| Body 10 MB làm hở giới hạn 1 MB của route khác | Tấn công gửi body lớn | Ngoại lệ chỉ gắn với một route. Test gửi 2 MB vào route khác vẫn nhận 413 |

## Câu hỏi mở

1. **Route REST thay route hành động.** Spec có bốn route đặt theo hành động, ví dụ `/publish`. Quyết định 16 đề xuất bảng route REST, gồm route mới `GET /api/help-documents`. Đây là mâu thuẫn giữa spec và luật REST ngày 2026-10-10. Người dùng chốt bảng này, rồi builder cập nhật spec trong `KB-05`.
2. **Duyệt package (Ask first).** `Markdig`, `DocumentFormat.OpenXml`, `UglyToad.PdfPig`. Xem quyết định 3.
3. **Ngày hiệu lực gắn với tài liệu hay phiên bản.** Spec đặt `effectiveFrom` trên tài liệu. Đăng lại với ngày tương lai vì vậy ẩn cả phiên bản đang hiệu lực. Đề xuất cho MVP: giữ theo spec. Nếu cần phiên bản cũ chạy tới ngày hiệu lực của phiên bản mới, cần thêm job đổi phiên bản theo lịch.
4. **Danh mục của guide.** Module không phụ thuộc `catalog`, nên không kiểm id danh mục. Đề xuất: lưu id không kiểm (quyết định 15). Phương án khác là thêm `catalog` vào phụ thuộc của module trong `spec/CAPABILITY-MAP.md`.
5. **Màn nhập file "xem bản chuyển đổi bên cạnh bản gốc".** Trình duyệt hiện được PDF gốc từ file trên máy. Trình duyệt không hiện được DOCX. Đề xuất: PDF hiện bản gốc bên cạnh; DOCX chỉ hiện tên file và bản chuyển đổi.
6. **Văn bản chính sách thật** (Open Question 1 của spec). Có văn bản thật thì `KB-11` dùng làm dữ liệu mẫu. Chưa có thì `KB-11` soạn theo các spec đã duyệt. Ngưỡng miễn phí ship 500.000 đ và hạn mức COD 20.000.000 đ vẫn chờ chốt ở Open Question 2 của `spec/SPEC-commerce.md`.
7. **Loader của `chatbot-rag`.** `chatbot-rag` hiện chỉ nạp PDF và DOCX từ `data/docs/`. Dự án đó cần một loader đọc `GET /api/help-documents`, hoặc loader Markdown. Việc này thuộc `chatbot-rag`. Plan chỉ ghi để theo dõi. Cần chốt thêm lịch nạp lại, ví dụ mỗi giờ hoặc khi admin bấm đăng.
8. **Sửa spec theo phạm vi mới.** Đã chốt ngày 2026-10-18: người dùng duyệt. `spec/SPEC-knowledge-base.md` bỏ mục "Chia đoạn", "Index", "Truy xuất", hai tiêu chí của bộ đánh giá và tiêu chí p95.
9. **Giới hạn của nguồn cho chatbot.** Đã chốt ngày 2026-10-18: một response, tối đa 500 tài liệu, không phân trang, vì kho dự kiến dưới vài trăm tài liệu. Kho lớn hơn thì thêm phân trang theo cursor.
