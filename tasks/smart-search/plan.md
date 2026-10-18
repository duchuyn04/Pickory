# Implementation Plan: smart-search

> **Trạng thái: đã bỏ ngày 2026-10-14.** Người dùng thu hẹp phần AI cá nhân hóa về một tính năng: chatbot tư vấn `shopping-assistant`.
>
> Tìm theo từ khóa cơ bản chuyển sang `catalog` (`GET /api/products?q=`). Hệ thống không còn tìm sản phẩm theo ý nghĩa.
>
> File này chỉ giữ để tra cứu lịch sử. Không làm task nào trong file này. Không tick mục nào trong file này.

## Overview

Module `smart-search` cho khách tìm sản phẩm bằng câu tự nhiên tiếng Việt. Khách gõ có dấu, không dấu, hoặc gõ mã model. Module kết hợp tìm toàn văn, tìm vector và bộ lọc phân tích từ câu tìm. Module cũng là công cụ tìm sản phẩm của `shopping-assistant` và nguồn sản phẩm tương tự của `recommendations`.

`smart-search` phụ thuộc `catalog`, `ai-gateway` và `activity-tracking`. Module đi sau `activity-tracking` và trước `recommendations`, theo `spec/CAPABILITY-MAP.md`.

Spec gốc: `spec/SPEC.md`, `spec/SPEC-smart-search.md`. Spec còn viết interface theo bản Node. Plan này chuyển interface sang .NET theo `tasks/dotnet-migration/plan.md`.
Danh sách task chi tiết: `tasks/smart-search/todo.md`.

## Phần đã phủ bởi plan cũ

Các phần dưới đây đã xong trong plan cũ. Plan này dùng lại, không lặp task.

| Phần | Plan cũ | Trạng thái |
| --- | --- | --- |
| `ICatalogReader.ListForIndexingAsync`, `GetAttributeDefinitionsAsync`, `ListProductsAsync`, `GetSkusAsync` | `tasks/catalog/todo.md`, CAT-10 | xong |
| `ProductChangedEvent` và `StockChangedEvent` phát sau commit | `tasks/catalog/todo.md`, CAT-03 | xong |
| `DescriptionText.ToPlainText` (bản .NET của `descriptionToPlainText`) | `tasks/dotnet-migration/todo.md`, DN-07 | xong, nhưng nằm trong `Pickory.Modules.Catalog`, chưa nằm trong Contracts |
| Dữ liệu seed 30 sản phẩm, 5 danh mục | `tasks/catalog/todo.md`, CAT-12 | xong |
| `IAiGateway.EmbedAsync` có cache embedding, `ChatJsonAsync` có kiểm validator | `tasks/identity-ai-gateway/todo.md`, AI-06, AI-08; `tasks/dotnet-migration/todo.md`, DN-06 | xong |
| Mã tính năng `smart-search.parse-query` và `smart-search.index`, bậc ngân sách cao | `tasks/identity-ai-gateway/plan.md`, quyết định 9 | xong |
| Extension `vector` trong schema `public` | `tasks/dotnet-migration/plan.md`, mốc DN-01 | xong |
| Package `Pgvector.EntityFrameworkCore` | `tasks/dotnet-migration/plan.md`, bảng thay thế công nghệ | đã duyệt |
| `PiiRedactor` trong `Pickory.Shared/Privacy/` | `tasks/identity-ai-gateway/todo.md`, AI-01; DN-06 | xong |
| Quyền `products:write` và `reports:read` trong `Permissions` | `tasks/identity-ai-gateway/todo.md`, ID-01 | xong |
| Chuyển `RateLimiter` sang `Pickory.Shared` | `tasks/shipping/todo.md`, SHIP-08 | chưa xong, là điều kiện trước |

## Điều kiện trước

1. `activity-tracking` xong `IServerEventTracker` và `IProfileReader` (xem `tasks/activity-tracking/plan.md`).
2. SHIP-08 xong, để `RateLimiter` nằm trong `Pickory.Shared`.
3. Plan đổi route cũ sang REST đã xong. Plan đó chạy trước `commerce`.
4. Người dùng trả lời các câu hỏi mở về Contracts của `catalog` (câu 1, 2, 3). Task phụ thuộc ghi rõ câu hỏi chặn nó.

## Đồ thị phụ thuộc

```
SRCH-01 chuẩn hóa câu tìm, bảng đồng nghĩa, nhận diện mã model (logic thuần)
SRCH-02 parser giá và luật quyết định gọi LLM (logic thuần)
SRCH-03 RRF, điều chỉnh điểm, nới bộ lọc, ưu tiên nguồn bộ lọc (logic thuần)
 └── SRCH-04 khung module, Contracts, schema, migration
      ├── SRCH-05 fake embedding theo hash văn bản (ai-gateway, chờ duyệt)
      ├── SRCH-06 văn bản embed và contentHash
      │    └── SRCH-07 hàng đợi index, job 30 giây, lệnh và route index lại ── (cần SRCH-05)
      │         ├── SRCH-08 đồng bộ giá và tồn kho từ StockChangedEvent
      │         └── SRCH-09 tìm toàn văn và trigram
      │              ├── SRCH-10 phân tích bằng luật (cần câu hỏi mở 1, 4)
      │              │    └── SRCH-11 phân tích bằng LLM và cache 7 ngày
      │              │         └── SRCH-12 SearchAsync: 3 nhánh, gộp, cá nhân hóa, nới bộ lọc
      │              │              ├── SRCH-13 GET /api/search ──────────── SRCH-UI2 trang /search
      │              │              └── SRCH-14 SimilarToAsync
      │              └── SRCH-15 gợi ý khi gõ ─────────────────────────── SRCH-UI1 ô tìm trên header
      └── SRCH-16 insights cho back-office (cần câu hỏi mở 6) ─────────── SRCH-UI3 màn insights
SRCH-17 bộ đánh giá 50 câu, lệnh search:eval
SRCH-18 lệnh search:benchmark đo p95
SRCH-E2E1 luồng chip bộ lọc
 └── SRCH-19 nghiệm thu
```

`SRCH-07` cần `SRCH-05`, vì test tìm vector cần vector khác nhau cho văn bản khác nhau. `SRCH-12` cần `IServerEventTracker` và `IProfileReader` của `activity-tracking`.

## Architecture Decisions

1. **Cấu trúc theo `tasks/dotnet-migration/plan.md`.** Hai project mới: `Pickory.Modules.SmartSearch` và `Pickory.Modules.SmartSearch.Contracts`. `SmartSearchModule.cs` có `AddSmartSearchModule` và `MapSmartSearchEndpoints`. `Pickory.ArchitectureTests` thêm luật cho module mới.

2. **Chuyển interface từ TypeScript sang .NET.** Spec viết `productSearch` bằng TypeScript. Bản .NET đặt trong Contracts như sau:
   - `IProductSearch.SearchAsync(SearchRequest request, CancellationToken cancellationToken)` trả `Task<SearchResult>`.
   - `SearchRequest` có `Query`, `Filters`, `Page`, `PageSize`, `Actor`, `Source`.
   - `SearchResult` có `Items`, `AppliedFilters`, `Relaxed`, `Total`.
   - `SearchSource` là enum `SearchPage`, `ShoppingAssistant`, thay union chuỗi `'search-page' | 'shopping-assistant'`.
   - `AppliedFilter` có `Kind`, `Value`, `Label` và `Origin`. `FilterOrigin` là enum `User`, `Rule`, `Llm`.
   - `IProductSearch.SimilarToAsync(productIds, k, filters, cancellationToken)` trả `Task<IReadOnlyList<SimilarProduct>>`. `SimilarProduct` có `ProductId` và `Similarity`.
   - `SearchFilters` của spec dùng lại `ProductFilters` của `Pickory.Modules.Catalog.Contracts`. Kết quả dùng lại `ProductSummary`. `Actor` lấy từ `Pickory.Modules.Identity.Contracts`.

3. **Bảng đồng nghĩa nằm trong code.** `Normalization/SynonymTable.cs` thay `smart-search/synonyms.ts`. Bảng là dữ liệu tĩnh, có unit test cho từng cặp.

4. **Từ khóa danh mục nằm trong module này.** Bảng `categories` của `catalog` không có cột từ khóa. Spec yêu cầu khớp "tên và từ khóa của danh mục". Module giữ bảng tĩnh `Parsing/CategoryKeywordTable.cs`, khóa theo slug danh mục. Xem câu hỏi mở 4.

5. **Phân tích bằng LLM dùng `IAiGateway.ChatJsonAsync<ParsedQuery>`.** `ParsedQueryValidator` (FluentValidation) thay schema Zod `parsedQuerySchema`. Feature là `AiFeature.SmartSearchParseQuery`. `CallContext` không có `ActorKey`, vì prompt phân tích không được gắn với người dùng.
   - Module hủy lời gọi sau 2,5 giây bằng `CancellationTokenSource` liên kết.
   - `AiUnavailableException`, `AiBudgetExceededException`, `AiInvalidOutputException` và timeout đều trả về kết quả của luật. Khách không thấy lỗi.
   - Kết quả LLM đi qua bước đối chiếu với `ICatalogReader`. Danh mục, thuộc tính hoặc giá trị không có thật bị bỏ.

6. **Cache phân tích 7 ngày trong `search_query_cache`.** Khóa là câu tìm đã chuẩn hóa. Spec yêu cầu xóa cache khi danh mục hoặc bộ thuộc tính đổi. `catalog` chưa phát sự kiện cho hai thay đổi này. Xem câu hỏi mở 3.

7. **Tìm toàn văn trên bảng riêng `product_search_docs`.** Module chỉ ghi bảng của chính nó.
   - Migration bật `unaccent` và `pg_trgm`. Migration tạo hàm `smart_search_unaccent` có nhãn `IMMUTABLE`, vì chỉ mục không nhận hàm `unaccent` gốc.
   - `search_vector` là `tsvector` cấu hình `simple`, dựng từ text không dấu.
   - Chỉ mục GIN cho `search_vector`. Chỉ mục trigram GIN cho `name_unaccent` và `sku_codes`.
   - Bảng thêm cột `attributes` (`jsonb`) và `parent_category_id`. Spec rút gọn không có hai cột này, nhưng bộ lọc thuộc tính và danh mục cha cần chúng.

8. **Tìm vector trên `product_embeddings`.** Cột `embedding` là `vector(1536)` qua `Pgvector.EntityFrameworkCore`. Migration tạo chỉ mục HNSW `vector_cosine_ops` bằng SQL. Cột `model` lưu `AppConfig.EmbeddingModel`. Đổi model thì chạy `search:reindex`.

9. **Hàng đợi index trong DB.** Trình nghe `ProductChangedEvent` ghi một dòng `search_index_jobs`, unique theo `productId`. Nhiều thay đổi của cùng sản phẩm gộp vào một dòng.
   - `SearchIndexWorker` chạy mỗi 30 giây, theo mẫu `ImageDeletionWorker` của `catalog`.
   - Test gọi thẳng `SearchIndexProcessor`, không chờ timer.
   - Job lỗi tăng `attempts`, ghi `lastError` và thử lại theo backoff tăng dần, tối đa 8 lần.
   - Sản phẩm không còn trong `ListForIndexingAsync`, hoặc có `Status` là `discontinued`, bị xóa khỏi hai bảng.

10. **`contentHash` chỉ đổi khi nội dung đổi.** Hash là SHA-256 của văn bản embed. Văn bản chứa khoảng giá làm tròn xuống bội số 1.000.000 đ, không chứa giá chính xác hay tồn kho. Đổi giá trong cùng khoảng làm tròn không gọi embed.

11. **Text mô tả lấy từ Contracts.** `ProductDetail.Description` là `JsonElement`. `DescriptionText.ToPlainText` nằm trong `Pickory.Modules.Catalog`, module này không tham chiếu được. Xem câu hỏi mở 2. Module cắt text còn tối đa 2.000 ký tự.

12. **Giá và tồn kho cập nhật không qua embed.** Trình nghe `StockChangedEvent` gọi `ICatalogReader.GetSkusAsync` để lấy `ProductId`. Sau đó trình nghe đọc lại sản phẩm và chỉ ghi `min_price`, `in_stock` của `product_search_docs`.

13. **`SearchAsync` chạy 3 nhánh song song bằng `Task.WhenAll`.** Ba nhánh là phân tích bằng LLM, tìm toàn văn và tìm vector. Luật phân tích chạy trước, vì luật nhanh và không gọi ra ngoài. Nhánh vector lỗi thì bỏ nhánh đó. Nhánh toàn văn luôn chạy.

14. **Gộp và xếp hạng là hàm thuần.** RRF dùng `k = 60` và tối đa 50 ứng viên mỗi nhánh. Còn hàng nhân 1,1. Cá nhân hóa cộng tối đa 10% điểm, chỉ khi `Actor.Consent.Personalization` là `true` và `IProfileReader` trả hồ sơ.

15. **Nới bộ lọc theo thứ tự cố định.** Kết quả rỗng thì bỏ bộ lọc thuộc tính trước, rồi bỏ bộ lọc giá. `SearchResult.Relaxed` là `true` khi đã nới. Spec không phân biệt nguồn của bộ lọc. Vì vậy bộ lọc khách chọn trên giao diện cũng được nới theo cùng thứ tự.

16. **Cache kết quả 5 phút trong bộ nhớ.** Khóa gồm câu tìm đã chuẩn hóa, bộ lọc và `actorKey`. `IMemoryCache` đủ cho MVP 1 instance. Chuyển trang đọc từ cache, không tính lại.

17. **Ghi sự kiện tìm qua `IServerEventTracker`.** Mỗi lần tìm ghi một sự kiện `search` với câu tìm đã qua `PiiRedactor`, số kết quả, bộ lọc đã hiểu, `Relaxed` và độ trễ. Log của module không chứa câu tìm gốc.

18. **Gợi ý khi gõ không gọi AI.** Gợi ý dùng trigram và tiền tố trên `name_unaccent`, cùng danh sách danh mục và thương hiệu. Danh sách này giữ trong `IMemoryCache` 5 phút.

19. **Giới hạn tần suất dùng `RateLimiter` của `Pickory.Shared`.** Khóa là `Actor.AnonymousId`. Tìm kiếm cho phép 30 lần mỗi phút. Gợi ý cho phép 120 lần mỗi phút. Vượt giới hạn trả 429 `TOO_MANY_REQUESTS`.

20. **Route theo REST mức tài nguyên.** Route của spec và route đề xuất:

   | Spec | Đề xuất | Lý do |
   | --- | --- | --- |
   | `GET /api/search` | giữ nguyên | Tài nguyên là danh sách kết quả tìm |
   | `GET /api/search/suggest` | `GET /api/search/suggestions` | Đường dẫn là danh từ |
   | `GET /api/admin/search/insights` | giữ nguyên | Đường dẫn là danh từ |
   | `POST /api/admin/search/reindex` | `POST /api/admin/search/reindex-jobs`, trả 202 | Route tạo một job index lại, không gọi hành động |

   Đây là điểm lệch spec. Xem câu hỏi mở 7.

21. **Lệnh dòng lệnh thay lệnh pnpm.** `pnpm search:reindex` thành `dotnet run --project apps/backend/src/Pickory.Api -- search:reindex`. `pnpm search:eval` thành `search:eval`. Lệnh mới `search:benchmark` đo p95 theo mẫu `catalog:benchmark`.

22. **Bộ đánh giá nằm trong module.** File `Evaluation/queries.json` thay `smart-search/eval/queries.json`. `search:eval` từ chối chạy khi thiếu `AI_LIVE_TESTS=1` hoặc thiếu API key. Phép tính Recall@10 và MRR là hàm thuần, có unit test.

23. **Fake embedding cần vector theo hash.** `FakeEmbeddingProvider` hiện tạo vector chỉ theo độ dài text. Hai text cùng độ dài cho cùng vector, nên test tìm vector không phân biệt được sản phẩm. Spec yêu cầu vector cố định từ hash của văn bản. `SRCH-05` sửa provider giả này, chờ người dùng duyệt (câu hỏi mở 5).

24. **Khách bỏ chip bằng tham số `ignore`.** Chip do luật hoặc LLM tạo không nằm trong query string. Khách bấm × thì frontend gửi lại câu tìm kèm `ignore=price` hoặc `ignore=category,brand`. Module bỏ qua bộ lọc phân tích thuộc loại đó. Spec không nêu cơ chế này, xem câu hỏi mở 8.

## Task List

Chi tiết từng task ở `tasks/smart-search/todo.md`.

### Phase 1: Logic thuần và nền tảng
- [ ] SRCH-01 · [ ] SRCH-02 · [ ] SRCH-03 · [ ] SRCH-04 · [ ] SRCH-05

### Checkpoint SR1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `SmartSearch` tham chiếu module khác ngoài Contracts
- [ ] Review với người dùng

### Phase 2: Index
- [ ] SRCH-06 · [ ] SRCH-07 · [ ] SRCH-08

### Checkpoint SR2
- [ ] Lệnh `search:reindex` index đủ 30 sản phẩm seed
- [ ] Sửa tên sản phẩm thì `product_search_docs` đổi sau một lượt job
- [ ] Review với người dùng

### Phase 3: Tìm kiếm
- [ ] SRCH-09 · [ ] SRCH-10 · [ ] SRCH-11 · [ ] SRCH-12 · [ ] SRCH-13 · [ ] SRCH-14 · [ ] SRCH-15 · [ ] SRCH-UI1 · [ ] SRCH-UI2

### Checkpoint SR3
- [ ] Khách tìm bằng câu không dấu và mã model trên dữ liệu seed, thấy chip bộ lọc
- [ ] Tắt API AI, tìm kiếm vẫn trả kết quả
- [ ] Review với người dùng

### Phase 4: Insights, đánh giá và nghiệm thu
- [ ] SRCH-16 · [ ] SRCH-UI3 · [ ] SRCH-17 · [ ] SRCH-18 · [ ] SRCH-E2E1 · [ ] SRCH-19

### Checkpoint cuối
- [ ] Mọi dòng Traceability đạt, trừ dòng ghi "khi có API key"
- [ ] Coverage dòng của `Pickory.Modules.SmartSearch` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `recommendations`

## Traceability: Success Criteria → Task

| Success Criteria (spec) | Task | Cách kiểm |
| --- | --- | --- |
| Bộ đánh giá 50 câu: chế độ đầy đủ đạt Recall@10 ít nhất 0,8 và cao hơn chế độ chỉ toàn văn ít nhất 0,15 | SRCH-17, SRCH-19 | `search:eval` với AI thật. Khi có API key. Không có key thì để mở |
| "laptop choi game duoi 25 trieu" chỉ trả laptop gaming giá không quá 25.000.000 đ | SRCH-10, SRCH-12, SRCH-E2E1 | Test tích hợp trên seed; E2E. Cần câu hỏi mở 1 chốt cách nhận diện "laptop gaming" |
| Gõ mã model hoặc gõ không dấu thì sản phẩm nằm trong top 3 | SRCH-09, SRCH-12 | Test tích hợp với PostgreSQL thật trên seed |
| Tắt API AI: tìm kiếm vẫn trả kết quả từ toàn văn và luật | SRCH-11, SRCH-12, SRCH-E2E1 | Test tích hợp với fake provider báo lỗi; E2E chạy không có key |
| p95 dưới 400 ms khi đã có cache phân tích; dưới 3 giây khi gọi LLM | SRCH-18, SRCH-19 | `search:benchmark`. Phần gọi LLM thật chỉ đo được khi có API key |
| Gợi ý khi gõ p95 dưới 100 ms, không gọi AI | SRCH-15, SRCH-18 | Test tích hợp kiểm fake provider không nhận request; `search:benchmark` |
| Sửa tên thì kết quả đổi trong 1 phút; đổi giá trong cùng khoảng làm tròn không gọi embed | SRCH-06, SRCH-07, SRCH-08 | Test tích hợp gọi processor; test cấu hình chu kỳ 30 giây; fake provider đếm request |
| Trang insights hiện đúng top câu tìm và câu không có kết quả | SRCH-16, SRCH-UI3 | Test tích hợp trên dữ liệu dựng sẵn; Vitest; ảnh chụp |
| Coverage ít nhất 80% | SRCH-19 | Coverage gộp unit và tích hợp, cách đo như shipping |

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
| --- | --- | --- |
| Seed không có danh mục "Laptop gaming" và không có thuộc tính GPU | Không đạt tiêu chí 2 và E2E của spec | Câu hỏi mở 1. Chưa chốt thì `SRCH-10` và `SRCH-E2E1` dừng |
| PostgreSQL không có bộ tách từ tiếng Việt | Tìm toàn văn bỏ sót từ ghép | Dùng `simple` kèm trigram. `search:eval` đo Recall trước khi nghiệm thu |
| Chưa có API key AI | Không đo được Recall@10 và p95 khi gọi LLM | Làm toàn bộ bằng fake provider. Hai tiêu chí để mở, giống mục sandbox của shipping |
| Fake embedding theo độ dài text | Test vector cho kết quả sai | `SRCH-05` đổi sang hash, chờ duyệt |
| Cache phân tích giữ danh mục đã xóa | LLM chọn bộ lọc không còn đúng | Bước đối chiếu với `ICatalogReader` chạy cả khi đọc từ cache |
| Cá nhân hóa đẩy sản phẩm kém liên quan lên đầu | Kết quả sai ý khách | Unit test chặn mức cộng vượt 10% |
| Câu tìm có SĐT hoặc email lọt vào log | Lộ dữ liệu cá nhân | Chỉ ghi câu đã qua `PiiRedactor`; test kiểm log |

## Câu hỏi mở

1. **"Laptop gaming" trong dữ liệu seed.** Seed của `catalog` chỉ có danh mục `Laptop`, không có danh mục con "Laptop gaming" và không có thuộc tính GPU. Spec cần chip "Laptop gaming" và kết quả "chỉ laptop gaming". Đề xuất: `catalog` thêm danh mục con "Laptop gaming" vào seed trong một task riêng của `catalog`. Phương án khác: thêm thuộc tính `gpu` lọc được cho `Laptop`.
2. **Text mô tả cho module khác.** Đề xuất chuyển `DescriptionText.ToPlainText` sang `Pickory.Modules.Catalog.Contracts`. Phương án khác: thêm trường `DescriptionText` vào `ProductDetail`. Đây là đổi Contracts, thuộc "Ask first".
3. **Danh sách và sự kiện danh mục.** Đề xuất thêm vào `ICatalogReader` hai hàm `ListCategoriesAsync` và `ListBrandsAsync`. Luật phân tích và gợi ý khi gõ cần hai hàm này. Đề xuất thêm `CategoryChangedEvent` vào `Pickory.Modules.Catalog.Contracts/Events/`, để xóa cache phân tích khi danh mục hoặc bộ thuộc tính đổi. Chưa có sự kiện thì cache chỉ hết hạn sau 7 ngày.
4. **Từ khóa danh mục.** Spec nói khớp "từ khóa của danh mục", nhưng `catalog` không có cột từ khóa. Đề xuất giữ bảng từ khóa tĩnh trong `smart-search` (quyết định 4). Phương án khác: `catalog` thêm cột từ khóa và màn nhập.
5. **Sửa `FakeEmbeddingProvider` của `ai-gateway`.** Đề xuất tạo vector từ SHA-256 của text, thay vì theo độ dài (quyết định 23). Việc này sửa code của module khác, nên cần người dùng duyệt.
6. **Nguồn dữ liệu của insights.** Spec không có bảng lưu câu tìm trong `smart-search`. `activity-tracking` chỉ lưu câu tìm của khách đã đồng ý. Đề xuất A: thêm hàm `GetSearchInsightsAsync` vào `IActivityMetrics`. Đề xuất B: `smart-search` lưu bảng tổng hợp theo ngày, không có định danh. Mỗi dòng gồm câu đã qua `PiiRedactor`, số lượt và số lượt không có kết quả. B phủ cả khách chưa đồng ý, nhưng thêm bảng ngoài spec.
7. **Route REST lệch spec.** Quyết định 20 đổi `suggest` thành `suggestions` và `reindex` thành `reindex-jobs`. Người dùng chốt trước `SRCH-07` và `SRCH-15`.
8. **Tham số `ignore`.** Spec cho khách bỏ chip, nhưng không nêu cách báo cho API. Đề xuất ở quyết định 24. Người dùng chốt trước `SRCH-13`.
9. **Câu hỏi mở của spec.** Bộ 50 câu đánh giá: người dùng gửi câu khách thật nếu có. Cá nhân hóa thứ tự kết quả giữ mặc định của spec (có, tối đa 10%). Tìm bằng giọng nói và hình ảnh không làm trong MVP.
