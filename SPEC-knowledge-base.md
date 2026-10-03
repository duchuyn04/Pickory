# Spec: knowledge-base

Module id: `knowledge-base` · Phụ thuộc: `identity`, `ai-gateway` · Quy ước chung: `SPEC.md`

## Objective

Kho tri thức dạng văn bản của cửa hàng: chính sách, câu hỏi thường gặp, bài hướng dẫn chọn mua. Module lo trọn phần **soạn và nhập tài liệu, chia đoạn, tạo embedding, truy xuất**. Đây là nguồn RAG cho `shopping-assistant` (chatbot trả lời có trích dẫn) và `support-desk` (gợi ý câu trả lời cho CSKH). Tài liệu đã đăng cũng hiện thành trang công khai, nên khách và chatbot luôn đọc cùng một nội dung.

Dữ liệu sản phẩm và review **không** nằm ở đây. Hai nguồn đó lấy trực tiếp từ `catalog`, `smart-search`, `commerce`, `personalized-content` để luôn đúng giá và tồn kho hiện tại.

**User stories**
- Là admin, tôi soạn tài liệu Markdown trên back-office, hoặc tải lên file PDF/DOCX có sẵn rồi sửa lại bản chuyển đổi trước khi đăng.
- Là admin, tôi đăng, gỡ, cập nhật tài liệu. Chatbot dùng nội dung mới ngay sau khi đăng.
- Là admin, tôi thử một câu hỏi và xem hệ thống truy xuất được đoạn nào, để biết tài liệu đã đủ rõ chưa.
- Là khách, tôi đọc trang chính sách bảo hành, đổi trả, giao hàng, và mở được đúng đoạn mà chatbot trích dẫn.
- Là nhân viên `support`, `sales`, tôi tra cứu tài liệu nội bộ khi trả lời khách.

## Thiết kế

### Loại tài liệu

| Loại | Ví dụ | Công khai |
|---|---|:-:|
| `policy` | Bảo hành, đổi trả, giao hàng, thanh toán, bảo mật dữ liệu | ✓ |
| `faq` | "Có xuất hóa đơn VAT không?", "Bao lâu thì nhận hàng?" | ✓ |
| `guide` | "Cách chọn laptop cho sinh viên", "RAM bao nhiêu là đủ?" | ✓ |
| `internal` | Quy trình xử lý khiếu nại, kịch bản trả lời | chỉ nhân viên |

- Tài liệu có: tiêu đề, slug, loại, nội dung Markdown, thẻ, danh mục sản phẩm liên quan (cho `guide`), trạng thái (`draft`, `published`, `archived`), ngày hiệu lực.
- Tài liệu `internal` **không bao giờ** được truy xuất cho chatbot phía khách, chỉ cho `support-desk`.
- FAQ soạn theo cấu trúc hỏi đáp: mỗi cặp là một mục riêng.

### Phiên bản

- Mỗi lần đăng tạo một phiên bản mới, không sửa đè. Chunk và embedding gắn với phiên bản.
- Trích dẫn của chatbot lưu id phiên bản. Mở lại hội thoại cũ vẫn biết lúc đó bot đã dựa vào nội dung nào.
- Ghi audit log khi đăng, gỡ, lưu trữ tài liệu.

### Nhập từ file

- Nhận DOCX và PDF có lớp chữ, tối đa 10 MB.
- DOCX: chuyển sang HTML bằng `mammoth` rồi sang Markdown, giữ heading, danh sách, bảng. (Không dùng DocxLoader của LangChain vì loader này chỉ lấy văn bản thô, mất heading.)
- PDF: trích văn bản theo trang bằng PDF loader của LangChain. PDF không giữ được cấu trúc heading, nên admin thêm heading khi sửa bản nháp. Màn hình nhập nhắc rõ điều này.
- Kết quả **luôn là bản nháp** để admin sửa rồi mới đăng. Không đăng thẳng từ file.
- PDF chỉ có ảnh scan (không có lớp chữ) thì báo lỗi rõ ràng. MVP không làm OCR.
- File gốc không lưu lâu dài. Chỉ giữ tên file và thời điểm nhập.
- Bước chuyển đổi không dùng AI, nên không tốn phí và cho kết quả ổn định.

### Chia đoạn

- Chia theo heading H2 và H3 bằng code tự viết (cần giữ đường dẫn heading và anchor). Đoạn dài thì cắt tiếp bằng `RecursiveCharacterTextSplitter` của LangChain, đếm độ dài theo token, mục tiêu 300 đến 500 token, chồng lấp 50 token.
- Mỗi chunk mang **đường dẫn heading** làm ngữ cảnh, ví dụ "Chính sách bảo hành > Điều kiện bảo hành > Hàng bị rơi vỡ". Đường dẫn này được đưa vào cả văn bản embed lẫn trích dẫn.
- FAQ: mỗi cặp hỏi đáp là 1 chunk.
- Bảng Markdown giữ nguyên trong 1 chunk, không cắt giữa bảng.
- Mỗi chunk có `anchor` trỏ tới heading tương ứng trên trang công khai, để link trích dẫn mở đúng chỗ.

### Index

- Đăng tài liệu thì tạo chunk, gọi `ai-gateway.embed` (feature `knowledge-base.index`), lưu vào `kb_chunks` có chỉ mục HNSW, kèm `search_vector` (unaccent) cho tìm toàn văn.
- Index xong mới chuyển phiên bản mới sang hiệu lực, rồi gỡ chunk của phiên bản cũ. Không có lúc nào tài liệu bị trống.
- Gỡ hoặc lưu trữ tài liệu thì xóa chunk ngay.
- Lỗi embed thì giữ phiên bản cũ đang hiệu lực, báo lỗi trên back-office, cho bấm thử lại.

### Truy xuất

```ts
// apps/api/src/modules/knowledge-base/index.ts
export const knowledge: {
  retrieve(input: {
    query: string;
    audience: 'customer' | 'staff';     // customer: không bao giờ trả internal
    types?: KbDocType[];
    categoryId?: string;                // ưu tiên guide của danh mục này
    k?: number;                         // mặc định 5, tối đa 10
  }): Promise<KbHit[]>;
};

export type KbHit = {
  chunkId: string;
  docId: string;
  docVersionId: string;
  docTitle: string;
  docType: KbDocType;
  headingPath: string;
  text: string;
  url: string;                          // /help/<slug>#<anchor>; tài liệu internal có url back-office
  score: number;
};

export { knowledgePermissions } from './permissions';  // ['knowledge:read', 'knowledge:write']
```

- **Hybrid:** tìm vector (embedding câu hỏi, feature `knowledge-base.query`) và toàn văn chạy song song, gộp bằng RRF.
- **Ngưỡng liên quan:** hit có độ tương đồng cosine dưới ngưỡng (mặc định 0,35, chỉnh bằng bộ đánh giá) bị loại. Không có hit nào qua ngưỡng thì trả mảng rỗng. Chatbot dựa vào đó để nói "không có thông tin" thay vì đoán.
- Embedding lỗi thì chỉ dùng toàn văn.
- Ngày hiệu lực trong tương lai: tài liệu chưa được truy xuất cho tới ngày đó.

### Phân quyền

- `knowledge:write` (mặc định chỉ admin): soạn, nhập, đăng, gỡ.
- `knowledge:read` (admin, `sales`, `support`): xem mọi tài liệu kể cả `internal` và bản nháp, dùng màn hình thử truy xuất.

*(Hai quyền này bổ sung vào danh mục quyền của `identity`.)*

## API

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/help` | công khai | Danh sách tài liệu công khai đã đăng, theo loại |
| GET | `/api/help/:slug` | công khai | Nội dung tài liệu công khai |
| GET | `/api/admin/knowledge/docs[/:id]` | `knowledge:read` | Danh sách, chi tiết, lịch sử phiên bản |
| POST · PATCH | `/api/admin/knowledge/docs[/:id]` | `knowledge:write` | Tạo, sửa bản nháp |
| POST | `/api/admin/knowledge/import` | `knowledge:write` | Tải file DOCX/PDF, trả bản nháp Markdown |
| POST | `/api/admin/knowledge/docs/:id/publish` · `/unpublish` · `/archive` | `knowledge:write` | Đăng, gỡ, lưu trữ |
| POST | `/api/admin/knowledge/retrieve-test` | `knowledge:read` | Thử câu hỏi, xem các chunk truy xuất được và điểm |

## Data Model (Prisma, rút gọn)

```prisma
model KbDocument {
  id              String   @id @default(uuid())
  slug            String   @unique
  type            KbDocType
  title           String
  draftMd         String
  tags            String[]
  categoryIds     String[]
  status          String   // draft | published | archived
  activeVersionId String?
  effectiveFrom   DateTime?
  updatedAt       DateTime @updatedAt
  @@map("kb_documents")
}

model KbDocVersion { /* id, docId, bodyMd, publishedBy, publishedAt */ }

model KbChunk {
  id           String                      @id @default(uuid())
  docVersionId String
  headingPath  String
  anchor       String
  text         String
  tokenCount   Int
  embedding    Unsupported("vector(1536)")
  // search_vector tsvector, chỉ mục HNSW và GIN tạo bằng SQL trong migration
  @@map("kb_chunks")
}
```

## Frontend

- Công khai (`features/knowledge-base/`): trang Trung tâm hỗ trợ `/help` theo loại; trang tài liệu `/help/:slug` có mục lục và anchor cho từng heading; link chân trang tới các chính sách.
- Back-office:
  - Danh sách tài liệu.
  - Trình soạn Markdown có xem trước.
  - Nhập file: xem bản chuyển đổi bên cạnh bản gốc.
  - Lịch sử phiên bản.
  - Màn hình "Thử truy xuất": nhập câu hỏi, xem chunk và điểm.

## Dữ liệu ban đầu

Seed bộ tài liệu mẫu bám theo các spec đã duyệt, admin sửa lại cho đúng thực tế:
- Chính sách: giao hàng (GHN, miễn phí trên ngưỡng), thanh toán (COD, chuyển khoản SePay, hạn 30 phút), bảo hành, đổi trả, bảo mật dữ liệu (đồng ý cá nhân hóa).
- Khoảng 20 FAQ.
- 5 bài hướng dẫn chọn mua cho 5 danh mục seed.

## Testing

- Unit:
  - Chia đoạn: theo heading, giới hạn token, chồng lấp, không cắt bảng, FAQ mỗi cặp 1 chunk, đường dẫn heading và anchor đúng.
  - Chuyển DOCX/PDF mẫu sang Markdown, giữ heading và bảng.
  - Lọc theo `audience`.
- Integration:
  - Đăng phiên bản mới: không có lúc truy xuất trả rỗng (chạy truy xuất song song trong lúc index).
  - Gỡ tài liệu thì truy xuất không còn trả chunk của nó.
  - `audience: 'customer'` không bao giờ trả `internal`, kể cả khi câu hỏi khớp đúng nội dung `internal`.
  - Câu hỏi không liên quan trả mảng rỗng.
  - Embed lỗi thì phiên bản cũ vẫn hiệu lực.
- **Bộ đánh giá truy xuất** (`knowledge-base/eval/questions.json`): 40 câu hỏi về chính sách, FAQ và hướng dẫn, mỗi câu kèm tài liệu và heading đúng, cùng 10 câu không có đáp án trong kho. Chạy `pnpm kb:eval` với embedding thật, in Recall@5 và tỉ lệ trả rỗng đúng.

## Boundaries (riêng module)

- **Always:** lọc theo `audience` ở tầng truy vấn DB, không lọc sau khi đã lấy; index xong mới đổi phiên bản hiệu lực; chạy `pnpm kb:eval` khi đổi cách chia đoạn hoặc ngưỡng.
- **Ask first:** thêm OCR; tự động đăng tài liệu nhập từ file; cho role ngoài admin quyền `knowledge:write`.
- **Never:** trả tài liệu `internal` cho chatbot phía khách; đưa thông tin cá nhân của khách vào kho tri thức; xóa phiên bản cũ đang được trích dẫn trong hội thoại còn lưu.

## Success Criteria

- [ ] Admin soạn, đăng, gỡ tài liệu; chatbot truy xuất được nội dung mới trong vòng 1 phút sau khi đăng.
- [ ] Nhập DOCX và PDF mẫu ra bản nháp Markdown giữ đúng heading; PDF ảnh scan báo lỗi rõ ràng.
- [ ] Bộ đánh giá: Recall@5 ≥ 0,9 trên 40 câu có đáp án; ít nhất 8/10 câu không có đáp án trả mảng rỗng.
- [ ] Không có trường hợp nào tài liệu `internal` lọt vào kết quả `audience: 'customer'` (test tự động).
- [ ] Link trích dẫn mở đúng trang và cuộn tới đúng heading.
- [ ] p95 của `retrieve` dưới 300 ms trên máy dev (đã có cache embedding câu hỏi).
- [ ] Coverage `service.ts` ≥ 80%.

## Open Questions

1. Bạn đã có sẵn văn bản chính sách (bảo hành, đổi trả...) chưa? Nếu có, mình dùng làm dữ liệu seed thay cho bản mẫu.
2. Ngưỡng liên quan 0,35 là giá trị khởi đầu, sẽ chỉnh theo kết quả `pnpm kb:eval`.
3. Có cần OCR cho PDF scan không? Mặc định: không trong MVP.
