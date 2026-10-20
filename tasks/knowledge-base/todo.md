# Task List: knowledge-base

Plan và quyết định thiết kế: `tasks/knowledge-base/plan.md`. Spec: `spec/SPEC-knowledge-base.md`. Quy ước chung: `spec/SPEC.md`, `tasks/dotnet-migration/plan.md`, `CLAUDE.md`.

Ngày 2026-10-18 người dùng bỏ phần AI của module: chia chunk, embedding, truy xuất, bộ đánh giá. Chatbot chuyển sang dự án riêng `chatbot-rag`. Xem mục "Thay đổi phạm vi ngày 2026-10-18" của plan. Task bỏ giữ nguyên nội dung để tra cứu, có dòng "Trạng thái: đã bỏ".

Ngày 2026-10-20 KB-14 chuyển sang Phase 5, làm cùng `chatbot-widget` ở sprint cuối. Xem mục "Thay đổi thứ tự ngày 2026-10-20" của plan.

Quy ước:
- Mỗi task backend phải qua `dotnet build` và `dotnet test` trong `apps/backend` trước khi báo xong.
- Mỗi task frontend phải qua `pnpm lint && pnpm typecheck && pnpm test` trong `apps/frontend`.
- Task back-office thêm endpoint mới vào `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`.
- Task UI theo `docs/design/design-system.md` và `tasks/design/plan.md`. Kiểm bằng Playwright: console 0 lỗi và 0 cảnh báo.
- Mỗi test mới đi qua ít nhất một phép thử đột biến trên luật nó bảo vệ.
- Module không gọi `IAiGateway`, không tạo embedding và không dùng pgvector.
- Kích thước: XS 1 file, S 1-2, M 3-5, L 6-8. Số file không tính file test.

---

## Phase 0: Chọn thư viện

### KB-00: Chọn thư viện .NET cho nhập file và parse Markdown
**Mô tả:** So sánh package đề xuất ở quyết định 3 của plan với phương án tự viết. Chạy thử trên DOCX và PDF mẫu tạo bằng code. Ghi kết quả vào `docs/adr/0005-knowledge-ingestion-dotnet.md`. Trình danh sách package cho người dùng duyệt, vì thêm dependency thuộc nhóm "Ask first". Sửa spec theo phạm vi mới nếu người dùng duyệt Câu hỏi mở 8 của plan.
**Tiêu chí nghiệm thu:**
- [ ] ADR ghi tên, phiên bản, giấy phép và lý do chọn của từng package
- [ ] ADR ghi phương án tự viết cho từng package và chi phí của phương án đó
- [ ] Thử DOCX có Heading 1 đến 3, danh sách và bảng: Markdown ra giữ đủ heading và bảng
- [ ] Thử PDF có lớp chữ: trích được văn bản theo trang
- [ ] Thử PDF chỉ có ảnh: phát hiện được trang không có lớp chữ
- [ ] Người dùng duyệt danh sách package. Ghi ngày duyệt vào ADR
- [ ] Sau khi duyệt, cập nhật hàng "RAG tooling" của `spec/SPEC.md` và thêm package vào `apps/backend/Directory.Packages.props`
- [ ] Người dùng duyệt Câu hỏi mở 8: `spec/SPEC-knowledge-base.md` bỏ mục chia đoạn, index, truy xuất và ba tiêu chí của bộ đánh giá, p95
**Kiểm tra:** script tạm ngoài repo, output thật trong ADR
**Phụ thuộc:** không · **Quy mô:** S
**File:** `docs/adr/0005-knowledge-ingestion-dotnet.md`, `spec/SPEC.md`, `spec/SPEC-knowledge-base.md`, `apps/backend/Directory.Packages.props`

### Checkpoint K0
- [ ] Người dùng duyệt danh sách package và route REST (Câu hỏi mở 1 và 2 của plan)
- [ ] Review với người dùng

---

## Phase 1: Nền tảng

### KB-01: Logic thuần
**Mô tả:** Các hàm thuần, không gọi DB và không gọi HTTP.
- `HeadingAnchor.Create(text, usedAnchors)`: anchor theo quyết định 6 của plan.
- `DocumentStatusRules.CanChange(from, to)`: bảng trạng thái ở quyết định 12.
- `PublicDocumentRules.IsPublic(type, status, effectiveFrom, now)`: luật tài liệu công khai ở quyết định 9 và 11.
**Tiêu chí nghiệm thu:**
- [ ] "Điều kiện bảo hành" thành `dieu-kien-bao-hanh`. "Đổi trả" thành `doi-tra`
- [ ] Heading trùng trong một tài liệu nhận `-2`, `-3`. Heading chỉ có ký hiệu nhận anchor `muc`
- [ ] Mọi cặp trạng thái trong bảng của plan trả đúng giá trị. Cặp ngoài bảng trả `false`
- [ ] Tài liệu `internal` không bao giờ công khai, kể cả khi đã đăng và tới ngày hiệu lực
- [ ] Tài liệu `draft` hoặc `archived` không công khai
- [ ] Tài liệu có `effectiveFrom` sau `now` không công khai. Đúng mốc `effectiveFrom` thì công khai
**Kiểm tra:** unit test
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.KnowledgeBase/Documents/{HeadingAnchor.cs, DocumentStatusRules.cs, PublicDocumentRules.cs}`, test trong `apps/backend/tests/Pickory.UnitTests/Modules/KnowledgeBase/`

### KB-02: Khung module, Contracts, schema và migration
**Mô tả:** Tạo `Pickory.Modules.KnowledgeBase` và `Pickory.Modules.KnowledgeBase.Contracts`. Contracts chỉ có `KbDocType` theo quyết định 2 của plan. Thêm `KnowledgeBaseDbContext`, migration đầu tiên và `KnowledgeBaseModule.cs`.
**Tiêu chí nghiệm thu:**
- [ ] Bảng `kb_documents` và `kb_doc_versions` theo Data Model của spec, cộng các cột ở quyết định 14 của plan
- [ ] Không có bảng `kb_chunks`, cột vector hay cột `tsvector`
- [ ] `kb_documents.slug` có ràng buộc unique
- [ ] Migration chạy được trên DB sạch
- [ ] Lệnh `e2e:start` và fixture test tích hợp dựng được schema mới
- [ ] `Pickory.ArchitectureTests` chặn `KnowledgeBase` tham chiếu module khác ngoài project Contracts
- [ ] `Pickory.ArchitectureTests` chặn `KnowledgeBase` tham chiếu `Pickory.Modules.AiGateway.Contracts`
- [ ] `Program.cs` chỉ gọi `AddKnowledgeBaseModule` và `MapKnowledgeBaseEndpoints`
**Kiểm tra:** build, test kiến trúc, test tích hợp schema
**Phụ thuộc:** KB-01 · **Quy mô:** M

### KB-03: Parse Markdown thành khối hiển thị
**Mô tả:** `MarkdownBlockParser` dùng `Markdig` (hoặc phương án đã chốt ở KB-00) đổi Markdown thành danh sách khối theo whitelist ở quyết định 4. Mỗi heading mang `anchor` từ `HeadingAnchor`. Hàm cũng trả mục lục gồm heading cấp 2 và 3.
**Tiêu chí nghiệm thu:**
- [ ] Mỗi loại khối trong whitelist ở quyết định 4 của plan có ít nhất một test
- [ ] `<script>`, `<img onerror>` và HTML thô khác thành khối chữ, không thành thẻ
- [ ] Link `javascript:` và `data:` thành chữ thường. Link `https:`, `http:` và `/help/...` giữ nguyên
- [ ] Tiêu đề cấp 1 trong nội dung hạ thành cấp 2
- [ ] Mục lục đúng thứ tự, anchor trùng anchor của khối tiêu đề tương ứng
- [ ] Markdown rỗng trả danh sách rỗng, không ném lỗi
**Kiểm tra:** unit test với bộ Markdown mẫu
**Phụ thuộc:** KB-00, KB-02 · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.KnowledgeBase/Markdown/{MarkdownBlockParser.cs, MarkdownBlock.cs}`

### KB-04: Chia chunk
Trạng thái: đã bỏ ngày 2026-10-18. `chatbot-rag` tự chia chunk khi nạp tài liệu, nên Pickory không chia chunk.
**Mô tả:** `ChunkSplitter.Split(docType, title, markdown)` trả danh sách chunk theo quyết định 7. Mỗi chunk có đường dẫn heading, anchor, nội dung và số token. `TokenCounter` đếm token theo bộ mã chốt ở KB-00.
**Tiêu chí nghiệm thu:**
- [ ] Mỗi H2 và H3 mở một chunk mới. Đường dẫn heading bắt đầu bằng tiêu đề tài liệu
- [ ] Mục dài hơn 500 token bị cắt. Mọi chunk thường có 300 đến 500 token, trừ chunk cuối của một mục
- [ ] Hai chunk liền nhau trong cùng mục chồng lấp khoảng 50 token
- [ ] Bảng 700 token nằm nguyên trong một chunk
- [ ] Tài liệu `faq`: mỗi cặp H2 và câu trả lời là đúng một chunk
- [ ] Anchor của chunk bằng anchor của khối tiêu đề do `MarkdownBlockParser` tạo cho cùng heading
- [ ] Văn bản embed của chunk bắt đầu bằng đường dẫn heading
**Kiểm tra:** unit test
**Phụ thuộc:** KB-03 · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.KnowledgeBase/Chunking/{ChunkSplitter.cs, TokenCounter.cs, DocumentChunk.cs}`

### Checkpoint K1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `KnowledgeBase` tham chiếu module khác ngoài Contracts
- [ ] Parse đúng bộ Markdown mẫu: heading, bảng, link, HTML thô
- [ ] Review với người dùng

---

## Phase 2: Soạn, đăng và nhập file

### KB-05: API bản nháp và lịch sử phiên bản
**Mô tả:** Route đọc, tạo, sửa bản nháp và đọc phiên bản theo bảng ở quyết định 16 của plan. Response chi tiết có `draftBlocks` cho bản xem trước (quyết định 5). Cập nhật bảng API của `spec/SPEC-knowledge-base.md` theo route người dùng đã chốt.
**Tiêu chí nghiệm thu:**
- [ ] Không có `knowledge:read` thì route đọc trả 403. Không có `knowledge:write` thì route ghi trả 403
- [ ] `sales` và `support` đọc được tài liệu `internal` và bản nháp. Hai role này sửa thì nhận 403
- [ ] Endpoint mới có trong ma trận quyền
- [ ] Tạo tài liệu không gửi slug thì slug sinh từ tiêu đề. Slug trùng trả 409 `KB_SLUG_TAKEN`
- [ ] Dữ liệu vượt giới hạn ở quyết định 18 trả 400 `VALIDATION_ERROR`
- [ ] `PATCH` với `status` là `published` trả 400
- [ ] Id không có trả 404 `KB_DOC_NOT_FOUND`
- [ ] Danh sách lọc được theo loại, trạng thái và từ khóa tiêu đề, có phân trang
- [ ] `GET .../versions/{versionId}` trả nội dung Markdown của đúng phiên bản đó
**Kiểm tra:** test tích hợp
**Phụ thuộc:** KB-03 · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.KnowledgeBase/Features/AdminDocuments/`, `spec/SPEC-knowledge-base.md`

### KB-UI2: Danh sách tài liệu và trình soạn
**Mô tả:** Màn `/admin/knowledge` liệt kê tài liệu. Màn `/admin/knowledge/docs/{id}` có ô soạn Markdown và bản xem trước từ `draftBlocks`. Thêm mục "Kho tri thức" vào `ADMIN_NAV_GROUPS` với quyền `knowledge:read`. Component hiện khối dùng chung với trang công khai.
**Tiêu chí nghiệm thu:**
- [ ] Ngừng gõ 1 giây thì bản nháp tự lưu. Trạng thái "Đã lưu" hoặc lỗi hiện cạnh ô soạn
- [ ] Bản xem trước khớp `draftBlocks` mới nhất. Component hiện khối không dùng `dangerouslySetInnerHTML`
- [ ] Người chỉ có `knowledge:read` thấy màn ở chế độ chỉ đọc, không có nút lưu
- [ ] Người thiếu `knowledge:read` không thấy menu. Mở thẳng URL thấy trang không đủ quyền
- [ ] Chọn danh mục cho guide từ `GET /api/categories`
- [ ] Dùng được bằng bàn phím. Mọi ô có `label`. Lỗi hiện dưới ô bằng `FieldError`
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright: console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`; ảnh chụp
**Phụ thuộc:** KB-05 · **Quy mô:** M
**File:** `apps/frontend/src/features/knowledge-base/admin/`, `apps/frontend/src/features/knowledge-base/components/`, `apps/frontend/src/features/identity/admin/admin-navigation.ts`, `apps/frontend/src/app.tsx`

### KB-06: Đăng, gỡ và lưu trữ tài liệu
**Mô tả:** `POST /api/admin/knowledge/docs/{id}/versions` đăng bản nháp theo các bước ở quyết định 8. `PATCH` với `status` là `draft` hoặc `archived` gỡ hoặc lưu trữ tài liệu. Mỗi thao tác ghi audit trong cùng transaction. Đăng tài liệu không gọi AI.
**Tiêu chí nghiệm thu:**
- [ ] Đăng tạo đúng 1 dòng `kb_doc_versions` và đổi `activeVersionId`. Route trả 201
- [ ] Chạy 20 lần đọc `GET /api/help/{slug}` song song trong lúc đăng phiên bản mới: không lần nào trả 404 hay nội dung trống
- [ ] Bản nháp không có chữ trả 422 `KB_EMPTY_DOCUMENT`
- [ ] Gỡ hoặc lưu trữ làm tài liệu biến mất khỏi route công khai ngay. Chuyển trạng thái sai trả 409 `KB_INVALID_STATUS_TRANSITION`
- [ ] Mỗi lần đăng, gỡ, lưu trữ ghi đúng 1 dòng audit với mã ở quyết định 12
- [ ] Lưu trữ giữ mọi dòng `kb_doc_versions`. Module không có route xóa tài liệu hay phiên bản
- [ ] Hai request đăng cùng tài liệu đồng thời: một request thành công, DB có đúng 1 phiên bản hiệu lực
**Kiểm tra:** test tích hợp
**Phụ thuộc:** KB-05 · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.KnowledgeBase/Features/Publishing/`

### KB-07: Truy xuất hybrid
Trạng thái: đã bỏ ngày 2026-10-18. `chatbot-rag` tự truy xuất trên index của nó, và không module Pickory nào còn đọc kho tri thức.
**Mô tả:** `KnowledgeRetriever` cài `IKnowledgeRetriever.RetrieveAsync` theo quyết định 9, 10 và 11. Nhánh vector gọi `EmbedAsync` với `AiFeature.KnowledgeBaseQuery`. Hai nhánh chạy song song.
**Tiêu chí nghiệm thu:**
- [ ] `Customer` không bao giờ nhận chunk `internal`, kể cả khi câu hỏi chép nguyên câu của tài liệu `internal`
- [ ] Câu SQL của `Customer` có điều kiện `doc_type`. Test đọc câu SQL do EF Core sinh để chứng minh
- [ ] `Staff` nhận được chunk `internal`
- [ ] Gỡ tài liệu thì truy xuất không còn trả chunk của tài liệu đó
- [ ] Câu hỏi không liên quan trả mảng rỗng
- [ ] Hit có cosine dưới `KB_RELEVANCE_THRESHOLD` bị loại
- [ ] Embedding lỗi thì vẫn trả kết quả từ nhánh toàn văn, không ném lỗi
- [ ] Tìm không dấu "bao hanh" ra chunk có chữ "bảo hành"
- [ ] Tài liệu có `effectiveFrom` trong tương lai không được trả
- [ ] Guide có `categoryId` khớp xếp trên guide khác cùng điểm gốc
- [ ] `Types` lọc đúng loại. `K` ngoài khoảng 1 đến 10 bị kéo về khoảng đó
- [ ] `KbHit.Url` của tài liệu công khai là `/help/{slug}#{anchor}`. Tài liệu `internal` có URL back-office
- [ ] Truy xuất ngay sau khi route đăng trả 201 thấy nội dung mới
**Kiểm tra:** test tích hợp với PostgreSQL thật và handler embedding giả
**Phụ thuộc:** KB-06 · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.KnowledgeBase/Retrieval/`

### KB-08: Route thử truy xuất
Trạng thái: đã bỏ ngày 2026-10-18. Module không còn truy xuất, nên không có gì để thử.
**Mô tả:** `GET /api/admin/knowledge/retrievals?query=&audience=&types=` gọi `RetrieveAsync` và trả mọi hit kèm điểm RRF và cosine. Route giúp admin biết tài liệu đã đủ rõ chưa.
**Tiêu chí nghiệm thu:**
- [ ] Thiếu `knowledge:read` trả 403. Endpoint có trong ma trận quyền
- [ ] Thiếu `query` hoặc `query` dài hơn 500 ký tự trả 400 `VALIDATION_ERROR`
- [ ] Mặc định `audience` là `customer`
- [ ] Mỗi hit có đủ trường của `KbHit`, cộng điểm RRF và cosine
**Kiểm tra:** test tích hợp
**Phụ thuộc:** KB-07 · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.KnowledgeBase/Features/AdminRetrievals/`

### KB-UI4: Màn thử truy xuất
Trạng thái: đã bỏ ngày 2026-10-18. Route `KB-08` đã bỏ.
**Mô tả:** Màn `/admin/knowledge/retrievals`. Admin nhập câu hỏi, chọn đối tượng đọc và loại tài liệu, rồi xem danh sách chunk kèm điểm.
**Tiêu chí nghiệm thu:**
- [ ] Kết quả rỗng hiện thông báo "Không có kết quả nào vượt ngưỡng liên quan"
- [ ] Mỗi hit có link mở đúng heading trên trang công khai hoặc màn back-office
- [ ] Lỗi AI hiện thông báo tiếng Việt, màn không trắng
- [ ] Dùng được bằng bàn phím. Mọi ô có `label`
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright: console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright; ảnh chụp
**Phụ thuộc:** KB-08, KB-UI2 · **Quy mô:** S
**File:** `apps/frontend/src/features/knowledge-base/admin/retrieval-test-page.tsx`

### KB-09: Nhập DOCX và PDF
**Mô tả:** `POST /api/admin/knowledge/imports` nhận file multipart và tạo tài liệu `draft` theo quyết định 14. `DocxToMarkdown` giữ heading, danh sách và bảng. `PdfToMarkdown` trích văn bản theo trang. Bước chuyển đổi không gọi AI.
**Tiêu chí nghiệm thu:**
- [ ] DOCX có Heading 1 đến 3, danh sách và bảng ra Markdown giữ đủ cấu trúc đó
- [ ] PDF có lớp chữ ra bản nháp có văn bản đủ các trang
- [ ] Response nhập PDF kèm cảnh báo "PDF không giữ heading, hãy thêm heading trước khi đăng"
- [ ] PDF chỉ có ảnh trả 422 `KB_PDF_NO_TEXT` với thông báo tiếng Việt
- [ ] File 11 MB trả 413 `PAYLOAD_TOO_LARGE`. File 9 MB được nhận
- [ ] Gửi body 2 MB vào route khác vẫn trả 413. Giới hạn 1 MB của route khác không đổi
- [ ] File không phải DOCX hoặc PDF trả 415 `KB_FILE_UNSUPPORTED`. Module kiểm chữ ký file, không chỉ kiểm đuôi
- [ ] Tài liệu tạo ra có `status` là `draft`, có `importedFileName` và `importedAt`
- [ ] DB và ổ đĩa server không còn file gốc sau request
- [ ] Thiếu `knowledge:write` trả 403. Endpoint có trong ma trận quyền
**Kiểm tra:** unit test với DOCX và PDF tạo bằng code trong test; test tích hợp
**Phụ thuộc:** KB-00, KB-05 · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.KnowledgeBase/Importing/`, `apps/backend/src/Pickory.Modules.KnowledgeBase/Features/AdminImports/`, `apps/backend/src/Pickory.Api/Middleware/ExceptionHandlingMiddleware.cs`

### KB-UI3: Nhập file, lịch sử phiên bản và nút đăng
**Mô tả:** Thêm vào màn soạn: nút đăng, gỡ, lưu trữ theo trạng thái; danh sách phiên bản; hộp thoại nhập file. Màn nhập file hiện bản chuyển đổi bên cạnh bản gốc theo đề xuất ở Câu hỏi mở 5 của plan.
**Tiêu chí nghiệm thu:**
- [ ] Nút chỉ hiện khi chuyển trạng thái đó hợp lệ. Lưu trữ và gỡ cần xác nhận trong hộp thoại
- [ ] Đăng lỗi hiện thông báo tiếng Việt và nút "Đăng lại". Phiên bản đang hiệu lực vẫn hiện đúng
- [ ] Lịch sử phiên bản hiện người đăng và thời điểm theo giờ `Asia/Ho_Chi_Minh`. Bấm một dòng thấy nội dung phiên bản đó
- [ ] Màn nhập file nhắc rõ PDF không giữ heading
- [ ] Lỗi 413, 415 và 422 hiện thông báo tiếng Việt dưới ô chọn file
- [ ] Hộp thoại giữ focus bên trong và đóng được bằng phím Esc
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright: console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright `--headed`; ảnh chụp
**Phụ thuộc:** KB-06, KB-09, KB-UI2 · **Quy mô:** M
**File:** `apps/frontend/src/features/knowledge-base/admin/`

### Checkpoint K2
- [ ] Admin soạn, nhập file, đăng, gỡ tài liệu trên giao diện
- [ ] Đăng phiên bản mới không làm request đọc song song thấy tài liệu trống
- [ ] Review với người dùng

---

## Phase 3: Trang công khai và dữ liệu mẫu

### KB-10: API công khai
**Mô tả:** `GET /api/help?type=` liệt kê tài liệu công khai đã đăng, nhóm theo loại. `GET /api/help/{slug}` trả thông tin tài liệu, khối nội dung và mục lục. Dữ liệu lấy từ phiên bản hiệu lực. Hai route dùng hàm truy vấn tài liệu công khai của quyết định 9.
**Tiêu chí nghiệm thu:**
- [ ] Danh sách không có tài liệu `internal`, `draft`, `archived` hoặc tài liệu có ngày hiệu lực trong tương lai
- [ ] Slug của bốn loại tài liệu trên trả 404 `KB_DOC_NOT_FOUND`, không lộ việc tài liệu tồn tại
- [ ] Câu SQL của route có điều kiện `doc_type`. Test đọc câu SQL do EF Core sinh để chứng minh
- [ ] Nội dung lấy từ phiên bản hiệu lực, không lấy từ bản nháp đang sửa
- [ ] Anchor của mọi khối tiêu đề khớp anchor của mục lục cùng heading
- [ ] Route không cần phiên và không gọi AI
**Kiểm tra:** test tích hợp
**Phụ thuộc:** KB-06 · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.KnowledgeBase/Features/HelpCenter/`

### KB-UI1: Trung tâm hỗ trợ
**Mô tả:** Trang `/help` liệt kê tài liệu theo loại. Trang `/help/:slug` có mục lục và anchor cho từng heading. Thêm link tới các chính sách vào chân trang mà `support-desk` đã dựng.
**Tiêu chí nghiệm thu:**
- [ ] Trang `/help` có link "Liên hệ CSKH" tới `/support`
- [ ] Chân trang dùng lại component của `support-desk`, không có hai chân trang trên cùng trang
- [ ] Mở `/help/{slug}#{anchor}` cuộn tới đúng heading sau khi nội dung tải xong
- [ ] Bấm mục trong mục lục cuộn tới heading và cập nhật hash trên URL
- [ ] Trang dùng component hiện khối của KB-UI2, không dùng `dangerouslySetInnerHTML`
- [ ] Slug không có hiện trang "Không tìm thấy tài liệu"
- [ ] Chân trang có link tới mọi tài liệu `policy` đã đăng
- [ ] Tiêu đề tài liệu là H1 duy nhất. Heading trong nội dung bắt đầu từ H2
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang. Bảng rộng cuộn trong khung riêng
- [ ] Playwright: console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright; ảnh chụp
**Phụ thuộc:** KB-10, KB-UI2 · **Quy mô:** M
**File:** `apps/frontend/src/features/knowledge-base/customer/`, `apps/frontend/src/shared/`, `apps/frontend/src/app.tsx`

### KB-11: Dữ liệu mẫu `knowledge:seed`
**Mô tả:** Lệnh `dotnet run --project apps/backend/src/Pickory.Api -- knowledge:seed` ghi bộ tài liệu mẫu theo mục "Dữ liệu ban đầu" của spec rồi đăng qua cùng service của KB-06. Nội dung bám các spec đã duyệt. Nếu người dùng gửi văn bản chính sách thật, dùng văn bản đó (Câu hỏi mở 6 của plan).
**Tiêu chí nghiệm thu:**
- [ ] Có đủ 5 chính sách theo mục "Dữ liệu ban đầu" của spec
- [ ] Có khoảng 20 FAQ và 5 guide cho 5 danh mục của `catalog:seed`
- [ ] Có ít nhất 1 tài liệu `internal` để test lọc tài liệu công khai
- [ ] Số liệu trong chính sách khớp spec: miễn phí ship từ 500.000 đ, hạn chuyển khoản 30 phút, COD
- [ ] Chạy lại không tạo trùng và không tạo phiên bản mới khi nội dung không đổi
- [ ] Từ chối khi `ASPNETCORE_ENVIRONMENT` là `Production`
- [ ] Lệnh không gọi AI và chạy xong khi không có mạng
**Kiểm tra:** test tích hợp gọi seeder hai lần
**Phụ thuộc:** KB-06 · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.KnowledgeBase/Seeding/`

### Checkpoint K3
- [ ] Khách đọc `/help` và mở link có anchor cuộn tới đúng heading
- [ ] `knowledge:seed` tạo đủ bộ tài liệu mẫu trên DB sạch
- [ ] Review với người dùng

---

## Phase 4: Nghiệm thu

### KB-12: Bộ đánh giá và đo p95
Trạng thái: đã bỏ ngày 2026-10-18. Module không còn truy xuất. `chatbot-rag` có bộ đánh giá riêng.
**Mô tả:** Soạn `Evaluation/eval-questions.json` gồm 40 câu có đáp án và 10 câu không có đáp án. Mỗi câu có đáp án ghi tài liệu và heading đúng. Lệnh `knowledge:eval` in Recall@5 và tỉ lệ trả rỗng đúng. Lệnh `knowledge:benchmark` đo p95 của `RetrieveAsync`.
**Tiêu chí nghiệm thu:**
- [ ] Bộ câu hỏi phủ cả `policy`, `faq` và `guide`, dựa trên dữ liệu của KB-11
- [ ] `knowledge:eval` chỉ chạy khi tên DB chứa `eval`. `knowledge:benchmark` chỉ chạy khi tên DB chứa `bench`
- [ ] Lệnh in số câu đạt, Recall@5, tỉ lệ trả rỗng đúng và danh sách câu trượt
- [ ] `knowledge:benchmark` chạy ít nhất 200 lần truy xuất với cache embedding đã có, in p95. p95 dưới 300 ms
- [ ] Khi có API key: Recall@5 ít nhất 0,9 trên 40 câu có đáp án
- [ ] Khi có API key: ít nhất 8/10 câu không có đáp án trả mảng rỗng
- [ ] Khi có API key: ghi ngưỡng liên quan đã chỉnh vào `docs/adr/0005-knowledge-ingestion-dotnet.md`
- [ ] Không có API key: ghi rõ ba tiêu chí trên còn mở và lý do
**Kiểm tra:** output của hai lệnh
**Phụ thuộc:** KB-07, KB-11 · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.KnowledgeBase/Evaluation/`

### KB-E2E1: Luồng E2E của knowledge-base
**Mô tả:** Playwright chạy trên DB `pickory_e2e`. Startup E2E chạy `knowledge:seed` hoặc tạo dữ liệu qua giao diện.
**Tiêu chí nghiệm thu:**
- [ ] Admin soạn tài liệu `policy` có 2 heading, đăng, rồi khách mở `/help/{slug}` thấy nội dung
- [ ] Khách mở `/help/{slug}#{anchor}` của heading thứ hai, trang cuộn tới đúng heading
- [ ] Admin nhập DOCX tạo trong test, thấy bản nháp giữ heading
- [ ] Admin gỡ tài liệu, khách mở lại slug thấy "Không tìm thấy tài liệu"
- [ ] Tài liệu `internal` không có trên `/help`
- [ ] Nhân viên `support` mở màn tài liệu thấy chế độ chỉ đọc
- [ ] Mọi bước: console 0 lỗi và 0 cảnh báo
**Kiểm tra:** `pnpm test:e2e`, chạy ở chế độ có giao diện và chế độ ẩn
**Phụ thuộc:** KB-UI1, KB-UI3, KB-11 · **Quy mô:** M
**File:** `apps/frontend/e2e/knowledge-base-*.spec.ts`

### KB-13: Nghiệm thu knowledge-base
**Mô tả:** Kiểm toàn module. Nguồn tài liệu cho `chatbot-rag` (KB-14) nghiệm thu riêng ở Phase 5.
**Tiêu chí nghiệm thu:**
- [ ] Ma trận quyền có mọi endpoint back-office của `knowledge-base`
- [ ] Coverage dòng của `Pickory.Modules.KnowledgeBase` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
- [ ] Test tự động chứng minh route công khai không trả tài liệu `internal` vẫn xanh
- [ ] Module không tham chiếu `IAiGateway`, không có bảng hay cột vector
**Kiểm tra:** `dotnet test`, script gộp coverage, test kiến trúc
**Phụ thuộc:** KB-E2E1 · **Quy mô:** S

## Checkpoint cuối
- [ ] Mọi dòng Traceability đạt, trừ dòng ghi "đã bỏ"
- [ ] Coverage dòng của `Pickory.Modules.KnowledgeBase` ít nhất 80%, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `chatbot-widget` và Phase 5

---

## Phase 5: Nguồn cho chatbot

Làm cùng `chatbot-widget` ở sprint cuối.

### KB-14: Nguồn tài liệu cho `chatbot-rag`
**Thứ tự:** ngày 2026-10-20 task chuyển xuống sprint cuối, làm cùng `chatbot-widget`. Ba bước E2E của nguồn tài liệu chuyển từ KB-E2E1 sang task này.
**Mô tả:** `GET /api/help-documents` trả mọi tài liệu công khai đã đăng theo quyết định 21 của plan. `chatbot-rag` gọi route này để nạp tài liệu, nên admin chỉ soạn tài liệu ở Pickory. Route dùng hàm truy vấn tài liệu công khai của `KB-10`.
**Tiêu chí nghiệm thu:**
- [ ] Mỗi tài liệu có `id`, `slug`, `title`, `type`, `content`, `url`, `updatedAt`
- [ ] `content` là Markdown của phiên bản hiệu lực, không phải bản nháp đang sửa
- [ ] Tài liệu `internal` đã đăng không có trong response, kể cả khi tài liệu đó mới nhất
- [ ] Tài liệu `draft`, `archived` hoặc chưa tới ngày hiệu lực không có trong response
- [ ] Câu SQL của route có điều kiện `doc_type`. Test đọc câu SQL do EF Core sinh để chứng minh
- [ ] Đăng phiên bản mới thì `updatedAt` và `content` đổi ngay sau khi route đăng trả 201
- [ ] Gỡ tài liệu thì tài liệu biến mất khỏi response ngay
- [ ] Route không cần phiên và không gọi AI
- [ ] Kho có 501 tài liệu công khai: response có 500 tài liệu mới nhất, và API ghi một dòng log cảnh báo
- [ ] Cập nhật bảng API của `spec/SPEC-knowledge-base.md` với route mới
- [ ] E2E: admin đăng tài liệu trên giao diện, `GET /api/help-documents` có tài liệu với nội dung mới
- [ ] E2E: admin gỡ tài liệu, `GET /api/help-documents` không còn tài liệu đó
- [ ] E2E: tài liệu `internal` đã đăng không có trong `GET /api/help-documents`
**Kiểm tra:** test tích hợp; phép thử đột biến bỏ điều kiện `doc_type` phải làm test đỏ; `pnpm test:e2e`
**Phụ thuộc:** KB-06, KB-10, KB-E2E1 · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.KnowledgeBase/Features/HelpDocumentsFeed/`, `spec/SPEC-knowledge-base.md`

### Checkpoint K5
- [ ] `GET /api/help-documents` không trả tài liệu `internal`, `draft`, `archived` hay chưa tới ngày hiệu lực
- [ ] E2E: admin đăng hoặc gỡ tài liệu thì `GET /api/help-documents` đổi theo ngay
- [ ] Review với người dùng
