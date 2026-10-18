# Spec: knowledge-base

> Đã cập nhật ngày 2026-10-18 theo phạm vi mới. Plan: `tasks/knowledge-base/plan.md`.

Module id: `knowledge-base` · Phụ thuộc: `identity` · Quy ước chung: `SPEC.md`

## Objective

Kho tri thức dạng văn bản của cửa hàng: chính sách, câu hỏi thường gặp, bài hướng dẫn chọn mua. Module lo phần soạn và nhập tài liệu, lưu phiên bản, đăng và gỡ tài liệu. Tài liệu đã đăng hiện thành trang công khai `/help`.

Module cũng cấp nguồn tài liệu công khai cho `chatbot-rag`, dự án chatbot riêng gắn vào Pickory bằng widget. `chatbot-rag` tự chia đoạn, tạo embedding và truy xuất. Admin chỉ soạn tài liệu ở Pickory, nên khách và chatbot đọc cùng một nội dung.

Dữ liệu sản phẩm và review **không** nằm ở đây. Hai nguồn đó nằm ở `catalog` và `commerce`.

**User stories**
- Là admin, tôi soạn tài liệu Markdown trên back-office, hoặc tải lên file PDF/DOCX có sẵn rồi sửa lại bản chuyển đổi trước khi đăng.
- Là admin, tôi đăng, gỡ, cập nhật tài liệu. `chatbot-rag` đọc được nội dung mới ngay sau khi đăng.
- Là khách, tôi đọc trang chính sách bảo hành, đổi trả, giao hàng, và mở link có anchor tới đúng đoạn.
- Là nhân viên `support`, `sales`, tôi tra cứu tài liệu nội bộ trên back-office khi trả lời khách.

## Thiết kế

### Loại tài liệu

| Loại | Ví dụ | Công khai |
|---|---|:-:|
| `policy` | Bảo hành, đổi trả, giao hàng, thanh toán, bảo mật dữ liệu | ✓ |
| `faq` | "Có xuất hóa đơn VAT không?", "Bao lâu thì nhận hàng?" | ✓ |
| `guide` | "Cách chọn laptop cho sinh viên", "RAM bao nhiêu là đủ?" | ✓ |
| `internal` | Quy trình xử lý khiếu nại, kịch bản trả lời | chỉ nhân viên |

- Tài liệu có: tiêu đề, slug, loại, nội dung Markdown, thẻ, danh mục sản phẩm liên quan (cho `guide`), trạng thái (`draft`, `published`, `archived`), ngày hiệu lực.
- Tài liệu `internal` **không bao giờ** có trong route công khai, kể cả nguồn cho `chatbot-rag`. Nhân viên chỉ xem tài liệu này trên back-office.
- FAQ soạn theo cấu trúc hỏi đáp: mỗi cặp là một mục riêng.
- Danh mục của `guide` lưu dạng id. Module không kiểm id với `catalog`, vì module không phụ thuộc `catalog`.

### Phiên bản

- Mỗi lần đăng tạo một phiên bản mới, không sửa đè.
- Đăng tài liệu chạy trong một transaction: tạo phiên bản, đổi phiên bản hiệu lực, ghi audit. Request đọc song song luôn thấy một phiên bản đầy đủ.
- Module không xóa phiên bản. Admin luôn mở lại được nội dung của phiên bản cũ.
- Ghi audit log khi đăng, gỡ, lưu trữ tài liệu.
- Ngày hiệu lực trong tương lai: trang công khai và nguồn cho `chatbot-rag` chưa hiện tài liệu cho tới ngày đó.

### Nhập từ file

- Nhận DOCX và PDF có lớp chữ, tối đa 10 MB.
- DOCX: chuyển sang Markdown, giữ heading, danh sách, bảng.
- PDF: trích văn bản theo trang. PDF không giữ được cấu trúc heading, nên admin thêm heading khi sửa bản nháp. Màn hình nhập nhắc rõ điều này.
- Thư viện chuyển đổi chọn ở `KB-00` và chờ người dùng duyệt (xem Open Questions).
- Kết quả **luôn là bản nháp** để admin sửa rồi mới đăng. Không đăng thẳng từ file.
- PDF chỉ có ảnh scan (không có lớp chữ) thì báo lỗi rõ ràng. MVP không làm OCR.
- File gốc không lưu lâu dài. Chỉ giữ tên file và thời điểm nhập.
- Bước chuyển đổi không dùng AI, nên không tốn phí và cho kết quả ổn định.

### Hiển thị Markdown

- Backend parse Markdown thành danh sách khối có whitelist. Frontend đổi từng khối thành thẻ React, không render HTML thô.
- Link chỉ nhận `https:`, `http:` và đường dẫn bắt đầu bằng `/`.
- Mỗi heading có anchor. Mục lục và khối tiêu đề dùng cùng hàm tạo anchor, nên link có anchor luôn trỏ đúng heading.

### Nguồn tài liệu cho `chatbot-rag`

- `GET /api/help-documents` trả mọi tài liệu công khai đã đăng trong một response.
- Mỗi tài liệu có `id`, `slug`, `title`, `type`, `content`, `url`, `updatedAt`. `content` là Markdown của phiên bản hiệu lực.
- Route dùng chung hàm truy vấn với `/api/help`. Bộ lọc nằm trong câu SQL: loại khác `internal`, trạng thái `published`, ngày hiệu lực rỗng hoặc đã tới.
- Route trả tối đa 500 tài liệu, không phân trang. Kho vượt mức này thì route trả 500 tài liệu mới nhất và log cảnh báo.
- `chatbot-rag` so `id` và `updatedAt` để biết tài liệu nào mới, đổi hay đã gỡ.

### Phân quyền

- `knowledge:write` (mặc định chỉ admin): soạn, nhập, đăng, gỡ.
- `knowledge:read` (admin, `sales`, `support`): xem mọi tài liệu kể cả `internal` và bản nháp.

*(Hai quyền này bổ sung vào danh mục quyền của `identity`.)*

## API

Route theo REST mức tài nguyên, theo quyết định 16 của plan.

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/help?type=` | công khai | Danh sách tài liệu công khai đã đăng, theo loại |
| GET | `/api/help/{slug}` | công khai | Nội dung tài liệu công khai |
| GET | `/api/help-documents` | công khai | Nguồn tài liệu công khai cho `chatbot-rag` |
| GET | `/api/admin/knowledge/docs`, `/api/admin/knowledge/docs/{id}` | `knowledge:read` | Danh sách, chi tiết |
| GET | `/api/admin/knowledge/docs/{id}/versions`, `/api/admin/knowledge/docs/{id}/versions/{versionId}` | `knowledge:read` | Lịch sử phiên bản |
| POST | `/api/admin/knowledge/docs` | `knowledge:write` | Tạo bản nháp |
| PATCH | `/api/admin/knowledge/docs/{id}` | `knowledge:write` | Sửa bản nháp; gỡ hoặc lưu trữ bằng `status` là `draft` hoặc `archived` |
| POST | `/api/admin/knowledge/docs/{id}/versions` | `knowledge:write` | Đăng: tạo phiên bản mới (201) |
| POST | `/api/admin/knowledge/imports` | `knowledge:write` | Tải file DOCX/PDF, tạo bản nháp Markdown (201) |

`PATCH` với `status` là `published` trả 400. Đăng tài liệu chỉ đi qua route tạo phiên bản.

## Data Model (Prisma, rút gọn)

```prisma
model KbDocument {
  id               String    @id @default(uuid())
  slug             String    @unique
  type             KbDocType
  title            String
  draftMd          String
  tags             String[]
  categoryIds      String[]
  status           String    // draft | published | archived
  activeVersionId  String?
  effectiveFrom    DateTime?
  importedFileName String?
  importedAt       DateTime?
  updatedAt        DateTime  @updatedAt
  @@map("kb_documents")
}

model KbDocVersion { /* id, docId, bodyMd, publishedBy, publishedAt */ }
```

## Frontend

- Công khai (`features/knowledge-base/`): trang Trung tâm hỗ trợ `/help` theo loại; trang tài liệu `/help/:slug` có mục lục và anchor cho từng heading; link chân trang tới các chính sách.
- Back-office:
  - Danh sách tài liệu.
  - Trình soạn Markdown có xem trước.
  - Nhập file: xem bản chuyển đổi bên cạnh bản gốc.
  - Lịch sử phiên bản.

## Dữ liệu ban đầu

Seed bộ tài liệu mẫu bám theo các spec đã duyệt, admin sửa lại cho đúng thực tế:
- Chính sách: giao hàng (GHN, miễn phí trên ngưỡng), thanh toán (COD, chuyển khoản SePay, hạn 30 phút), bảo hành, đổi trả, bảo mật dữ liệu (đồng ý cá nhân hóa).
- Khoảng 20 FAQ.
- 5 bài hướng dẫn chọn mua cho 5 danh mục seed.

Lệnh seed không gọi AI và chạy được khi không có mạng.

## Testing

- Unit:
  - Tạo anchor: bỏ dấu tiếng Việt, heading trùng nhận hậu tố.
  - Parse Markdown: heading, bảng, link, HTML thô hiện thành chữ.
  - Chuyển DOCX/PDF mẫu sang Markdown, giữ heading và bảng.
  - Luật tài liệu công khai.
- Integration:
  - Đăng phiên bản mới: request đọc song song không bao giờ thấy tài liệu trống.
  - Gỡ tài liệu thì tài liệu biến mất khỏi `/api/help` và `/api/help-documents`.
  - Tài liệu `internal` đã đăng không có trong `/api/help`, `/api/help/{slug}` và `/api/help-documents`.
  - Câu SQL của route công khai có điều kiện loại tài liệu.
  - Đăng bản nháp không có chữ trả 422.

## Boundaries (riêng module)

- **Always:**
  - Lọc tài liệu công khai ngay trong câu truy vấn DB.
  - Đổi phiên bản hiệu lực trong một transaction.
- **Ask first:** thêm OCR; tự động đăng tài liệu nhập từ file; cho role ngoài admin quyền `knowledge:write`.
- **Never:**
  - Trả tài liệu `internal` qua route công khai, kể cả nguồn cho `chatbot-rag`.
  - Đưa thông tin cá nhân của khách vào kho tri thức.
  - Xóa phiên bản cũ.
  - Gọi API AI.

## Success Criteria

- [ ] Admin soạn, đăng, gỡ tài liệu; `GET /api/help-documents` trả nội dung mới ngay sau khi route đăng trả 201.
- [ ] Nhập DOCX và PDF mẫu ra bản nháp Markdown giữ đúng heading; PDF ảnh scan báo lỗi rõ ràng.
- [ ] Không có trường hợp nào tài liệu `internal` lọt vào route công khai, kể cả nguồn cho `chatbot-rag` (test tự động).
- [ ] Link `/help/{slug}#{anchor}` mở đúng trang và cuộn tới đúng heading.
- [ ] Coverage dòng của `Pickory.Modules.KnowledgeBase` ít nhất 80%, gộp unit và tích hợp.

## Phần đã bỏ ngày 2026-10-18

Chatbot chuyển sang dự án riêng `chatbot-rag`, và Pickory gỡ `ai-gateway`. Các phần sau bị bỏ:

- Chia đoạn theo heading và theo token: `chatbot-rag` tự chia đoạn khi nạp tài liệu.
- Index embedding, bảng `kb_chunks`, chỉ mục HNSW và `search_vector`: Pickory không tạo embedding.
- Truy xuất hybrid `retrieve`, `audience` và ngưỡng liên quan: `chatbot-rag` tự truy xuất.
- Route và màn hình "Thử truy xuất": module không còn truy xuất.
- Bộ đánh giá truy xuất `kb:eval` và tiêu chí Recall@5: chuyển sang bộ đánh giá của `chatbot-rag`.
- Tiêu chí p95 của `retrieve`: module không còn `retrieve`.
- Gợi ý câu trả lời cho `support-desk`: `support-desk` làm độc lập, không đọc kho tri thức.

## Open Questions

1. Bạn đã có sẵn văn bản chính sách (bảo hành, đổi trả...) chưa? Nếu có, mình dùng làm dữ liệu seed thay cho bản mẫu.
2. Có cần OCR cho PDF scan không? Mặc định: không trong MVP.
3. Bảng route REST ở mục API theo quyết định 16 của plan. Người dùng chưa chốt bảng này (Câu hỏi mở 1 của plan).
4. Thư viện .NET cho nhập file và parse Markdown chờ duyệt ở `KB-00` (Câu hỏi mở 2 của plan).
5. Ngày hiệu lực gắn với tài liệu hay phiên bản. Spec giữ trên tài liệu (Câu hỏi mở 3 của plan).
6. Màn nhập file: trình duyệt không hiện được DOCX gốc. Plan đề xuất chỉ PDF hiện bản gốc bên cạnh (Câu hỏi mở 5 của plan).
7. `chatbot-rag` cần loader đọc `GET /api/help-documents` và lịch nạp lại. Việc này thuộc dự án `chatbot-rag` (Câu hỏi mở 7 của plan).
