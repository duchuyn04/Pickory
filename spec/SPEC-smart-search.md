# Spec: smart-search

Module id: `smart-search` · Phụ thuộc: `catalog`, `ai-gateway`, `activity-tracking` · Quy ước chung: `SPEC.md`

## Objective

Cho khách tìm sản phẩm bằng câu tự nhiên tiếng Việt, kể cả gõ không dấu hoặc sai chính tả nhẹ, thay vì phải nhớ đúng tên sản phẩm. Ví dụ:
- "laptop chơi game dưới 25 triệu"
- "tai nghe chong on di may bay"
- "điện thoại pin trâu chụp ảnh đẹp tầm 10tr"
- "rog g16"

Module này cũng là công cụ tìm sản phẩm cho `shopping-assistant`. Chatbot chỉ gợi ý những gì `smart-search` trả về, nên không bịa sản phẩm.

**User stories**
- Là khách, tôi gõ nhu cầu bằng lời và nhận danh sách sản phẩm đúng ý, kèm các bộ lọc hệ thống hiểu được (ví dụ "Laptop · dưới 25 triệu") mà tôi bỏ được nếu hiểu sai.
- Là khách, tôi gõ không dấu hoặc gõ mã model vẫn ra đúng sản phẩm.
- Là khách, khi gõ, tôi thấy gợi ý tên sản phẩm, danh mục, thương hiệu ngay lập tức.
- Là khách, khi không có kết quả khớp hoàn toàn, tôi vẫn thấy sản phẩm gần nhất kèm thông báo rõ ràng.
- Là admin hoặc `sales` (`reports:read`), tôi xem khách hay tìm gì, câu nào không ra kết quả, để bổ sung hàng hoặc sửa mô tả.

## Thiết kế

### Tổng quan luồng tìm kiếm

```
câu tìm ──→ chuẩn hóa ──┬─→ phân tích bằng luật (giá, hãng, danh mục) ─┐
                        ├─→ phân tích bằng LLM (nếu cần, có cache) ────┤→ bộ lọc
                        ├─→ tìm toàn văn (PostgreSQL, không dấu) ──────┐
                        └─→ tìm vector (embedding câu tìm, pgvector) ──┤→ gộp RRF → áp bộ lọc → xếp hạng → trả kết quả
```

Ba nhánh chạy **song song** để độ trễ không cộng dồn.

### 1. Chuẩn hóa câu tìm

- Bỏ khoảng trắng thừa, chuyển chữ thường, giới hạn 200 ký tự.
- Tạo thêm bản không dấu để tìm toàn văn.
- Thay từ đồng nghĩa và viết tắt theo bảng trong `smart-search/synonyms.ts`, ví dụ `lap` → `laptop`, `đt` → `điện thoại`, `tai phone` → `tai nghe`, `tr`/`củ` → `triệu`.

### 2. Phân tích câu tìm thành bộ lọc

**Bằng luật (luôn chạy, không tốn tiền):**
- Giá: "dưới 20 triệu", "từ 15 đến 20tr", "tầm 10 triệu" (tầm = ±15%), "trên 5 triệu", "500k".
- Thương hiệu: khớp với danh sách thương hiệu trong `catalog`.
- Danh mục: khớp tên và từ khóa của danh mục.

**Bằng LLM (`smart-search.parse-query` qua `ai-gateway.chatJson`):**
- Chỉ gọi khi câu tìm có từ 4 từ trở lên và không phải mã model (tức là câu nhu cầu, không phải tên sản phẩm).
- Prompt gửi kèm danh sách danh mục và các thuộc tính `filterable` của chúng, để LLM chỉ chọn trong những gì có thật.
- Kết quả validate bằng Zod và đối chiếu với `catalog`. Danh mục, thuộc tính hay giá trị không tồn tại thì bỏ.
- Cache theo câu tìm đã chuẩn hóa trong 7 ngày (bảng `search_query_cache`). Xóa cache khi danh mục hoặc bộ thuộc tính đổi.
- Timeout 2,5 giây. Quá hạn, lỗi hoặc hết ngân sách thì dùng kết quả của luật, không báo lỗi cho khách.

```ts
// smart-search/schemas.ts
export const parsedQuerySchema = z.object({
  categorySlug: z.string().optional(),
  brandSlugs: z.array(z.string()).max(5).optional(),
  priceMin: z.number().int().positive().optional(),
  priceMax: z.number().int().positive().optional(),
  attributes: z.array(z.object({
    key: z.string(),                        // phải là thuộc tính filterable của danh mục
    op: z.enum(['eq', 'gte', 'lte']),
    value: z.union([z.string(), z.number(), z.boolean()]),
  })).max(5).optional(),
  keywords: z.string(),                     // phần còn lại dùng cho tìm toàn văn và vector
});
```

**Gộp:** bộ lọc khách chọn trên giao diện > kết quả luật > kết quả LLM. Bộ lọc từ phân tích hiện thành "chip" trên trang kết quả, khách bấm × để bỏ.

### 3. Tìm toàn văn

- Bảng riêng `product_search_docs` của module này có cột `search_vector` (tsvector) gồm tên, thương hiệu, danh mục, mô tả ngắn, giá trị thông số, giá trị tùy chọn, mã SKU. Dùng extension `unaccent` với cấu hình `simple`, vì PostgreSQL không có bộ tách từ tiếng Việt.
- Thêm chỉ mục trigram (`pg_trgm`) trên tên sản phẩm và mã SKU để bắt lỗi gõ và mã model (`g16`, `rtx 4060`).
- Lấy tối đa 50 ứng viên.

### 4. Tìm vector

- Mỗi sản phẩm `active` có 1 embedding (không tính theo SKU), lưu ở `product_embeddings` với chỉ mục HNSW, đo bằng cosine.
- Embedding câu tìm gọi `ai-gateway.embed`. Câu đã từng tìm thì đã có trong cache nên không tốn tiền.
- Lấy tối đa 50 ứng viên.
- Embedding lỗi thì bỏ nhánh này, chỉ dùng toàn văn.

### 5. Gộp và xếp hạng

- Gộp hai danh sách bằng **Reciprocal Rank Fusion**: điểm = Σ 1/(60 + hạng) trên mỗi nhánh.
- Áp bộ lọc cứng (danh mục, hãng, giá, thuộc tính, còn hàng nếu khách chọn).
- Điều chỉnh điểm:
  - Còn hàng: ×1,1.
  - Ngừng kinh doanh: loại khỏi kết quả.
  - **Cá nhân hóa** (chỉ khi khách đã đồng ý): sản phẩm thuộc danh mục hoặc thương hiệu trong hồ sơ sở thích được cộng tối đa 10% điểm. Mức này đủ để nghiêng thứ tự, nhưng không đẩy sản phẩm kém liên quan lên đầu.
- Phân trang 24 sản phẩm mỗi trang. Kết quả của câu tìm được giữ 5 phút theo `(câu tìm, bộ lọc, actorKey)` để chuyển trang không phải tính lại.

### 6. Không có kết quả

Bộ lọc làm kết quả rỗng thì nới lỏng lần lượt: bỏ bộ lọc thuộc tính → bỏ bộ lọc giá. Kết quả sau khi nới hiện kèm thông báo, ví dụ "Không có laptop gaming dưới 15 triệu. Đây là các lựa chọn gần nhất". Ghi lại là câu tìm "không có kết quả khớp".

### Gợi ý khi gõ

- `GET /api/search/suggest?q=`: tối đa 5 sản phẩm, 3 danh mục, 3 thương hiệu, dựa trên trigram và tiền tố. **Không gọi AI.**
- Frontend gọi sau khi khách ngừng gõ 150 ms, hủy request cũ khi có ký tự mới.

### Index embedding

- Văn bản để embed, dựng từ dữ liệu `catalog`: tên, thương hiệu, đường dẫn danh mục, mô tả ngắn, thông số dạng "Nhãn: giá trị đơn vị", các tùy chọn, khoảng giá, mô tả chi tiết (đã bỏ Markdown, cắt còn tối đa 2.000 ký tự).
- Lưu `contentHash` của văn bản. Chỉ gọi embed lại khi hash đổi (đổi giá hay tồn kho không làm đổi hash, vì giá chỉ đưa vào dưới dạng khoảng làm tròn).
- Nghe `onProductChanged`, đưa vào hàng đợi trong DB (`search_index_jobs`), job chạy mỗi 30 giây, gộp nhiều thay đổi của cùng sản phẩm thành 1 lần embed.
- Sản phẩm chuyển `hidden` hoặc `discontinued` thì xóa embedding.
- Lệnh `pnpm search:reindex` index lại toàn bộ (dùng sau khi đổi model embedding).
- Ghi nhận: index lại 500 sản phẩm với `text-embedding-3-small` tốn chi phí rất nhỏ, nhưng vẫn đi qua ngân sách của `ai-gateway`.

### Interface public

```ts
// apps/backend/src/modules/smart-search/index.ts
export const productSearch: {
  search(input: {
    query: string;
    filters?: SearchFilters;          // cùng định dạng bộ lọc của catalog
    page?: number;
    pageSize?: number;
    actor: Actor;
    source: 'search-page' | 'shopping-assistant';
  }): Promise<{
    items: ProductSummary[];
    appliedFilters: AppliedFilter[];  // kèm nguồn: 'user' | 'rule' | 'llm'
    relaxed: boolean;                 // đã phải nới bộ lọc
    total: number;
  }>;
  /** Sản phẩm gần nhất theo embedding với 1 hoặc nhiều sản phẩm gốc. Không gọi AI. Cho recommendations. */
  similarTo(productIds: string[], k: number, filters?: SearchFilters): Promise<{ productId: string; similarity: number }[]>;
};
```

- Mỗi lần tìm gọi `trackServerEvent('search', ...)` với câu tìm đã qua `redactPII`, số kết quả, bộ lọc đã hiểu, `relaxed`, độ trễ.
- `shopping-assistant` gọi `search` với `source: 'shopping-assistant'`, thường đã tự trích bộ lọc nên truyền `filters` sẵn.

## API

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/search?q=&page=&...bộ lọc` | công khai | Tìm kiếm, giới hạn 30 lần/phút mỗi khách |
| GET | `/api/search/suggest?q=` | công khai | Gợi ý khi gõ, giới hạn 120 lần/phút mỗi khách |
| GET | `/api/admin/search/insights` | `reports:read` | Câu tìm nhiều nhất, câu không có kết quả, tỉ lệ click từ kết quả tìm |
| POST | `/api/admin/search/reindex` | `products:write` | Index lại toàn bộ (chạy nền) |

## Data Model (Prisma, rút gọn)

```prisma
model ProductEmbedding {
  productId   String                     @id
  model       String
  contentHash String
  embedding   Unsupported("vector(1536)")
  updatedAt   DateTime                   @updatedAt
  @@map("product_embeddings")
}
// Chỉ mục HNSW (vector_cosine_ops) tạo bằng SQL trong migration.

model ProductSearchDoc { /* productId @id, searchVector tsvector, nameUnaccent, skuCodes, categoryId, brandId, minPrice, inStock, status */ }
model SearchQueryCache { /* normalizedQuery @unique, parsed Json, createdAt, expiresAt */ }
model SearchIndexJob   { /* productId @unique, enqueuedAt, attempts, lastError */ }
```

- `product_search_docs` là bảng riêng của `smart-search`, không thêm cột vào bảng `products` của `catalog`, đúng quy tắc mỗi module chỉ ghi bảng của mình. Bảng được cập nhật cùng job index. Giá và tồn kho trong bảng cập nhật thêm khi nghe `onStockChanged`.

## Frontend

- Ô tìm kiếm trên header có gợi ý khi gõ (điều hướng được bằng bàn phím, có ARIA combobox).
- Trang kết quả `/search?q=`: chip bộ lọc đã hiểu (bỏ được), bộ lọc bên cạnh như trang danh mục, thông báo khi đã nới bộ lọc.
- Mỗi lần khách click kết quả, ghi `rec_click` với `source: 'smart-search'` để đo tỉ lệ click.

## Testing

- Unit: chuẩn hóa và đồng nghĩa; parser giá (bộ ví dụ: "dưới 20 triệu", "15-20tr", "tầm 10 củ", "500k", "trên 5tr"); quyết định có gọi LLM hay không; RRF; điểm cá nhân hóa không vượt 10%; thứ tự nới bộ lọc.
- Integration với PostgreSQL thật:
  - Tìm không dấu ra sản phẩm có dấu.
  - Gõ mã model ra đúng sản phẩm.
  - LLM trả danh mục không tồn tại thì bị bỏ.
  - LLM timeout thì vẫn trả kết quả.
  - `onProductChanged` dẫn đến embed lại chỉ khi `contentHash` đổi.
  - Sản phẩm `hidden` không xuất hiện trong kết quả.
- AI dùng fake provider. Riêng embedding, fake provider sinh vector cố định từ hash của văn bản, để test chạy ổn định.
- **Bộ đánh giá chất lượng** (`smart-search/eval/queries.json`): 50 câu tìm tiếng Việt thực tế, mỗi câu kèm danh sách sản phẩm đúng trong dữ liệu seed. Chạy bằng `pnpm search:eval`, dùng AI thật (`AI_LIVE_TESTS=1`), in ra Recall@10 và MRR cho 2 chế độ: chỉ toàn văn, và đầy đủ.
- E2E: gõ "laptop choi game duoi 25 trieu" → thấy chip "Laptop gaming" và "dưới 25 triệu" → kết quả đều thỏa → bỏ chip giá → kết quả mở rộng.

## Boundaries (riêng module)

- **Always:** giữ được kết quả khi AI lỗi (toàn văn luôn chạy); đối chiếu kết quả LLM với dữ liệu thật trước khi dùng; chạy `pnpm search:eval` khi đổi prompt, trọng số hay model.
- **Ask first:** đổi model embedding (phải index lại toàn bộ); tăng mức cộng điểm cá nhân hóa trên 10%; gọi LLM cho mọi câu tìm.
- **Never:** trả về sản phẩm không có trong `catalog` hoặc không ở trạng thái `active`; đưa thông tin người dùng vào prompt phân tích câu tìm; để câu tìm chưa qua `redactPII` vào log.

## Success Criteria

- [ ] Bộ đánh giá 50 câu: chế độ đầy đủ đạt Recall@10 ≥ 0,8 và cao hơn chế độ chỉ toàn văn ít nhất 0,15.
- [ ] "laptop choi game duoi 25 trieu" chỉ trả laptop gaming giá ≤ 25.000.000 đ trong dữ liệu seed.
- [ ] Gõ mã model hoặc gõ không dấu tên sản phẩm có trong seed thì sản phẩm đó nằm trong top 3.
- [ ] Tắt API AI: tìm kiếm vẫn trả kết quả từ toàn văn và luật, không báo lỗi.
- [ ] Độ trễ p95 trên máy dev: dưới 400 ms khi câu tìm đã có cache phân tích; dưới 3 giây khi phải gọi LLM.
- [ ] Gợi ý khi gõ p95 dưới 100 ms và không phát sinh lời gọi AI.
- [ ] Sửa tên sản phẩm thì kết quả tìm phản ánh thay đổi trong vòng 1 phút; đổi giá trong cùng khoảng làm tròn thì không phát sinh lời gọi embed.
- [ ] Trang insights hiện đúng top câu tìm và câu không có kết quả từ dữ liệu test.
- [ ] Coverage `service.ts` ≥ 80%.

## Open Questions

1. Bộ 50 câu đánh giá: mình sẽ soạn dựa trên dữ liệu seed. Nếu bạn có các câu khách thật hay hỏi (từ fanpage, Zalo), gửi giúp để bộ đánh giá sát thực tế hơn.
2. Có muốn cá nhân hóa thứ tự kết quả tìm kiếm không, hay chỉ cá nhân hóa ở gợi ý? Mặc định: có, nhẹ (tối đa 10%).
3. Có cần tìm kiếm bằng giọng nói hoặc hình ảnh không? Mặc định: không trong MVP.
