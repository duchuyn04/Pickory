# Task List: smart-search

> **Trạng thái: đã bỏ ngày 2026-10-14.** Người dùng thu hẹp phần AI cá nhân hóa về một tính năng: chatbot tư vấn `shopping-assistant`.
>
> Tìm theo từ khóa cơ bản chuyển sang `catalog` (`GET /api/products?q=`). Hệ thống không còn tìm sản phẩm theo ý nghĩa.
>
> File này chỉ giữ để tra cứu lịch sử. Không làm task nào trong file này. Không tick mục nào trong file này.

Plan và quyết định thiết kế: `tasks/smart-search/plan.md`. Spec: `spec/SPEC-smart-search.md`. Quy ước chung: `spec/SPEC.md`, `tasks/dotnet-migration/plan.md`, `CLAUDE.md`.

Quy ước:
- Mỗi task backend phải qua `dotnet build` và `dotnet test` trong `apps/backend` trước khi báo xong.
- Mỗi task frontend phải qua `pnpm lint && pnpm typecheck && pnpm test` trong `apps/frontend`.
- Task back-office thêm endpoint mới vào `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`.
- Mỗi test mới đi qua ít nhất một phép thử đột biến trên luật nó bảo vệ.
- Test tự động không gọi API AI thật. Test dùng fake provider của `ai-gateway`.
- Unit test đặt ở `apps/backend/tests/Pickory.UnitTests/Modules/SmartSearch/`. Test tích hợp đặt ở `apps/backend/tests/Pickory.IntegrationTests/SmartSearch/`.
- Kích thước: XS 1 file, S 1-2, M 3-5. Số file không tính file test.

---

## Phase 1: Logic thuần và nền tảng

### SRCH-01: Chuẩn hóa câu tìm
**Mô tả:** Hàm thuần chuẩn hóa câu tìm theo mục 1 của spec. Hàm trả bản có dấu, bản không dấu và cờ "là mã model". Bảng đồng nghĩa thay `smart-search/synonyms.ts` (quyết định 3).
**Tiêu chí nghiệm thu:**
- [ ] Bỏ khoảng trắng thừa, chuyển chữ thường, cắt còn 200 ký tự
- [ ] Bản không dấu đổi `đ` thành `d`, ví dụ "điện thoại" thành "dien thoai"
- [ ] `lap` thành `laptop`, `đt` thành `điện thoại`, `tai phone` thành `tai nghe`, `tr` và `củ` thành `triệu`
- [ ] Từ đồng nghĩa chỉ thay khi khớp nguyên từ, ví dụ `trắng` không đổi thành `triệuắng`
- [ ] `g16`, `rog g16`, `rtx 4060` được nhận là mã model. "laptop chơi game" không phải mã model
**Kiểm tra:** unit test
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.SmartSearch/Normalization/{QueryNormalizer.cs, SynonymTable.cs, ModelCodeDetector.cs}`

### SRCH-02: Parser giá và luật gọi LLM
**Mô tả:** Hàm thuần đọc khoảng giá trong câu tìm và trả phần còn lại làm từ khóa. Hàm thứ hai quyết định có gọi LLM hay không.
**Tiêu chí nghiệm thu:**
- [ ] "dưới 20 triệu" cho `PriceMax = 20000000`
- [ ] "15-20tr" và "từ 15 đến 20tr" cho khoảng 15.000.000 đến 20.000.000 đ
- [ ] "tầm 10 củ" cho khoảng 8.500.000 đến 11.500.000 đ (±15%)
- [ ] "trên 5tr" cho `PriceMin = 5000000`. "dưới 500k" cho `PriceMax = 500000`
- [ ] Câu không có giá trả `null` cho cả hai biên, không ném lỗi
- [ ] Phần giá bị bỏ khỏi từ khóa còn lại
- [ ] Gọi LLM chỉ khi câu có từ 4 từ trở lên và không phải mã model
**Kiểm tra:** unit test theo bộ ví dụ của spec
**Phụ thuộc:** SRCH-01 · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.SmartSearch/Parsing/{PriceRuleParser.cs, LlmParsePolicy.cs}`

### SRCH-03: Gộp, xếp hạng và nới bộ lọc
**Mô tả:** Hàm thuần cho quyết định 14, 15 và thứ tự ưu tiên bộ lọc của spec.
**Tiêu chí nghiệm thu:**
- [ ] RRF tính điểm bằng tổng `1/(60 + hạng)` trên mỗi nhánh. Sản phẩm có ở hai nhánh xếp trên sản phẩm chỉ có ở một nhánh cùng hạng
- [ ] Sản phẩm còn hàng nhân điểm 1,1. Sản phẩm `discontinued` bị loại
- [ ] Mức cộng cá nhân hóa không vượt 10% điểm gốc với mọi hồ sơ, kể cả hồ sơ có điểm rất lớn
- [ ] Không có hồ sơ thì thứ tự giữ nguyên
- [ ] Thứ tự nới: bỏ bộ lọc thuộc tính trước, rồi bỏ bộ lọc giá
- [ ] Bộ lọc cùng loại từ nhiều nguồn: khách chọn thắng luật, luật thắng LLM
**Kiểm tra:** unit test
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.SmartSearch/Ranking/{ReciprocalRankFusion.cs, ScoreAdjuster.cs, FilterRelaxation.cs, FilterPrecedence.cs}`

### SRCH-04: Khung module, Contracts, schema và migration
**Mô tả:** Tạo `Pickory.Modules.SmartSearch` và `Pickory.Modules.SmartSearch.Contracts`. Thêm `SmartSearchDbContext`, migration đầu tiên và `SmartSearchModule.cs`. Contracts có `IProductSearch` và các kiểu của quyết định 2.
**Tiêu chí nghiệm thu:**
- [ ] Bảng `product_embeddings`, `product_search_docs`, `search_query_cache`, `search_index_jobs` theo Data Model của spec và quyết định 7
- [ ] Migration bật `unaccent` và `pg_trgm`, tạo hàm `smart_search_unaccent` có nhãn `IMMUTABLE`
- [ ] Migration tạo chỉ mục HNSW `vector_cosine_ops`, chỉ mục GIN cho `search_vector`, chỉ mục trigram cho `name_unaccent` và `sku_codes`
- [ ] `search_index_jobs.product_id` có ràng buộc unique
- [ ] Migration chạy trên DB sạch. Lệnh `e2e:start` và fixture test tích hợp dựng được schema mới
- [ ] `Pickory.ArchitectureTests` chặn `SmartSearch` tham chiếu module khác ngoài project Contracts
- [ ] Test kiến trúc cho phép `SmartSearch.Contracts` dùng kiểu của `Catalog.Contracts` và `Identity.Contracts`. Luật hiện tại chặn thì dừng và hỏi người dùng
- [ ] `Program.cs` chỉ gọi `AddSmartSearchModule` và `MapSmartSearchEndpoints`
**Kiểm tra:** build, test kiến trúc, test tích hợp schema
**Phụ thuộc:** không · **Quy mô:** M

### SRCH-05: Fake embedding theo hash văn bản
**Mô tả:** Sửa `FakeEmbeddingProvider` của `ai-gateway` theo quyết định 23. Vector sinh từ SHA-256 của text.
**Trạng thái:** chờ người dùng duyệt (câu hỏi mở 5 của plan).
**Tiêu chí nghiệm thu:**
- [ ] Cùng text luôn cho cùng vector 1536 chiều
- [ ] Hai text khác nhau cùng độ dài cho hai vector khác nhau
- [ ] Test hiện có của `ai-gateway` vẫn xanh
**Kiểm tra:** unit test của `ai-gateway`
**Phụ thuộc:** không · **Quy mô:** XS
**File:** `apps/backend/src/Pickory.Modules.AiGateway/Providers/Fake/FakeEmbeddingProvider.cs`

### Checkpoint SR1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `SmartSearch` tham chiếu module khác ngoài Contracts
- [ ] Review với người dùng

---

## Phase 2: Index

### SRCH-06: Văn bản embed và contentHash
**Mô tả:** Hàm thuần dựng văn bản embed từ `ProductDetail` theo mục "Index embedding" của spec, rồi tính `contentHash` (quyết định 10).
**Tiêu chí nghiệm thu:**
- [ ] Văn bản có tên, thương hiệu, đường dẫn danh mục dạng "Cha > Con", mô tả ngắn, tùy chọn
- [ ] Thông số viết dạng "Nhãn: giá trị đơn vị". Thông số có `Value` là `null` bị bỏ
- [ ] Mô tả chi tiết là text thuần, cắt còn tối đa 2.000 ký tự
- [ ] Giá chỉ có dạng khoảng làm tròn xuống bội số 1.000.000 đ
- [ ] Đổi giá trong cùng khoảng làm tròn giữ nguyên hash. Đổi tên làm đổi hash
- [ ] Đổi tồn kho giữ nguyên hash
**Kiểm tra:** unit test
**Phụ thuộc:** SRCH-04, câu hỏi mở 2 của plan · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.SmartSearch/Indexing/{EmbeddingTextBuilder.cs, ContentHash.cs}`

### SRCH-07: Hàng đợi index, job 30 giây và index lại toàn bộ
**Mô tả:** Trình nghe `ProductChangedEvent` ghi `search_index_jobs`. `SearchIndexWorker` chạy `SearchIndexProcessor` mỗi 30 giây (quyết định 9). Lệnh `search:reindex` và route `POST /api/admin/search/reindex-jobs` (quyền `products:write`) đưa mọi sản phẩm vào hàng đợi.
**Tiêu chí nghiệm thu:**
- [ ] 3 sự kiện của cùng sản phẩm trước một lượt job tạo đúng 1 dòng job và 1 lần gọi embed
- [ ] Processor ghi `product_search_docs` và `product_embeddings` cho sản phẩm khách thấy được
- [ ] Hash không đổi thì không gọi `EmbedAsync`, chỉ cập nhật `product_search_docs`
- [ ] Sản phẩm `hidden`, `discontinued` hoặc đã xóa bị xóa khỏi hai bảng
- [ ] Embed lỗi: giữ dòng `product_search_docs`, tăng `attempts`, ghi `lastError`, thử lại theo backoff, dừng sau 8 lần
- [ ] Chu kỳ job là 30 giây
- [ ] `search:reindex` duyệt hết `ListForIndexingAsync` theo cursor và đưa 30 sản phẩm seed vào hàng đợi
- [ ] Route index lại trả 202. `sales` và `support` nhận 403. Endpoint có trong ma trận quyền
- [ ] Mọi lời gọi embed dùng `AiFeature.SmartSearchIndex`
**Kiểm tra:** test tích hợp gọi processor trực tiếp với fake provider đếm request
**Phụ thuộc:** SRCH-05, SRCH-06, câu hỏi mở 7 của plan · **Quy mô:** M

### SRCH-08: Đồng bộ giá và tồn kho
**Mô tả:** Trình nghe `StockChangedEvent` cập nhật `min_price` và `in_stock` của `product_search_docs` (quyết định 12).
**Tiêu chí nghiệm thu:**
- [ ] SKU hết hàng làm `in_stock` của sản phẩm thành `false` khi mọi SKU bật đều hết hàng
- [ ] Đổi tồn kho không gọi `EmbedAsync`
- [ ] Sản phẩm chưa có dòng trong `product_search_docs` thì không làm gì
**Kiểm tra:** test tích hợp
**Phụ thuộc:** SRCH-07 · **Quy mô:** S

### Checkpoint SR2
- [ ] Lệnh `search:reindex` index đủ 30 sản phẩm seed
- [ ] Sửa tên sản phẩm thì `product_search_docs` đổi sau một lượt job
- [ ] Review với người dùng

---

## Phase 3: Tìm kiếm

### SRCH-09: Tìm toàn văn và trigram
**Mô tả:** Truy vấn PostgreSQL trả tối đa 50 ứng viên kèm hạng, theo mục 3 của spec.
**Tiêu chí nghiệm thu:**
- [ ] "tai nghe chong on" tìm ra sản phẩm có chữ "chống ồn"
- [ ] Mã model có trong seed nằm trong top 3 khi gõ đúng mã
- [ ] Gõ sai một ký tự trong mã model vẫn ra sản phẩm nhờ trigram
- [ ] Không bao giờ trả sản phẩm ngoài `product_search_docs`
- [ ] Trả tối đa 50 ứng viên
**Kiểm tra:** test tích hợp với PostgreSQL thật trên dữ liệu seed
**Phụ thuộc:** SRCH-07 · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.SmartSearch/Retrieval/FullTextRetriever.cs`

### SRCH-10: Phân tích bằng luật với dữ liệu catalog
**Mô tả:** Khớp thương hiệu và danh mục trong câu tìm với dữ liệu thật của `catalog`, cộng kết quả của parser giá. Kết quả là danh sách `AppliedFilter` có `Origin = Rule`.
**Tiêu chí nghiệm thu:**
- [ ] Tên thương hiệu có hoặc không dấu khớp thương hiệu trong `catalog`
- [ ] Tên danh mục và từ khóa trong `CategoryKeywordTable` khớp đúng danh mục
- [ ] "laptop choi game duoi 25 trieu" cho chip danh mục "Laptop gaming" và chip "dưới 25 triệu"
- [ ] Không gọi AI
**Kiểm tra:** unit test cho bảng từ khóa; test tích hợp với seed
**Phụ thuộc:** SRCH-02, SRCH-09, câu hỏi mở 1, 3 và 4 của plan · **Quy mô:** S

### SRCH-11: Phân tích bằng LLM và cache 7 ngày
**Mô tả:** Gọi `IAiGateway.ChatJsonAsync<ParsedQuery>` theo quyết định 5. Lưu kết quả đã đối chiếu vào `search_query_cache`.
**Tiêu chí nghiệm thu:**
- [ ] Prompt gửi danh sách danh mục và thuộc tính `filterable` của chúng. Prompt không có `Actor`, `anonymousId` hay câu tìm chưa qua `PiiRedactor`
- [ ] `ParsedQueryValidator` từ chối giá âm, quá 5 thương hiệu, quá 5 thuộc tính, `op` ngoài `eq`, `gte`, `lte`
- [ ] Danh mục, thuộc tính hoặc giá trị không có trong `catalog` bị bỏ, phần hợp lệ vẫn dùng
- [ ] Câu đã có trong cache không gọi AI. Cache quá 7 ngày bị bỏ qua
- [ ] Quá 2,5 giây, `AiUnavailableException`, `AiBudgetExceededException`, `AiInvalidOutputException`: trả kết quả luật, không ném lỗi
- [ ] Mọi lời gọi dùng `AiFeature.SmartSearchParseQuery`
**Kiểm tra:** test tích hợp với fake provider có kịch bản lỗi và chậm
**Phụ thuộc:** SRCH-10 · **Quy mô:** M

### SRCH-12: `IProductSearch.SearchAsync`
**Mô tả:** Ghép các phần trên theo quyết định 13 đến 17.
**Tiêu chí nghiệm thu:**
- [ ] Ba nhánh LLM, toàn văn và vector chạy song song. Test với nhánh chậm 1 giây cho tổng thời gian dưới 1,5 giây
- [ ] Embed câu tìm lỗi thì vẫn trả kết quả từ toàn văn
- [ ] Bộ lọc cứng áp đúng danh mục (gồm danh mục con), thương hiệu, giá, thuộc tính, còn hàng
- [ ] Chỉ trả sản phẩm `active`
- [ ] Khách chưa đồng ý cá nhân hóa không gọi `IProfileReader`
- [ ] Kết quả rỗng thì nới bộ lọc theo thứ tự, `Relaxed = true`, và sự kiện ghi là "không có kết quả khớp"
- [ ] Mỗi trang 24 sản phẩm. Gọi trang 2 trong 5 phút không chạy lại truy vấn
- [ ] Mỗi lần tìm gọi `IServerEventTracker` một lần với câu đã qua `PiiRedactor`
- [ ] Câu tìm có SĐT và email: log của module không chứa SĐT và email đó
**Kiểm tra:** test tích hợp; test kiểm log
**Phụ thuộc:** SRCH-11, `IServerEventTracker` và `IProfileReader` của `activity-tracking` · **Quy mô:** M

### SRCH-13: `GET /api/search`
**Mô tả:** Route công khai cho trang kết quả. Query string dùng cùng tham số bộ lọc với `GET /api/products`, thêm `q`, `page` và `ignore` (quyết định 24).
**Tiêu chí nghiệm thu:**
- [ ] Thiếu `q` hoặc `q` rỗng trả 400 `VALIDATION_ERROR`
- [ ] Tham số bộ lọc sai trả 400 với cùng mã lỗi như `GET /api/products`
- [ ] `ignore=price` bỏ bộ lọc giá do luật hoặc LLM tạo
- [ ] Response `{ data }` có `items`, `appliedFilters` kèm nguồn, `relaxed`, `total`, `page`
- [ ] Lần gọi thứ 31 trong một phút của cùng `anonymousId` trả 429 `TOO_MANY_REQUESTS`
- [ ] Tắt API AI (fake provider báo lỗi) vẫn trả 200 có kết quả
**Kiểm tra:** test tích hợp hợp đồng HTTP
**Phụ thuộc:** SRCH-12, câu hỏi mở 8 của plan · **Quy mô:** S

### SRCH-14: `IProductSearch.SimilarToAsync`
**Mô tả:** Trả sản phẩm gần nhất theo embedding với một hoặc nhiều sản phẩm gốc, cho `recommendations`.
**Tiêu chí nghiệm thu:**
- [ ] Không gọi AI. Fake provider không nhận request
- [ ] Kết quả không chứa sản phẩm gốc
- [ ] Áp được `ProductFilters`, ví dụ cùng danh mục và khoảng giá
- [ ] Trả tối đa `k` sản phẩm, sắp theo `Similarity` giảm dần
- [ ] Sản phẩm gốc chưa có embedding trả danh sách rỗng
**Kiểm tra:** test tích hợp
**Phụ thuộc:** SRCH-12 · **Quy mô:** S

### SRCH-15: Gợi ý khi gõ
**Mô tả:** `GET /api/search/suggestions?q=` theo quyết định 18 và 20.
**Tiêu chí nghiệm thu:**
- [ ] Trả tối đa 5 sản phẩm, 3 danh mục, 3 thương hiệu
- [ ] Gõ "rog" hoặc "ro" thấy sản phẩm ROG trong seed
- [ ] Không gọi AI. Fake provider không nhận request
- [ ] `q` rỗng trả ba danh sách rỗng, không lỗi
- [ ] Lần gọi thứ 121 trong một phút trả 429 `TOO_MANY_REQUESTS`
**Kiểm tra:** test tích hợp
**Phụ thuộc:** SRCH-09, câu hỏi mở 3 và 7 của plan · **Quy mô:** S

### SRCH-UI1: Ô tìm trên header
**Mô tả:** Thêm ô tìm vào `apps/frontend/src/shared/header.tsx`, có gợi ý khi gõ. Component đặt trong `apps/frontend/src/features/smart-search/`.
**Tiêu chí nghiệm thu:**
- [ ] Gọi gợi ý sau khi khách ngừng gõ 150 ms. Ký tự mới hủy request cũ bằng `AbortController`
- [ ] Theo mẫu ARIA combobox: `role="combobox"`, `aria-expanded`, `aria-controls`, `aria-activedescendant`
- [ ] Mũi tên lên, xuống chọn gợi ý. Enter mở gợi ý đang chọn. Escape đóng danh sách
- [ ] Enter khi chưa chọn gợi ý mở `/search?q=`
- [ ] Có `label` cho ô tìm. Lỗi mạng ẩn danh sách, không hiện lỗi cho khách
- [ ] Theo `docs/design/design-system.md`. Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright: console 0 lỗi và 0 cảnh báo khi gõ và chọn gợi ý
**Kiểm tra:** Vitest; ảnh chụp; Playwright
**Phụ thuộc:** SRCH-15 · **Quy mô:** M

### SRCH-UI2: Trang kết quả `/search`
**Mô tả:** Trang `/search?q=` có chip bộ lọc đã hiểu, bộ lọc bên cạnh như trang danh mục, thông báo khi đã nới bộ lọc.
**Tiêu chí nghiệm thu:**
- [ ] Mỗi chip có nút × có `aria-label`. Bấm × gửi lại câu tìm kèm `ignore` và kết quả đổi
- [ ] Bộ lọc bên cạnh dùng lại component của trang danh mục (`apps/frontend/src/features/catalog/customer/`)
- [ ] `relaxed = true` thì hiện thông báo, ví dụ "Không có laptop gaming dưới 15 triệu. Đây là các lựa chọn gần nhất"
- [ ] Click kết quả ghi `rec_click` với `source: 'smart-search'` qua `track` của `activity-tracking`
- [ ] URL giữ câu tìm, bộ lọc và trang, nên chia sẻ link được
- [ ] Không có kết quả nào hiện trạng thái rỗng có hướng dẫn
- [ ] Theo `docs/design/design-system.md`. Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright: console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; ảnh chụp; Playwright
**Phụ thuộc:** SRCH-13 · **Quy mô:** M

### Checkpoint SR3
- [ ] Khách tìm bằng câu không dấu và mã model trên dữ liệu seed, thấy chip bộ lọc
- [ ] Tắt API AI, tìm kiếm vẫn trả kết quả
- [ ] Review với người dùng

---

## Phase 4: Insights, đánh giá và nghiệm thu

### SRCH-16: Insights cho back-office
**Mô tả:** `GET /api/admin/search/insights?from=&to=` với quyền `reports:read`. Route trả top câu tìm, câu không có kết quả, tỉ lệ click từ kết quả tìm và tỉ lệ câu tìm phải gọi LLM. Nguồn dữ liệu theo câu hỏi mở 6 của plan. Tỉ lệ click đọc từ `IActivityMetrics` với `source` là `smart-search`.
**Tiêu chí nghiệm thu:**
- [ ] Bộ dữ liệu dựng sẵn cho đúng top 10 câu tìm và top 10 câu không có kết quả
- [ ] Câu tìm trong response đã qua `PiiRedactor`
- [ ] Ngày tính theo `Asia/Ho_Chi_Minh`
- [ ] `support` nhận 403. Endpoint có trong ma trận quyền
**Kiểm tra:** test tích hợp
**Phụ thuộc:** SRCH-12, câu hỏi mở 6 của plan · **Quy mô:** M

### SRCH-UI3: Màn insights tìm kiếm
**Mô tả:** Màn `/admin/search/insights` trong back-office. Menu chỉ hiện với người có `reports:read`.
**Tiêu chí nghiệm thu:**
- [ ] Bảng top câu tìm và bảng câu không có kết quả, có bộ chọn khoảng ngày
- [ ] Người thiếu quyền không thấy menu. Mở thẳng URL thấy trang không đủ quyền
- [ ] Theo `docs/design/design-system.md`. Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright: console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; E2E `--headed`
**Phụ thuộc:** SRCH-16 · **Quy mô:** M

### SRCH-17: Bộ đánh giá 50 câu và lệnh `search:eval`
**Mô tả:** Soạn `Evaluation/queries.json` gồm 50 câu tìm tiếng Việt trên dữ liệu seed, mỗi câu kèm danh sách sản phẩm đúng. Lệnh `search:eval` in Recall@10 và MRR cho hai chế độ: chỉ toàn văn, và đầy đủ.
**Tiêu chí nghiệm thu:**
- [ ] File có đúng 50 câu. Mỗi câu có ít nhất 1 sản phẩm đúng có trong seed
- [ ] Phép tính Recall@10 và MRR đúng với bộ ví dụ tính tay
- [ ] Thiếu `AI_LIVE_TESTS=1` hoặc thiếu API key thì lệnh dừng với thông báo rõ, không chạy với fake provider
- [ ] Output in hai dòng số liệu, một dòng cho mỗi chế độ
**Kiểm tra:** unit test phép tính; test tích hợp phần đọc file
**Phụ thuộc:** SRCH-12 · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.SmartSearch/Evaluation/{queries.json, SearchEvaluation.cs, SearchCommandLine.cs}`

### SRCH-18: Lệnh `search:benchmark`
**Mô tả:** Đo p95 của `GET /api/search` và `GET /api/search/suggestions` qua HTTP thật trong cùng tiến trình, theo mẫu `catalog:benchmark`.
**Tiêu chí nghiệm thu:**
- [ ] Lệnh chỉ chạy khi tên DB chứa "bench"
- [ ] Đo riêng câu đã có cache phân tích và gợi ý khi gõ
- [ ] In p95 của từng loại
- [ ] Không có API key thì bỏ phần đo câu phải gọi LLM và in lý do
**Kiểm tra:** chạy lệnh, lưu output vào báo cáo
**Phụ thuộc:** SRCH-13, SRCH-15 · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Api/Benchmark/SearchBenchmark.cs`

### SRCH-E2E1: Luồng E2E tìm kiếm
**Mô tả:** E2E theo luồng của spec, gồm 4 bước.
1. Gõ "laptop choi game duoi 25 trieu".
2. Kiểm chip "Laptop gaming" và chip "dưới 25 triệu".
3. Kiểm mọi kết quả thỏa hai chip.
4. Bỏ chip giá, kiểm kết quả mở rộng.
**Tiêu chí nghiệm thu:**
- [ ] Mọi kết quả trước khi bỏ chip là laptop gaming giá không quá 25.000.000 đ
- [ ] Sau khi bỏ chip giá, số kết quả tăng
- [ ] Suite chạy không có API key AI và vẫn pass
- [ ] Console 0 lỗi và 0 cảnh báo
- [ ] Pass ở chế độ có giao diện và chế độ ẩn
**Kiểm tra:** `pnpm test:e2e`
**Phụ thuộc:** SRCH-UI1, SRCH-UI2, câu hỏi mở 1 của plan · **Quy mô:** M
**File:** `apps/frontend/e2e/smart-search-query.spec.ts`

### SRCH-19: Nghiệm thu smart-search
**Mô tả:** Kiểm toàn module trước khi sang `recommendations`.
**Tiêu chí nghiệm thu:**
- [ ] Ma trận quyền có mọi endpoint back-office của `smart-search`
- [ ] Coverage dòng của `Pickory.Modules.SmartSearch` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
- [ ] `search:benchmark`: câu đã có cache phân tích p95 dưới 400 ms, gợi ý khi gõ p95 dưới 100 ms
- [ ] Khi có API key: `search:eval` cho chế độ đầy đủ Recall@10 ít nhất 0,8 và cao hơn chế độ chỉ toàn văn ít nhất 0,15
- [ ] Khi có API key: câu phải gọi LLM có p95 dưới 3 giây
- [ ] Không có API key: ghi rõ hai tiêu chí trên còn mở và lý do
**Kiểm tra:** `dotnet test`, script gộp coverage, output `search:benchmark` và `search:eval`
**Phụ thuộc:** SRCH-16, SRCH-17, SRCH-18, SRCH-E2E1 · **Quy mô:** S

## Checkpoint cuối
- [ ] Mọi dòng Traceability đạt, trừ dòng ghi "khi có API key"
- [ ] Coverage dòng của `Pickory.Modules.SmartSearch` ít nhất 80%, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `recommendations`
